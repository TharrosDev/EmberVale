using System;
using Embervale.Core;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Dialogue;
using Embervale.Enemies;
using Embervale.Entities;
using Embervale.Settings;
using Godot;

namespace Embervale.Player;

/// <summary>
/// The camera's special views, driven entirely by gameplay events: a push-in and a lean toward whoever
/// is speaking, a brief framing of a boss's entrance, a slow pull-back when the player falls, a faint
/// lean toward a shrine or a door being looked at, and a pull-back hint while mounted. There are no
/// authored tracks; every view starts and ends on a signal the game already raises.
///
/// <para>It is an <see cref="ICameraLayer"/>, so it returns a <see cref="CameraNudge"/> and never
/// touches the camera. The rig sums it with every other layer and is the only thing that writes the
/// transform. The pure rules (recipes, priority, the look-at angles, when the player takes the camera
/// back) are in <see cref="SpecialViewMath"/>.</para>
///
/// <para>⚠️ <b>The aim ray reads the camera's forward, so every look-at here is small and eased and the
/// player's own look input wins.</b> Steering (mouse or stick past a threshold) zeroes a lean through a
/// short fade, and a conversation or an entrance keeps it zeroed until the view ends. The lean is
/// measured against the camera's REST forward (the pivot and the rig's rest position), never the
/// camera's live one, so this layer does not chase its own output.</para>
///
/// <para>⚠️ <b>Steps in <see cref="Sample"/>, not in a <c>_Process</c>.</b> A blocking menu pauses the
/// tree, and a dialogue is one, so a layer that advanced on its own clock would arrive at full push-in
/// while the rig was not sampling and then jump on the first frame back. Stepped by the rig's own
/// <c>dt</c>, a view only exists while the camera is actually being asked for it.</para>
/// </summary>
[GlobalClass]
public partial class CameraDirectorLayer : EntityComponent, ICameraLayer
{
    /// <summary>Seconds the yield to the player's steering takes to reach zero: quick, but not a cut.</summary>
    private const float YieldSeconds = 0.2f;

    /// <summary>Seconds of smoothing on a lean's angles, so a subject that moves does not jerk the camera.</summary>
    private const float LeanSmoothSeconds = 0.25f;

    /// <summary>How far up a subject's bounds the camera looks, as a fraction of the way from its middle
    /// to its top: high enough to put a person's face in frame, and a dragon's chest.</summary>
    private const float HeadBias = 0.6f;

    /// <summary>Seconds a focus lean waits after the player last steered, and how long it takes to
    /// return; a lean the player has shrugged off must not creep straight back.</summary>
    private const float FocusHoldSeconds = 0.8f;

    private const float FocusRecoverSeconds = 2f;

    private static readonly SpecialView[] Views =
    {
        SpecialView.Focus, SpecialView.Dialogue, SpecialView.BossIntro, SpecialView.Death,
    };

    /// <summary>What one view is doing this frame.</summary>
    private struct ViewState
    {
        public float Amount;
        public Vector2 Lean;
        public float Yield;
        public LookYield Steering;
    }

    /// <summary>Something to look at: a body and where on it, kept as an offset so the point follows
    /// the body when it moves.</summary>
    private struct LookTarget
    {
        public Node3D? Body;
        public Vector3 Offset;

        public readonly bool TryGetPoint(out Vector3 point)
        {
            if (Body != null && GodotObject.IsInstanceValid(Body) && Body.IsInsideTree())
            {
                point = Body.GlobalPosition + Offset;
                return true;
            }

            point = Vector3.Zero;
            return false;
        }

        public static LookTarget Of(Node3D body, float headBias, out Vector3 size)
        {
            Aabb bounds = BoundsOf(body);
            size = bounds.Size;
            Vector3 point = bounds.Size == Vector3.Zero
                ? body.GlobalPosition + (Vector3.Up * 1.5f)
                : SpecialViewMath.LookPoint(bounds.Position, bounds.End, headBias);
            return new LookTarget { Body = body, Offset = point - body.GlobalPosition };
        }
    }

    // Indexed by (int)SpecialView; slot 0 (None) is unused.
    private readonly ViewState[] _views = new ViewState[5];

    private PlayerCameraRig? _rig;
    private InteractionSensor? _sensor;
    private SettingsService? _settings;

    private LookTarget _dialogue;
    private bool _dialogueOn;
    private LookTarget _boss;
    private float _bossSeconds;
    private float _deathClock = -1f;
    private float _mount;

    private LookTarget _focus;
    private bool _focusOk;
    private bool _focusReady;
    private ulong _focusId;
    private float _focusDwell;

    private float _mousePixels;

