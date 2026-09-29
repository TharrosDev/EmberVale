using Embervale.Combat.Actions;
using Godot;

namespace Embervale.Combat;

/// <summary>
/// Resource-driven definition of a melee weapon: what it is, what it hits for, and which chain of
/// <see cref="ActionDefinitionResource"/> a swing runs through.
///
/// <para>The three flat timing floats below are the <b>legacy shape</b>, kept because all 14
/// authored weapons speak it and because a weapon that never needed per-link authoring should not
/// have to grow four resource files to say so. When <see cref="Attacks"/> is empty they are
/// synthesised into a chain by <see cref="AttackChain"/>, at exactly the timings the old
/// stopwatch produced. Author <see cref="Attacks"/> when a weapon wants links that differ.</para>
/// </summary>
[GlobalClass]
public partial class WeaponResource : Resource
{
    [Export] public string DisplayName { get; set; } = "Weapon";
    [Export] public DamageType DamageType { get; set; } = DamageType.Physical;

    [ExportGroup("Damage")]
    [Export] public float BaseDamage { get; set; } = 12f;
    [Export] public float PoiseDamage { get; set; } = 20f;
    [Export] public float StaminaCost { get; set; } = 12f;

    [ExportGroup("Actions")]
    /// <summary>The authored attack chain, in order. Empty means "synthesise one from the legacy
    /// timings below", which is what every weapon does until it needs otherwise.</summary>
    [Export] public Godot.Collections.Array<ActionDefinitionResource> Attacks { get; set; } = new();

    [ExportGroup("Legacy timing (seconds, scaled by AttackSpeed; used when Attacks is empty)")]
    [Export] public float WindupTime { get; set; } = 0.15f;
    [Export] public float ActiveTime { get; set; } = 0.12f;
    [Export] public float RecoveryTime { get; set; } = 0.28f;

    /// <summary>Animation/feel speed multiplier; combines with the wielder's AttackSpeed stat.</summary>
    [Export] public float AttackSpeed { get; set; } = 1f;

    [ExportGroup("Ranged")]
    /// <summary>
    /// True for a bow or crossbow: the swing spawns a projectile instead of opening a hitbox.
    ///
    /// A flag on the existing resource rather than a second weapon type, because everything else
    /// about a ranged weapon — damage, poise, stamina, the action chain, the equipment socket — is
    /// identical to a melee one. What differs is what happens at the release, and that is one
    /// branch in one place.
    /// </summary>
    [Export] public bool IsRanged { get; set; }

    /// <summary>Metres per second the projectile travels.</summary>
    [Export] public float ProjectileSpeed { get; set; } = 38f;

    /// <summary>How far it flies before giving up, in metres.</summary>
    [Export] public float ProjectileRange { get; set; } = 60f;

    /// <summary>The model the projectile wears. Empty draws the small default bolt shape.</summary>
    [Export] public string ProjectileModelPath { get; set; } = "";

    [ExportGroup("Combo")]
    [Export] public int ComboLength { get; set; } = 3;

    /// <summary>Extra damage multiplier applied at the final combo hit (the finisher).</summary>
    [Export] public float FinisherMultiplier { get; set; } = 1.5f;

    [ExportGroup("Commitment")]
    /// <summary>How much of a synthesised light swing's recovery is committed (0 = cancellable at once,
    /// 1 = the whole tail). The committed tail is the window a swing can be punished in, and it is what
    /// stops a chain being an unbroken stream.</summary>
    [Export(PropertyHint.Range, "0,1,0.05")] public float RecoveryCommit { get; set; } = 0.3f;

    /// <summary>As <see cref="RecoveryCommit"/> for the last link, which hits hardest and pays most.</summary>
    [Export(PropertyHint.Range, "0,1,0.05")] public float FinisherRecoveryCommit { get; set; } = 0.65f;

    [ExportGroup("Heavy and charged")]
    /// <summary>An authored heavy attack. Empty synthesises one from the legacy timings, slower and
    /// harder-hitting than the light chain, so every melee weapon has a heavy without authoring one.</summary>
    [Export] public ActionDefinitionResource? Heavy { get; set; }

    [Export] public float HeavyDamageScale { get; set; } = 2f;
    [Export] public float HeavyPoiseScale { get; set; } = 2.4f;

    /// <summary>Multiplies the light stamina cost for the synthesised heavy.</summary>
    [Export] public float HeavyStaminaMultiplier { get; set; } = 2.2f;

    /// <summary>Seconds of holding (past the tap threshold) to reach a full charge.</summary>
    [Export] public float MaxChargeSeconds { get; set; } = 1f;

    /// <summary>Extra damage multiplier at a full charge, on top of the heavy's own scale.</summary>
    [Export] public float ChargeDamageBonus { get; set; } = 0.75f;

