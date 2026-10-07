using Embervale.Combat;
using Embervale.Entities;
using Embervale.Magic;
using Embervale.Stats;
using Godot;

namespace Embervale.Enemies;

/// <summary>
/// How a standoff fighter fights: hold a band, kite when crowded, and pick one spell a tick.
///
/// <para>A standoff fighter is one whose profile authors a <see cref="AIProfileResource.StandoffRange"/>
/// beyond its attack range — a caster today, an archer later. ⚠️ <b>The rule is the profile, not the
/// presence of spells</b>: the dragon carries a breath and still closes to bite.</para>
///
/// <para>It is a plain class owned by <see cref="EnemyAIComponent"/>, not a component of its own: it
/// has no lifetime, no events and no state beyond one throttle timer, and every archetype that does
/// not stand off would carry an idle node for nothing.</para>
/// </summary>
public sealed class EnemyCasterTactics
{
    /// <summary>Band a caster falls back to when its profile authors no standoff range — the
    /// pre-34A cast-range default, so an unconverted caster fights exactly as it used to.</summary>
    public const float DefaultCastRange = 14f;

    /// <summary>How often a support caster may scan its team for someone to heal. A constant rather
    /// than a profile knob because it paces a cost, it does not express a personality. Short enough
    /// that a heal still lands inside a swing, long enough that the scan stops being per-frame.</summary>
    public const double SupportScanInterval = 0.3d;

    private readonly IEntity _owner;
    private readonly Node3D _body;
    private readonly AiNavigator _nav;
    private readonly SceneTree _tree;

    private double _supportScanTimer;

    public EnemyCasterTactics(IEntity owner, Node3D body, AiNavigator nav, SceneTree tree)
    {
        _owner = owner;
        _body = body;
        _nav = nav;
        _tree = tree;
    }

    /// <summary>Advances the support-scan throttle. Ticked on wall-clock time so the AI's level of
    /// detail is accounted for, beside the state and retreat timers.</summary>
    public void TickTimers(double wallSeconds) => _supportScanTimer -= wallSeconds;

    /// <summary>
    /// Standoff combat: hold the band (approach when too far, kite when too close), face the target
    /// so the attack aims true, and fire whatever is ready. Reuses the player's
    /// <see cref="SpellcastingComponent"/> — there is no parallel casting system.
    /// </summary>
    public void TickCombat(
        AIProfileResource profile,
        SpellcastingComponent? casting,
        CombatComponent? combat,
        Vector3 targetPos,
        Vector3 home,
        double delta,
        double frameDelta,
        bool airborne)
    {
        _nav.FaceTowards(targetPos, profile.TurnSpeedDegrees, frameDelta);
        PitchAim(casting, targetPos);

        float distance = AiNavigator.HorizontalDistance(_body.GlobalPosition, targetPos);
        float band = profile.StandoffRange > 0f ? profile.StandoffRange : DefaultCastRange;

        switch (CasterDecision.Move(distance, profile.KiteDistance, band))
        {
            case CasterMove.Kite:
                Vector3 away = _body.GlobalPosition - targetPos;
                away.Y = 0f;
                Vector3 flee = away.LengthSquared() > 0.01f
                    ? _body.GlobalPosition + (away.Normalized() * 5f)
                    : home;
                _nav.MoveTowards(flee, delta, sprint: true, stopDistance: 0.1f, airborne);
                break;
            case CasterMove.Approach:
                _nav.MoveTowards(targetPos, delta, sprint: false, stopDistance: band * 0.9f, airborne);
                break;
            default:
                _nav.Stand(delta);
                break;
        }

        TryCast(profile, casting, combat);
    }

    /// <summary>Height above a target's feet a bolt is pitched at: a person's chest.</summary>
    private const float TargetChest = 1.0f;

    /// <summary>The steepest a cast origin tilts, radians (60 degrees): past that the target is
    /// under the caster's feet and no bolt is the right answer.</summary>
    private const float MaxAimPitch = 1.047f;

    /// <summary>
    /// Tilts the cast origin up or down onto the target. The body's turn supplies the yaw (so a slow
    /// turner can still be side-stepped); this supplies only the pitch.
    ///
    /// <para>A bolt leaves along the cast origin's forward, and that origin sits three quarters up
    /// the body. Level, it passes over anything shorter than the caster's hands: a caster of
    /// ordinary height got away with it, one four or five metres tall would throw every bolt over
    /// the player's head, and any caster missed a target standing downhill.</para>
    ///
    /// <para>A breath wielder is left alone: <see cref="BreathComponent"/> aims that origin itself.</para>
    /// </summary>
    private void PitchAim(SpellcastingComponent? casting, Vector3 targetPos)
    {
        if (casting?.AimNode is not { } aim || !GodotObject.IsInstanceValid(aim) ||
            _owner.GetComponent<BreathComponent>() != null)
        {
            return;
        }

        Vector3 to = (targetPos + (Vector3.Up * TargetChest)) - aim.GlobalPosition;
        aim.Rotation = new Vector3(CasterDecision.AimPitch(to.Y, new Vector2(to.X, to.Z).Length(), MaxAimPitch), 0f, 0f);
    }

