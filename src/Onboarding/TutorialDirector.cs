using Embervale.Combat.Actions;
using Embervale.Combat;
using Embervale.Core;
using Embervale.Core.Diagnostics;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Interaction;
using Embervale.Magic;
using Embervale.Movement;
using Embervale.Player;
using Embervale.Save;
using Embervale.Settings;
using Embervale.UI;
using Godot;

namespace Embervale.Onboarding;

/// <summary>
/// Teaches the game's verbs by watching the player play them (Phase 33B basics, 33C town verbs). It
/// is deliberately
/// <em>observational</em>: it never blocks input, freezes the world, or gates progress on a hint
/// being obeyed. A hint asks for a verb; when the player performs it — for real, through the same
/// components and events combat already raises — the hint clears and the next one appears. A veteran
/// who ignores every hint loses nothing, and one who turns tutorials off in Settings never sees one.
///
/// Completion is detected from actual game state rather than raw keypresses wherever the distinction
/// matters: an attack counts when the weapon component reports a swing, a dodge when locomotion is
/// genuinely dashing. Pressing a key with no stamina teaches nothing, so it doesn't count. The held
/// verbs (moving, guarding) are polled; the discrete ones (interacting, opening a panel, casting)
/// arrive as events, which are themselves proof the verb happened.
///
/// Progress persists (<see cref="ISaveable"/>), so reloading never re-teaches a verb the player has
/// already shown they know.
/// </summary>
public partial class TutorialDirector : Node, ISaveable
{
    public string SaveId => "tutorial";

    /// <summary>Total mouse travel (pixels) that counts as having looked around.</summary>
    [Export] public float LookPixels { get; set; } = 700f;

    /// <summary>Seconds of held movement that counts as having walked.</summary>
    [Export] public float MoveSeconds { get; set; } = 1.2f;

    /// <summary>Seconds of sprinting that counts as having sprinted.</summary>
    [Export] public float SprintSeconds { get; set; } = 0.7f;

    /// <summary>Seconds of held guard that counts as having blocked.</summary>
    [Export] public float BlockSeconds { get; set; } = 0.5f;

    /// <summary>Seconds a completed hint lingers before the next appears, so hints don't flash by.</summary>
    [Export] public float StepGapSeconds { get; set; } = 1.1f;

    private TutorialStep _step = TutorialStep.None;
    private PlayerCharacter? _player;
    private float _lookTravel;
    private double _moveHeld;
    private double _sprintHeld;
    private double _blockHeld;
    private double _gap;
    private bool _finished;

    /// <summary>The hint the player is currently being shown (<see cref="TutorialStep.None"/> when
    /// the sequence is done, skipped, or waiting between hints).</summary>
    public TutorialStep Step => _step;

    public bool IsFinished => _finished;

    public override void _EnterTree()
    {
        ServiceScope.RegisterOwned(this, this);
        SaveManager.Instance?.Register(this);
    }

    public override void _ExitTree()
    {
        EventBus? bus = EventBus.Instance;
        if (bus != null)
        {
            bus.Unsubscribe<InteractionPerformedEvent>(OnInteracted);
            bus.Unsubscribe<UiPanelToggledEvent>(OnPanelToggled);
            bus.Unsubscribe<SpellCastEvent>(OnSpellCast);
        }

        SaveManager.Instance?.Unregister(this);
    }

    public override void _Ready()
    {
        // The town verbs are discrete moments rather than held inputs, so they arrive as events.
        EventBus bus = EventBus.Instance;
        bus?.Subscribe<InteractionPerformedEvent>(OnInteracted);
        bus?.Subscribe<UiPanelToggledEvent>(OnPanelToggled);
        bus?.Subscribe<SpellCastEvent>(OnSpellCast);

        // Settings win outright: a player who turned tutorials off gets none, and no saved progress
        // can turn them back on.
        if (!TutorialsEnabled())
        {
            _finished = true;
            return;
        }

        Begin(TutorialScript.First);
    }