    /// <summary>Stamina drained per second while a charge is held.</summary>
    [Export] public float ChargeStaminaPerSecond { get; set; } = 8f;

    [ExportGroup("Plunge")]
    /// <summary>An authored plunge attack. Empty synthesises one.</summary>
    [Export] public ActionDefinitionResource? Plunge { get; set; }

    [Export] public float PlungeDamageScale { get; set; } = 1.5f;

    private ActionDefinitionResource[]? _synthesised;
    private ActionDefinitionResource? _synthHeavy;
    private ActionDefinitionResource? _synthPlunge;
    private ActionDefinitionResource? _synthRoll;

    /// <summary>The heavy attack a hold-and-release swings: authored, or synthesised once. Null for a
    /// ranged weapon, which has no melee heavy.</summary>
    public ActionDefinitionResource? HeavyAttack() =>
        IsRanged ? null : Heavy ?? (_synthHeavy ??= SynthesiseHeavy());

    /// <summary>The downward strike a jump-attack becomes. Null for a ranged weapon.</summary>
    public ActionDefinitionResource? PlungeAttack() =>
        IsRanged ? null : Plunge ?? (_synthPlunge ??= SynthesisePlunge());

    /// <summary>The quick lunging cut an attack pressed out of a roll becomes. Null for a ranged weapon.</summary>
    public ActionDefinitionResource? RollAttack() =>
        IsRanged ? null : _synthRoll ??= SynthesiseRoll();

    /// <summary>
    /// The chain a swing runs through — authored if there is one, otherwise synthesised once from
    /// the legacy timings and cached.
    ///
    /// <para>The synthesis is deliberately exact: <c>ActiveFrom</c> is the wind-up's share of the
    /// whole, <c>ActiveTo</c> is the end of the live window, and recovery is fully cancellable, which
    /// is precisely what the stopwatch FSM did. A migrated weapon therefore feels identical, and the
    /// only thing that changed is that the clip is now warped to span the same duration instead of
    /// playing at whatever speed it was exported at.</para>
    /// </summary>
    /// <summary>
    /// Overrides the chain for as long as it is set — a boss phase's own attack set.
    ///
    /// Held on the weapon rather than on the actor because the chain IS the weapon's, and a phase
    /// that swapped the actor's weapon outright would take its damage and identity with it.
    /// </summary>
    public ActionDefinitionResource[]? PhaseOverride { get; set; }

    public ActionDefinitionResource[] AttackChain()
    {
        if (PhaseOverride is { Length: > 0 } phase)
        {
            return phase;
        }

        if (Attacks.Count > 0)
        {
            var authored = new ActionDefinitionResource[Attacks.Count];
            for (int i = 0; i < Attacks.Count; i++)
            {
                authored[i] = Attacks[i];
            }

            return authored;
        }

        return _synthesised ??= Synthesise();
    }

    private ActionDefinitionResource[] Synthesise()
    {
        float total = Mathf.Max(0.05f, WindupTime + ActiveTime + RecoveryTime);
        float activeFrom = WindupTime / total;
        float activeTo = (WindupTime + ActiveTime) / total;
        int links = Mathf.Max(1, ComboLength);

        var chain = new ActionDefinitionResource[links];
        for (int i = 0; i < links; i++)
        {
            chain[i] = new ActionDefinitionResource
            {
                Id = $"{DisplayName}.attack{i + 1}",
                Kind = ActionKind.Attack,
                AnimationSlot = "attack",
                Duration = total,
                FallbackDuration = total,
                ActiveFrom = activeFrom,
                ActiveTo = activeTo,

                // The start of the tail is committed (RecoveryCommit; longer for the finisher) so a
                // swing can be punished, and the rest is the combo window: chaining "during
                // Recovery" advances the combo, and a press before it opens is buffered.
                CancelFrom = activeTo + ((1f - activeTo) *
                    Mathf.Clamp(i == links - 1 ? FinisherRecoveryCommit : RecoveryCommit, 0f, 1f)),
                ComboFrom = activeTo,
                ComboTo = 1f,

                StaminaCost = StaminaCost,
                DamageScale = i == links - 1 ? FinisherMultiplier : 1f,

                // The one deliberate change of feel in the migration: a committed swing no longer
                // moves the actor at full speed. The old FSM restricted movement not at all, which
                // is why every swing read as a float rather than as a commitment.
                MoveScale = 0.35f,

                // ⚠️ A SYNTHESISED ACTION EXPRESSES NO OPINION ABOUT REACH, and it must not. The
                // AI's own AIProfileResource.AttackRange already decided the actor was close
                // enough — a dragon attacks from 6.5 m — so a synthesised action inheriting the
                // 2.1 m default would silently refuse every dragon's swing and there would be no
                // error, just three creatures that never attack. An AUTHORED action is where a
                // designer says "this blow only reaches so far".
                AiMinRange = 0f,
                AiMaxRange = 999f,

                // A committed swing cannot be steered round a circling target. ⚠️ This game's body
                // yaw *is* the player's camera yaw in both view modes (PlayerCameraRig), so capping
                // the turn would cap looking around: CharacterActionComponent.EnforceTurnLimit is
                // therefore switched off for the player while free-looking and on while locked on,
                // where it is the lock's auto-facing that is being limited. Every AI actor is always
                // limited.
                TurnDegreesPerSecond = 140f,
            };
        }

        return chain;
    }

