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

    private ActionDefinitionResource[]? _synthesised;

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

                // Recovery is entirely cancellable and is entirely the combo window — the legacy
                // rule, where chaining "during Recovery" advanced the combo.
                CancelFrom = activeTo,
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

                // ⚠️ NOT restricted, and the camera is why. This game's body yaw *is* its camera
                // yaw in both view modes (PlayerCameraRig), so capping the turn rate during an
                // attack would cap the player's ability to look around. Per-actor rotation limits
                // belong on authored enemy actions, where facing and view are separate things.
                TurnDegreesPerSecond = -1f,
            };
        }

        return chain;
    }
}
