using Godot;

namespace Embervale.Combat.Actions;

/// <summary>What kind of thing an action is. Drives the interrupt/cancel rules that are the same for
/// every action of a kind, so a definition does not restate them.</summary>
// APPEND ONLY: ordinals persist in .tres — never reorder/insert/remove (EnumStabilityTests).
public enum ActionKind
{
    Attack,
    HeavyAttack,
    Block,
    Parry,
    Dodge,
    Cast,
    Ranged,
    Equip,
    UseItem,
    Stagger,
    Death,
    Contextual,
}

/// <summary>How much of the visible clip's own displacement drives the body.</summary>
// APPEND ONLY: ordinals persist in .tres — never reorder/insert/remove (EnumStabilityTests).
public enum RootMotionMode
{
    /// <summary>The clip does not move the body; velocity does. The right answer for locomotion.</summary>
    None,

    /// <summary>The clip's horizontal displacement drives the body, collision-swept.</summary>
    Horizontal,

    /// <summary>Horizontal displacement plus warping toward a locked target within the limits below.</summary>
    WarpToTarget,
}

/// <summary>
/// One authored character action — a light swing, a heavy, a block, a parry, a roll, a cast, a bow
/// release. <b>This is the single authoritative timeline.</b> Gameplay windows, the clip that shows
/// them, the cost, the commitment and the presentation all live here together, so there is no second
/// place for them to disagree.
///
/// <para><b>Windows are fractions, not seconds.</b> <see cref="ActiveFrom"/> and friends are
/// <c>0..1</c> across <see cref="Duration"/>. Set <see cref="Duration"/> to <c>0</c> and the action
/// lasts exactly as long as its animation clip — the clip is then literally the clock. Set it to a
/// number and the clip is time-warped to fit it. Either way what you see and what hits you are the
/// same window, which is the defect this type exists to make impossible.</para>
/// </summary>
[GlobalClass]
public partial class ActionDefinitionResource : Resource
{
    [Export] public string Id { get; set; } = "";
    [Export] public ActionKind Kind { get; set; } = ActionKind.Attack;

    [ExportGroup("Animation")]
    /// <summary>The <c>AnimationClips</c> slot to play ("attack", "heavy", "block", "cast", …).
    /// Resolution stays fuzzy and per-model, so a rig without this exact clip degrades to its
    /// nearest and an actor with no clip at all still fights on the fallback timer.</summary>
    [Export] public string AnimationSlot { get; set; } = "attack";

    /// <summary>Seconds the whole action takes. <b>0 means "however long the clip is"</b> — the
    /// animation-authoritative case, and the default for anything with a real clip.</summary>
    [Export] public float Duration { get; set; }

    /// <summary>Seconds to use when the actor has no clip for <see cref="AnimationSlot"/> and
    /// <see cref="Duration"/> is 0. Never 0 itself, or an actor without animation would finish its
    /// attacks instantly.</summary>
    [Export] public float FallbackDuration { get; set; } = 0.55f;

    [ExportGroup("Windows (fractions of Duration, 0..1)")]
    [Export(PropertyHint.Range, "0,1,0.01")] public float ActiveFrom { get; set; } = 0.34f;
    [Export(PropertyHint.Range, "0,1,0.01")] public float ActiveTo { get; set; } = 0.52f;

    /// <summary>Before this the actor is committed — no new action, no dodge, presses are buffered.
    /// This is what stops attack spam and instant cancellation.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float CancelFrom { get; set; } = 0.62f;

    [Export(PropertyHint.Range, "0,1,0.01")] public float ComboFrom { get; set; } = 0.52f;
    [Export(PropertyHint.Range, "0,1,0.01")] public float ComboTo { get; set; } = 1f;

    /// <summary>The action a press inside the combo window chains into. Empty ends the chain.</summary>
    [Export] public string NextActionId { get; set; } = "";