    /// <summary>
    /// One cast action per tick, in a strict priority order. Per-spell cooldowns pace it, so a caster
    /// that heals this tick attacks the next.
    /// </summary>
    public void TryCast(AIProfileResource profile, SpellcastingComponent? casting, CombatComponent? combat)
    {
        if (casting == null)
        {
            return;
        }

        // 1. Support: heal the most-wounded ally (or itself) below the heal threshold.
        SpellResource? heal = ReadySupport(casting, healing: true);
        if (heal != null && FindWoundedAlly(profile, combat) is { } ally && casting.TryCastSupportOn(ally, heal))
        {
            return;
        }

        // 2. Control: a root, silence, stun or mark goes out whenever it is ready. Its own cooldown paces
        //    it, so a shaman roots you, then fills the gap with damage, and the fight has a rhythm
        //    instead of one spell on repeat.
        SpellResource? opener = ReadyControl(casting);
        if (opener != null && casting.TryCastById(opener.Id))
        {
            return;
        }

        // 3. Offensive: the hardest-hitting ready damage spell, aimed down the body's facing.
        SpellResource? attack = ReadyOffensive(casting);
        if (attack != null && casting.TryCastById(attack.Id))
        {
            return;
        }

        // 4. Ward itself when there is nothing better to do and the buff is not already up.
        SpellResource? ward = ReadySupport(casting, healing: false);
        if (ward != null && !HasStatus(_owner, ward.StatusEffectId))
        {
            casting.TryCastSupportOn(_owner, ward);
        }
    }

    /// <summary>A ready non-Self spell whose status controls its target (root, silence, stun, mark), or null.
    /// Control is read from the status resource, so a spell earns this by its data and no id is named.</summary>
    private static SpellResource? ReadyControl(SpellcastingComponent casting)
    {
        foreach (SpellResource spell in casting.Spells)
        {
            if (spell.Delivery != SpellDelivery.Self && spell.HasStatusEffect && casting.CanCast(spell) &&
                StatusEffectDatabase.Get(spell.StatusEffectId) is { } status && IsControlStatus(status))
            {
                return spell;
            }
        }

        return null;
    }

    /// <summary>Root, silence and stun take the target out of the fight; a status that raises the damage it
    /// takes (Grave Mark) sets it up. A bare Mark flag is not enough: Kindled and Stormbrand carry one and
    /// are damage riders, which the damage pick below already handles.</summary>
    private static bool IsControlStatus(StatusEffectResource status) =>
        (status.Controls & (StatusControl.Root | StatusControl.Silence | StatusControl.Stun)) != StatusControl.None ||
        status.DamageTakenModifier > 0f;

    /// <summary>The strongest ready offensive (non-Self, damaging) spell the caster knows, or null.</summary>
    private static SpellResource? ReadyOffensive(SpellcastingComponent casting)
    {
        SpellResource? best = null;
        foreach (SpellResource spell in casting.Spells)
        {
            if (spell.Delivery != SpellDelivery.Self && spell.BaseDamage > 0f && casting.CanCast(spell) &&
                (best == null || spell.BaseDamage > best.BaseDamage))
            {
                best = spell;
            }
        }

        return best;
    }

    /// <summary>A ready Self-delivery support spell: a heal (<paramref name="healing"/> true) or a
    /// beneficial ward (false), or null when none is castable.</summary>
    private static SpellResource? ReadySupport(SpellcastingComponent casting, bool healing)
    {
        foreach (SpellResource spell in casting.Spells)
        {
            bool isHeal = spell.Healing > 0f;
            if (spell.Delivery == SpellDelivery.Self && isHeal == healing && casting.CanCast(spell) &&
                (healing || spell.HasStatusEffect))
            {
                return spell;
            }
        }

        return null;
    }

    /// <summary>
    /// The most-wounded ally (or itself) within the profile's support range on the caster's team
    /// whose health is below its heal threshold, or null when none needs healing.
    ///
    /// ⚠️ <b>Throttled, because this is a group-wide scan inside the combat tick.</b> It walks every
    /// node in the enemy group — a freshly marshalled Godot array each call — and does an owner
    /// lookup plus two component lookups per candidate. It ran unthrottled on every physics frame for
    /// every caster with a heal ready, so the cost was O(casters × live enemies) per frame: invisible
    /// with ten enemies, real in a boss arena where the summon waves have built a crowd.
    ///
    /// A throttled tick returns null and the caster falls through to attacking instead, rather than
    /// caching an ally reference that could be freed before the next tick reads it.
    /// </summary>
    private IEntity? FindWoundedAlly(AIProfileResource profile, CombatComponent? combat)
    {
        if (_supportScanTimer > 0d)
        {
            return null;
        }

        _supportScanTimer = SupportScanInterval;

        int team = combat?.Team ?? 0;
        IEntity? best = null;
        float lowest = profile.AllyHealThreshold;

        foreach (Node node in _tree.GetNodesInGroup(Quests.ObjectiveLocator.EnemyGroup))
        {
            if (node is not Node3D body ||
                AiNavigator.HorizontalDistance(_body.GlobalPosition, body.GlobalPosition) > profile.AllySupportRange ||
                EntityNode.FindOwner(node) is not { } ally ||
                ally.GetComponent<CombatComponent>()?.Team != team)
            {
                continue;
            }

            StatsComponent? stats = ally.GetComponent<StatsComponent>();
            if (stats is not { IsAlive: true })
            {
                continue;
            }

            float fraction = stats.GetNormalized(StatType.Health);
            if (fraction < lowest)
            {
                lowest = fraction;
                best = ally;
            }
        }

        return best;
    }

    private static bool HasStatus(IEntity entity, string statusId) =>
        !string.IsNullOrEmpty(statusId) && entity.GetComponent<StatusEffectsComponent>()?.Has(statusId) == true;
}