    /// <summary>Ends the sequence immediately — the Settings toggle and the `tutorial skip` command.</summary>
    public void Skip()
    {
        if (_finished)
        {
            return;
        }

        // Skip does not route through Begin, so it has to drop the pending gap itself. Skipping
        // inside the ~1 s between two hints otherwise let the queued Begin fire afterwards and put a
        // hint back on screen for a tutorial that had just been turned off — and because Complete()
        // refuses to act once _finished is set, that hint could never be cleared again.
        _gap = 0d;
        SetStep(TutorialStep.None);
        _finished = true;
        EventBus.Instance?.Publish(new TutorialFinishedEvent(Skipped: true));
        Log.Info("Tutorial skipped.");
    }

    /// <summary>Restarts the sequence from the first hint (authoring/QA).</summary>
    public void Restart()
    {
        _finished = false;
        Begin(TutorialScript.First);
    }

    public override void _Input(InputEvent @event)
    {
        // Looking is the one verb with no bound action to watch, so it's measured from raw mouse
        // travel. Only counted while the player actually has the camera.
        if (_step == TutorialStep.Look && @event is InputEventMouseMotion motion && CanObserve())
        {
            _lookTravel += motion.Relative.Length();
            if (_lookTravel >= LookPixels)
            {
                Complete(TutorialStep.Look);
            }
        }
    }

    public override void _Process(double delta)
    {
        // Between hints: let the completed one breathe before the next appears.
        if (_gap > 0d)
        {
            _gap -= delta;
            if (_gap <= 0d)
            {
                Begin(TutorialScript.Next(_lastCompleted));
            }

            return;
        }

        if (_finished || _step == TutorialStep.None || !CanObserve())
        {
            return;
        }

        switch (_step)
        {
            case TutorialStep.Move:
                Accumulate(ref _moveHeld, MovementHeld(), delta, MoveSeconds, TutorialStep.Move);
                break;

            case TutorialStep.Sprint:
                // Sprinting means moving fast, not merely holding shift on the spot.
                Accumulate(
                    ref _sprintHeld,
                    MovementHeld() && Godot.Input.IsActionPressed(GameInput.Sprint),
                    delta,
                    SprintSeconds,
                    TutorialStep.Sprint);
                break;

            case TutorialStep.Attack:
                // A real swing, not a keypress: a click with no stamina teaches nothing.
                if (Player()?.GetComponent<CharacterActionComponent>()?.IsCommitted == true)
                {
                    Complete(TutorialStep.Attack);
                }

                break;

            case TutorialStep.Block:
                Accumulate(
                    ref _blockHeld,
                    Player()?.GetComponent<CombatComponent>()?.IsBlocking == true,
                    delta,
                    BlockSeconds,
                    TutorialStep.Block);
                break;

            case TutorialStep.Dodge:
                if (Player()?.GetComponent<LocomotionComponent>()?.IsDashing == true)
                {
                    Complete(TutorialStep.Dodge);
                }

                break;
        }
    }

    // --- Event-driven verbs (33C) --------------------------------------------

    // These deliberately do NOT go through CanObserve: opening the inventory sets UiState.MenuOpen
    // by definition, so requiring "no menu open" would make the inventory hint impossible to clear.
    // They are already proof the verb happened, which is the whole point of listening for them.

    private void OnInteracted(InteractionPerformedEvent e)
    {
        if (e.Instigator is PlayerCharacter)
        {
            Complete(TutorialStep.Interact);
        }
    }

    private void OnPanelToggled(UiPanelToggledEvent e)
    {
        if (!e.Open)
        {
            return;
        }

        switch (e.Panel)
        {
            case InventoryPanel:
                Complete(TutorialStep.Inventory);
                break;
            case QuestLogPanel:
                Complete(TutorialStep.Journal);
                break;
        }
    }

    private void OnSpellCast(SpellCastEvent e)
    {
        if (e.Caster is PlayerCharacter)
        {
            Complete(TutorialStep.Cast);
        }
    }