    protected override void OnInitialize()
    {
        _rig = Entity!.GetComponent<PlayerCameraRig>();
        _sensor = Entity.GetComponent<InteractionSensor>();
        _settings = ServiceLocator.Instance is { } locator && locator.TryGet(out SettingsService settings)
            ? settings
            : null;

        for (int i = 0; i < _views.Length; i++)
        {
            _views[i].Yield = 1f;
            _views[i].Steering = new LookYield();
        }

        EventBus.Instance?.Subscribe<DialogueStartedEvent>(OnDialogueStarted);
        EventBus.Instance?.Subscribe<DialogueEndedEvent>(OnDialogueEnded);
        EventBus.Instance?.Subscribe<BossEncounterStartedEvent>(OnBossStarted);
        EventBus.Instance?.Subscribe<BossWithdrewEvent>(OnBossWithdrew);
        EventBus.Instance?.Subscribe<EntityDiedEvent>(OnEntityDied);
    }

    protected override void OnTeardown()
    {
        EventBus.Instance?.Unsubscribe<DialogueStartedEvent>(OnDialogueStarted);
        EventBus.Instance?.Unsubscribe<DialogueEndedEvent>(OnDialogueEnded);
        EventBus.Instance?.Unsubscribe<BossEncounterStartedEvent>(OnBossStarted);
        EventBus.Instance?.Unsubscribe<BossWithdrewEvent>(OnBossWithdrew);
        EventBus.Instance?.Unsubscribe<EntityDiedEvent>(OnEntityDied);
    }

