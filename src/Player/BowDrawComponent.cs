using Embervale.Combat;
using Embervale.Combat.Actions;
using Embervale.Core.Events;
using Embervale.Entities;
using Embervale.Progression;
using Embervale.Stats;
using Godot;

namespace Embervale.Player;

/// <summary>
/// The player's draw: how long the attack button was held through the bow's startup. Driven each
/// physics frame by <see cref="PlayerInputRouter"/>'s ranged block; read once by
/// <see cref="RangedAttack"/> on the release frame through <see cref="Take"/>.
///
/// <para>The arrow still leaves on the action's release frame (see <see cref="RangedMath"/>); this
/// only decides how strong that shot is. It holds no persisted state.</para>
///
/// <para>Also owns the small draw ring around the crosshair, so the player can read how far back the
/// string is, and flashes a marker when an arrow lands a headshot.</para>
/// </summary>
[GlobalClass]
public partial class BowDrawComponent : EntityComponent
{
    private CharacterActionComponent? _action;
    private StatsComponent? _stats;
    private RangedMath.Draw _draw;
    private bool _active;
    private BowDrawReticle? _reticle;

    /// <summary>The live draw, 0..1. Zero when no draw is in progress.</summary>
    public float Charge => _active ? RangedMath.Charge(_draw.Held) : 0f;

    /// <summary>True while a draw is in progress.</summary>
    public bool IsDrawing => _active;

    /// <summary>True while the arms are out of stamina and the string is not coming back further.</summary>
    public bool Strained => _active && _draw.Strained;

    protected override void OnInitialize()
    {
        _action = Entity!.GetComponent<CharacterActionComponent>();
        _stats = Entity.GetComponent<StatsComponent>();
        EventBus.Instance?.Subscribe<ArrowHitEvent>(OnArrowHit);

        // A headless run has no screen to draw on, and the probes build this component bare.
        if (DisplayServer.GetName() != "headless")
        {
            var layer = new CanvasLayer { Layer = 30, Name = "DrawLayer" };
            _reticle = new BowDrawReticle { Name = "DrawRing" };
            layer.AddChild(_reticle);
            AddChild(layer);
        }
    }

    protected override void OnTeardown() => EventBus.Instance?.Unsubscribe<ArrowHitEvent>(OnArrowHit);

    /// <summary>Advances the draw one frame. <paramref name="attackHeld"/> is the attack button.</summary>
    public void Tick(double delta, bool attackHeld)
    {
        if (_action is not { Weapon.IsRanged: true, Current: not null, Phase: ActionPhase.Startup })
        {
            Reset();
            return;
        }

        if (!_active)
        {
            _active = true;
            _draw = default;
        }

        float stamina = _stats?.GetCurrent(StatType.Stamina) ?? float.MaxValue;
        _draw = RangedMath.Advance(
            _draw, (float)delta, attackHeld, stamina, out float spent,
            staminaPerSecond: RangedMath.DrawStaminaPerSecond * PerkQuery.Factor(Entity, PerkEffectKind.BowDrawStaminaMult));
        if (spent > 0f)
        {
            _stats?.ModifyCurrent(StatType.Stamina, -spent);
        }

        _reticle?.Show(Charge, Strained);
    }

    /// <summary>Reads the draw at the release frame (0..1) and ends it.</summary>
    public float Take()
    {
        float charge = Charge;
        Reset();
        return charge;
    }

    private void Reset()
    {
        if (_active)
        {
            _active = false;
            _draw = default;
            _reticle?.Show(0f, false);
        }
    }

    private void OnArrowHit(ArrowHitEvent e)
    {
        if (ReferenceEquals(e.Shooter, Entity) && e.Headshot)
        {
            _reticle?.Flash();
        }
    }
}
