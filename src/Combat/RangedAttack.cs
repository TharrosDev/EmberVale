using Embervale.Combat.Actions;
using Embervale.Core.Events;
using Embervale.Entities;
using Embervale.Items;
using Embervale.Movement;
using Embervale.Player;
using Embervale.Progression;
using Embervale.Stats;
using Embervale.World;
using Godot;

namespace Embervale.Combat;

/// <summary>
/// Sends an arrow instead of opening a volume. Owned by one <see cref="CharacterActionComponent"/>,
/// which calls <see cref="Fire"/> on the action's release frame and <see cref="Clear"/> on teardown.
///
/// <para>⚠️ It spawns on the action's release frame like everything else — the string is drawn, and the
/// arrow leaves when the animation shows it leaving. A bow that fired on key-down would put the
/// arrow across the room before the draw finished, which is the melee desync all over again in a
/// system that never had it.</para>
///
/// <para>Split out of the executor so the ranged rules have one owner and the executor stays the
/// melee/cast hub.</para>
/// </summary>
public sealed class RangedAttack
{
    /// <summary>Pooled arrows, built on first use so a melee actor never pays for one.</summary>
    private Core.Pooling.NodePool<Arrow>? _arrows;

    /// <summary>Parked arrows are detached nodes and are not freed with their owner's scene.
    /// Release their render/physics resources when a streamed actor leaves the world.</summary>
    public void Clear()
    {
        _arrows?.Clear();
        _arrows = null;
    }

    private void ReleaseArrow(Arrow arrow)
    {
        if (_arrows != null)
            _arrows.Return(arrow);
        else
            arrow.QueueFree(); // A shot may finish after its owner has left the scene.
    }

    /// <summary>Whether this shooter's arrows are counted. The player's are; nobody else's.</summary>
    public static bool NeedsAmmo(IEntity? shooter) => shooter is PlayerCharacter;

    /// <summary>
    /// Whether <paramref name="shooter"/> has an arrow to loose: always true for anyone whose arrows
    /// are not counted. <see cref="Fire"/> is the enforcement; this is the same answer for a HUD or
    /// an input gate that wants to refuse the draw before it starts.
    /// </summary>
    public static bool HasAmmo(IEntity? shooter) =>
        !NeedsAmmo(shooter) || shooter!.GetComponent<EquipmentComponent>() is not { } quiver || quiver.AmmoCount > 0;

    public void Fire(
        WeaponResource bow,
        ActionDefinitionResource definition,
        IEntity? shooter,
        Node3D body,
        StatsComponent? stats,
        CombatComponent? combat,
        MountComponent? mount,
        Vector3? aimPoint)
    {
        _arrows ??= new Core.Pooling.NodePool<Arrow>(() => new Arrow { Released = ReleaseArrow }, prewarm: 2);

        // The draw. A shooter with a BowDrawComponent (the player) is as strong as the string was held
        // back; anyone else (an AI archer) looses a full draw, exactly as every bow did before draw
        // existed. Packet.Charge stays 0 for the latter so nothing downstream mistakes it for a drawn shot.
        BowDrawComponent? draw = shooter?.GetComponent<BowDrawComponent>();
        float charge = draw?.Take() ?? 1f;

        // The arrow. Only the player's quiver is real: an AI archer, a companion and a probe's bare
        // archer loose for free, exactly as before. The draw above is already taken, so a dry
        // release resets the string instead of leaving it held.
        float arrowDamage = 0f;
        if (NeedsAmmo(shooter) && shooter!.GetComponent<EquipmentComponent>() is { } quiver)
        {
            int tier = quiver.Ammo?.Template.Tier ?? 0;
            if (!quiver.ConsumeAmmo())
            {
                EventBus.Instance?.Publish(new WorldHazardNoticeEvent(AmmoRules.NoAmmoReasonKey));
                return;
            }

            arrowDamage = AmmoRules.BonusDamage(tier);
        }

        float mounted = MountedCombat.DamageScale(
            mount is { IsMounted: true }, mount is { IsGalloping: true });
        (float amount, bool isCrit) = CombatMath.RollAttack(
            (bow.BaseDamage + arrowDamage) * definition.DamageScale * mounted, stats);

        // ⚠️ THE DRAW SCALES THE ROLLED DAMAGE, NOT THE WEAPON'S BASE. RollAttack adds the archer's power
        // stat to the base, and on a levelled character that stat is most of the number: scaling only
        // the base made a snap shot hit for 92% of a full draw.
        amount *= RangedMath.DamageScale(charge) * PerkQuery.Factor(shooter, PerkEffectKind.RangedPowerBonus);

        Vector3 from = body.GlobalPosition + (Vector3.Up * 1.4f);
        float speed = bow.ProjectileSpeed * RangedMath.SpeedScale(charge);

        // ⚠️ THE PLAYER'S `AimPoint` IS THE EYE, NOT A POINT OUT ALONG THE AIM. The input router copies
        // the aim NODE's position into it, which sits at the eye (1.62 m) — 0.22 m above this arrow's
        // origin — so trusting it sent every player arrow straight up. The aim controller's own
        // convergence point is the real answer; AimPoint remains the AI's and the harnesses' route.
        AimController? aimer = shooter?.GetComponent<AimController>();
        Vector3? focus = aimer is { HasFocus: true } ? aimer.Focus : aimPoint;
        if (focus is { } aimed && aimer != null)
        {
            focus = aimer.AssistedFocus(from, aimed, charge);
        }

        Vector3 direction = focus is { } target && target.DistanceSquaredTo(from) > 0.04f
            ? RangedMath.LaunchDirection(
                from, target, speed, RangedMath.ArrowGravity, bow.ProjectileRange * RangedMath.ReachFraction)
            : -body.GlobalBasis.Z;
        direction = RangedMath.Scatter(direction, charge, GD.Randf(), GD.Randf());

        Arrow arrow = _arrows.Get();
        if (arrow.GetParent() == null)
        {
            // ⚠️ CurrentScene is null outside a normal game boot — every `--script` harness runs with
            // no current scene — and `CurrentScene?.AddChild` then silently does nothing. The arrow
            // is a live object that is not in the tree: no physics, no overlaps, no hit, no error.
            // Falling back to the tree root keeps it in the WORLD (never parented to the shooter,
            // which would carry it along) and keeps the probes honest.
            SceneTree tree = body.GetTree();
            (tree.CurrentScene ?? tree.Root).AddChild(arrow);
        }

        arrow.GlobalPosition = from;
        arrow.Launch(
            new DamagePacket(amount, bow.DamageType, shooter, isCrit,
                bow.PoiseDamage * definition.PoiseScale * RangedMath.PoiseScale(charge), HitKind.Ranged,
                draw != null ? charge : 0f),
            shooter, combat?.Team ?? 0, direction,
            speed, bow.ProjectileRange, bow.ProjectileModelPath, RangedMath.ArrowGravity);

        if (draw != null && shooter != null)
        {
            EventBus.Instance?.Publish(new ChargeReleasedEvent(shooter, charge));
        }
    }
}