    private ActionDefinitionResource SynthesiseHeavy()
    {
        // Every phase is longer than the light swing's, the wind-up most of all: a heavy is a tell you
        // can read and a recovery you can punish. The floors keep a dagger's heavy from being a flick.
        float wind = Mathf.Max(0.32f, WindupTime * 2.2f);
        float active = Mathf.Max(0.12f, ActiveTime * 1.5f);
        float recovery = Mathf.Max(0.5f, RecoveryTime * 1.8f);
        float total = wind + active + recovery;
        float activeTo = (wind + active) / total;

        return new ActionDefinitionResource
        {
            Id = $"{DisplayName}.heavy",
            Kind = ActionKind.HeavyAttack,
            AnimationSlot = "heavy",
            Duration = total,
            FallbackDuration = total,
            ActiveFrom = wind / total,
            ActiveTo = activeTo,
            CancelFrom = activeTo + ((1f - activeTo) * 0.85f),
            ComboFrom = 1f,
            ComboTo = 1f,
            Interruptible = true,
            RecoveryVulnerable = true,
            MoveScale = 0.1f,
            TurnDegreesPerSecond = 60f,
            StaminaCost = StaminaCost * HeavyStaminaMultiplier,
            DamageScale = HeavyDamageScale,
            PoiseScale = HeavyPoiseScale,
            Knockback = 4f,
            RootMotion = RootMotionMode.WarpToTarget,
            MaxWarpDistance = 1.6f,
            MaxWarpDegrees = 25f,
            HitStopScale = 1.7f,
            CameraImpulse = 0.7f,
            TrailFrom = 0.4f,
            TrailTo = 0.75f,
            AiWeight = 0f,
            AiMinRange = 0f,
            AiMaxRange = 999f,
            AiRecoverySeconds = 0.9f,
        };
    }

    private ActionDefinitionResource SynthesisePlunge()
    {
        // The dive itself is not on this clock (it waits for the ground); this is the landing blow.
        const float total = 0.75f;
        return new ActionDefinitionResource
        {
            Id = $"{DisplayName}.plunge",
            Kind = ActionKind.Attack,
            AnimationSlot = "heavy_overhead",
            Duration = total,
            FallbackDuration = total,
            ActiveFrom = 0.12f,
            ActiveTo = 0.3f,
            CancelFrom = 0.78f,
            ComboFrom = 1f,
            ComboTo = 1f,
            RecoveryVulnerable = true,
            MoveScale = 0f,
            TurnDegreesPerSecond = 0f,
            StaminaCost = StaminaCost * 1.6f,
            DamageScale = PlungeDamageScale,
            PoiseScale = 2f,
            Knockback = 5f,
            HitboxName = "PlungeArc",
            HitStopScale = 1.8f,
            CameraImpulse = 0.9f,
            AiWeight = 0f,
        };
    }

    private ActionDefinitionResource SynthesiseRoll()
    {
        // Quicker in startup than the light swing (the roll already did the wind-up's work) but with a
        // real recovery, so a roll-attack is an opening rather than a free hit.
        float wind = Mathf.Max(0.06f, WindupTime * 0.55f);
        float active = Mathf.Max(0.08f, ActiveTime * 1.2f);
        float recovery = Mathf.Max(0.2f, RecoveryTime * 0.9f);
        float total = wind + active + recovery;
        float activeTo = (wind + active) / total;

        return new ActionDefinitionResource
        {
            Id = $"{DisplayName}.rollcut",
            Kind = ActionKind.Attack,
            AnimationSlot = "attack",
            Duration = total,
            FallbackDuration = total,
            ActiveFrom = wind / total,
            ActiveTo = activeTo,
            CancelFrom = activeTo + ((1f - activeTo) * 0.5f),
            ComboFrom = activeTo,
            ComboTo = 1f,
            MoveScale = 0.35f,
            TurnDegreesPerSecond = -1f,
            StaminaCost = StaminaCost,
            DamageScale = 1.15f,
            RootMotion = RootMotionMode.WarpToTarget,
            MaxWarpDistance = 1.4f,
            MaxWarpDegrees = 30f,
            AdvanceMetres = 0.8f,
            AiWeight = 0f,
        };
    }
}