    [ExportGroup("Commitment")]
    /// <summary>Whether a stagger during startup cancels this outright. False is hyperarmor —
    /// what a boss's committed slam wants.</summary>
    [Export] public bool Interruptible { get; set; } = true;

    /// <summary>Whether the committed tail of this action (after the blow, before it can cancel) is a
    /// punish window: the actor counts as caught mid-action for poise (<c>CombatComponent.InWindup</c>),
    /// exactly as it is during startup. On for heavies, where the big swing is paid for in recovery.</summary>
    [Export] public bool RecoveryVulnerable { get; set; }

    /// <summary>What kind of blow this reads as on the telegraph, the warning arc and the nameplate
    /// (parryable, unblockable, sweep). <c>Auto</c> infers it from the id, hitbox and commitment.</summary>
    [Export] public TelegraphClass Telegraph { get; set; } = TelegraphClass.Auto;

    /// <summary>Fan angle in degrees for a <c>Sweep</c> telegraph. 0 infers it (a tail is wider, a
    /// spin is a full circle).</summary>
    [Export(PropertyHint.Range, "0,360,5")] public float SweepDegrees { get; set; }

    /// <summary>How much of normal movement the actor keeps while this runs. 0 roots them.</summary>
    [Export(PropertyHint.Range, "0,1,0.05")] public float MoveScale { get; set; }

    /// <summary>Degrees per second the actor may still turn. 0 locks facing at the commit — which is
    /// what stops a swing tracking a circling target through its whole animation.</summary>
    [Export] public float TurnDegreesPerSecond { get; set; } = 90f;

    [ExportGroup("Cost & damage")]
    [Export] public float StaminaCost { get; set; } = 12f;

    /// <summary>Multiplies the weapon's base damage. The finisher of a chain is just a link with a
    /// bigger number here, which is why <c>FinisherMultiplier</c> no longer needs to exist.</summary>
    [Export] public float DamageScale { get; set; } = 1f;
    [Export] public float PoiseScale { get; set; } = 1f;
    [Export] public float Knockback { get; set; }

    [ExportGroup("Hit")]
    /// <summary>Names which of the actor's hit volumes this action opens. Empty uses the default one.
    /// A dragon's bite, wing and tail are three definitions naming three volumes.</summary>
    [Export] public string HitboxName { get; set; } = "";

    [ExportGroup("Root motion")]
    [Export] public RootMotionMode RootMotion { get; set; } = RootMotionMode.None;
    [Export] public float MaxWarpDistance { get; set; } = 1.5f;
    [Export] public float MaxWarpDegrees { get; set; } = 35f;

    /// <summary>Metres the actor steps forward during startup with or without a target (swept, spent
    /// across the wind-up like a warp). 0 for none. A lunge is authored here, not root motion.</summary>
    [Export] public float AdvanceMetres { get; set; }

    [ExportGroup("Presentation")]
    [Export] public float HitStopScale { get; set; } = 1f;
    [Export] public float CameraImpulse { get; set; }
    [Export] public string SwingCueId { get; set; } = "sfx.combat.swing";
    [Export(PropertyHint.Range, "0,1,0.01")] public float TrailFrom { get; set; } = 0.3f;
    [Export(PropertyHint.Range, "0,1,0.01")] public float TrailTo { get; set; } = 0.6f;

    [ExportGroup("AI")]
    /// <summary>Relative likelihood an AI picks this when several are in range. 0 makes it
    /// player-only.</summary>
    [Export] public float AiWeight { get; set; } = 1f;
    [Export] public float AiMinRange { get; set; }
    [Export] public float AiMaxRange { get; set; } = 2.1f;

    /// <summary>Seconds an AI waits after this action before choosing again — the pause the old
    /// system had none of, which is why enemies attacked at maximum weapon cadence forever.</summary>
    [Export] public float AiRecoverySeconds { get; set; } = 0.35f;

    /// <summary>The windows as the pure timeline wants them.</summary>
    public ActionWindows Windows =>
        new(ActiveFrom, ActiveTo, CancelFrom, ComboFrom, ComboTo);
}