    /// <summary>Counts mouse motion between samples, which is how the layer knows the player is
    /// steering. Mouse look is event-driven, so there is nothing to poll.</summary>
    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventMouseMotion motion &&
            Godot.Input.MouseMode == Godot.Input.MouseModeEnum.Captured)
        {
            _mousePixels += motion.Relative.Length();
        }
    }

    public CameraNudge Sample(float dt, in CameraSnapshot snapshot)
    {
        dt = Math.Max(dt, 0f);
        CameraComfort comfort = CameraComfort.From(_settings?.Current);

        // A cinematic lock (the boss intro) suspends look input, so nothing the player does then is
        // steering and the lean holds for the whole beat.
        bool steering = !UiState.MenuOpen &&
            SpecialViewMath.IsSteering(SpecialViewMath.LookMagnitude(_mousePixels, StickDeflection()));
        _mousePixels = 0f;

        if (_deathClock >= 0f)
        {
            _deathClock += dt;
        }

        if (_bossSeconds > 0f)
        {
            _bossSeconds -= dt;
        }

        bool death = SpecialViewMath.DeathHolding(_deathClock);
        bool boss = _bossSeconds > 0f && _boss.TryGetPoint(out _);
        bool dialogue = _dialogueOn && _dialogue.TryGetPoint(out _);
        bool focus = ResolveFocus(dt, snapshot);
        SpecialView winner = SpecialViewMath.Choose(death, boss, dialogue, focus);

        float distance = 1f;
        float fov = 0f;
        float drop = 0f;
        Vector2 lean = Vector2.Zero;

        foreach (SpecialView view in Views)
        {
            ref ViewState state = ref _views[(int)view];
            ViewRecipe recipe = ViewRecipe.For(view);
            bool on = winner == view;

            state.Amount = recipe.Advance(state.Amount, on, dt);
            if (state.Amount <= 0f && !on)
            {
                state.Lean = Vector2.Zero;
                state.Yield = 1f;
                state.Steering.Reset();
                continue;
            }

            float eased = SpecialViewMath.Ease(state.Amount);
            distance *= recipe.Distance(eased);
            fov += recipe.FovDegrees * eased;
            drop += recipe.DropMetres * eased;

            if (recipe.MaxYaw > 0f)
            {
                Vector2 wanted = on ? LeanToward(view, recipe, state.Lean) : state.Lean;
                state.Lean = state.Lean.Lerp(wanted, CameraRigMath.Damp(dt, LeanSmoothSeconds));

                bool latched = view != SpecialView.Focus;
                state.Steering.Update(dt, steering && on, FocusHoldSeconds, latched ? 0f : FocusRecoverSeconds);
                state.Yield += (state.Steering.Value - state.Yield) * CameraRigMath.Damp(dt, YieldSeconds);
                lean += state.Lean * (eased * state.Yield);
            }
        }

        _mount = SpecialViewMath.Advance(
            _mount, snapshot.Mounted, dt, SpecialViewMath.MountedBlendSeconds, SpecialViewMath.MountedBlendSeconds);
        distance *= SpecialViewMath.MountedDistance(SpecialViewMath.Ease(_mount));

        return new CameraNudge(
            new Vector3(0f, drop, 0f),
            new Vector3(lean.X, lean.Y, 0f),
            fov * comfort.FovKick,
            distance);
    }

    /// <summary>The (pitch, yaw) that would turn the camera toward the view's subject, or the current
    /// lean when the subject cannot be resolved this frame.</summary>
    private Vector2 LeanToward(SpecialView view, in ViewRecipe recipe, Vector2 current)
    {
        LookTarget target = view switch
        {
            SpecialView.Dialogue => _dialogue,
            SpecialView.BossIntro => _boss,
            SpecialView.Focus => _focus,
            _ => default,
        };

        if (_rig?.CameraPivot is not { } pivot || !GodotObject.IsInstanceValid(pivot) || !pivot.IsInsideTree() ||
            !target.TryGetPoint(out Vector3 point))
        {
            return current;
        }

        // Pivot space with the rest position taken off: the direction from where the camera SITS to
        // the subject, in the frame the rig's own rotation is applied in.
        Vector3 direction = pivot.ToLocal(point) - _rig.CameraRestPosition;
        return SpecialViewMath.LookAngles(direction, recipe.MaxPitch, recipe.MaxYaw);
    }

    /// <summary>Whether a focus lean is in force: the same interactable must have stayed under the
    /// crosshair for the dwell, be big enough to be worth leaning at, and the camera must be in plain
    /// exploration. The subject is kept after focus is lost so the lean can fade rather than snap.</summary>
    private bool ResolveFocus(float dt, in CameraSnapshot snapshot)
    {
        if (_sensor?.FocusedEntity is not { } entity || _sensor.FocusedInteractable == null ||
            snapshot.Context != CameraContext.Exploration)
        {
            _focusId = 0;
            _focusReady = false;
            return false;
        }

        Node3D body = entity.Body;
        if (!GodotObject.IsInstanceValid(body))
        {
            _focusId = 0;
            _focusReady = false;
            return false;
        }

        ulong id = body.GetInstanceId();
        if (id != _focusId)
        {
            _focusId = id;
            _focusDwell = 0f;
            _focusReady = false;
        }

        _focusDwell += dt;
        if (!_focusReady && _focusDwell >= SpecialViewMath.FocusDwellSeconds)
        {
            _focusReady = true;
            _focus = LookTarget.Of(body, 0.5f, out Vector3 size);
            _focusOk = SpecialViewMath.FocusLeanApplies(snapshot.Context, size);
        }

        return _focusReady && _focusOk;
    }

    private static float StickDeflection() => Godot.Input.GetVector(
        GameInput.LookLeft, GameInput.LookRight, GameInput.LookUp, GameInput.LookDown).Length();

    /// <summary>The union of every visible mesh's bounds under <paramref name="root"/>, in world space.
    /// Zero-sized when there is no mesh. Walked once per subject, when its view starts, so the child
    /// enumeration is not on the per-frame path.</summary>
    private static Aabb BoundsOf(Node3D root)
    {
        Aabb bounds = default;
        bool any = false;
        Gather(root, ref bounds, ref any, 0);
        return bounds;
    }

    private static void Gather(Node node, ref Aabb bounds, ref bool any, int depth)
    {
        if (depth > 8)
        {
            return;
        }

        if (node is MeshInstance3D { Visible: true, Mesh: not null } mesh)
        {
            Aabb world = mesh.GlobalTransform * mesh.GetAabb();
            bounds = any ? bounds.Merge(world) : world;
            any = true;
        }

        foreach (Node child in node.GetChildren())
        {
            Gather(child, ref bounds, ref any, depth + 1);
        }
    }

    private bool IsOurs(IEntity? entity) => entity != null && ReferenceEquals(entity, Entity);

    private void OnDialogueStarted(DialogueStartedEvent e)
    {
        // A monologue names the player as its own speaker (the boss defeat conversation does): there
        // is nobody to lean toward and the push-in would frame the back of the player's head.
        if (!IsOurs(e.Player) || IsOurs(e.Speaker) || !GodotObject.IsInstanceValid(e.Speaker.Body))
        {
            return;
        }

        _dialogue = LookTarget.Of(e.Speaker.Body, HeadBias, out _);
        _dialogueOn = true;
        _views[(int)SpecialView.Dialogue].Steering.Reset();
    }

    private void OnDialogueEnded(DialogueEndedEvent e)
    {
        if (IsOurs(e.Player))
        {
            _dialogueOn = false;
        }
    }

    private void OnBossStarted(BossEncounterStartedEvent e)
    {
        if (Entity?.Body is not { } player || !GodotObject.IsInstanceValid(e.Boss.Body) ||
            !SpecialViewMath.BossFramingApplies(e.Boss.Body.GlobalPosition.DistanceTo(player.GlobalPosition)))
        {
            return;
        }

        _boss = LookTarget.Of(e.Boss.Body, HeadBias, out _);
        _bossSeconds = SpecialViewMath.BossHoldSeconds(e.Boss.GetComponent<BossController>()?.Fight?.IntroLockSeconds ?? 2.5f);
        _views[(int)SpecialView.BossIntro].Steering.Reset();
    }

    private void OnBossWithdrew(BossWithdrewEvent e) => EndBossFraming(e.Boss);

    private void OnEntityDied(EntityDiedEvent e)
    {
        if (IsOurs(e.Entity))
        {
            _deathClock = 0f;
        }

        EndBossFraming(e.Entity);
    }

    private void EndBossFraming(IEntity boss)
    {
        if (_boss.Body != null && ReferenceEquals(_boss.Body, boss.Body))
        {
            _bossSeconds = 0f;
        }
    }
}