    // --- Sequencing ----------------------------------------------------------

    private TutorialStep _lastCompleted = TutorialStep.None;

    private void Begin(TutorialStep step)
    {
        _lookTravel = 0f;
        _moveHeld = 0d;
        _sprintHeld = 0d;
        _blockHeld = 0d;

        // The pending between-hints gap is progress state like the accumulators above, and has to
        // clear with them: it holds a queued Begin(Next(_lastCompleted)) that would otherwise fire a
        // moment later and overwrite whatever step this call just set. That is what made Restart()
        // land on the first hint and then jump back to wherever the sequence had got to.
        _gap = 0d;

        SetStep(step);
        if (step == TutorialStep.None && !_finished)
        {
            _finished = true;
            EventBus.Instance?.Publish(new TutorialFinishedEvent(Skipped: false));
            Log.Info("Tutorial complete.");
        }
    }

    private void Complete(TutorialStep step)
    {
        // An event can arrive for a verb that isn't currently being taught (a player who casts
        // before the magic hint). That is not a completion — the hint is still owed — so only the
        // step on screen can be completed.
        if (_step != step || _finished)
        {
            return;
        }

        _lastCompleted = step;
        EventBus.Instance?.Publish(new TutorialStepCompletedEvent(step));
        SetStep(TutorialStep.None);
        _gap = StepGapSeconds;
    }

    private void SetStep(TutorialStep step)
    {
        if (_step == step)
        {
            return;
        }

        _step = step;
        EventBus.Instance?.Publish(new TutorialStepChangedEvent(step));
    }

    private void Accumulate(ref double held, bool active, double delta, float target, TutorialStep step)
    {
        if (!active)
        {
            return;
        }

        held += delta;
        if (held >= target)
        {
            Complete(step);
        }
    }

    // --- Environment ---------------------------------------------------------

    /// <summary>Only observe while the player actually has control: not in a menu, not mid-prologue,
    /// not paused. Otherwise mouse-look in a panel would "teach" looking.</summary>
    private bool CanObserve() =>
        !_finished && GameManager.Instance is { IsPlaying: true } && !UiState.MenuOpen;

    private static bool MovementHeld() =>
        Godot.Input.IsActionPressed(GameInput.MoveForward) ||
        Godot.Input.IsActionPressed(GameInput.MoveBack) ||
        Godot.Input.IsActionPressed(GameInput.MoveLeft) ||
        Godot.Input.IsActionPressed(GameInput.MoveRight);

    private static bool TutorialsEnabled() =>
        ServiceLocator.Instance == null ||
        !ServiceLocator.Instance.TryGet(out SettingsService settings) ||
        settings.Current.ShowTutorials;

    private PlayerCharacter? Player()
    {
        if (_player != null && IsInstanceValid(_player))
        {
            return _player;
        }

        _player = null;
        if (ServiceLocator.Instance != null && ServiceLocator.Instance.TryGet(out PlayerCharacter found))
        {
            _player = found;
        }

        return _player;
    }

    // --- Persistence ---------------------------------------------------------

    public Godot.Collections.Dictionary Save() => new()
    {
        ["step"] = (int)_step,
        ["finished"] = _finished,
    };

    public void Load(Godot.Collections.Dictionary data)
    {
        _finished = data.TryGetValue("finished", out Variant done) && done.AsBool();
        _gap = 0d;

        if (_finished || !TutorialsEnabled())
        {
            _finished = true;
            SetStep(TutorialStep.None);
            return;
        }

        // Resume where the save left off. An unknown step falls out of the script and simply ends
        // the sequence rather than stranding the player on a hint that no longer exists.
        var saved = (TutorialStep)(data.TryGetValue("step", out Variant step) ? step.AsInt32() : 0);
        Begin(TutorialScript.IndexOf(saved) >= 0 ? saved : TutorialStep.None);
    }
}
