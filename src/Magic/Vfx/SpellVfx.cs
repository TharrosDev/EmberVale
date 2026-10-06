using Embervale.Animation;
using Embervale.Combat;
using Embervale.Entities;
using Godot;

namespace Embervale.Magic.Vfx;

/// <summary>What a caster is winding up.</summary>
public enum SpellWindupKind
{
    /// <summary>The committed wind-up of a cast action, before its release frame.</summary>
    Cast,

    /// <summary>A charged spell being held; <see cref="SpellVfx.WindupProgress"/> follows it.</summary>
    Charge,

    /// <summary>A channel that has released and is sustaining.</summary>
    Channel,
}

/// <summary>How a wind-up, charge or channel ended.</summary>
public enum SpellWindupEnd
{
    /// <summary>The spell left the hand.</summary>
    Released,

    /// <summary>It simply stopped: a channel let go, a menu opened, the spell was forgotten.</summary>
    Stopped,

    /// <summary>A stagger, a silence, a stun or the caster's death cut it off.</summary>
    Interrupted,
}

/// <summary>What a spell struck.</summary>
public enum SpellImpactKind
{
    /// <summary>It landed on a target.</summary>
    Target,

    /// <summary>A guard stopped it.</summary>
    Blocked,

    /// <summary>A bolt burst against world geometry.</summary>
    World,

    /// <summary>A bolt was spent against a standing barrier.</summary>
    Barrier,

    /// <summary>A bolt ran out of range in the air.</summary>
    Expired,
}

/// <summary>Where a burst came from.</summary>
public enum SpellBurstSource
{
    /// <summary>A nova around the caster.</summary>
    Caster,

    /// <summary>A bolt with an impact radius, where it burst.</summary>
    Projectile,

    /// <summary>A ground spell landing after its telegraph.</summary>
    Ground,

    /// <summary>One pulse of a lingering zone.</summary>
    Zone,
}

/// <summary>What a line drawn between two points means.</summary>
public enum SpellArcKind
{
    /// <summary>Lightning jumping to a nearby foe.</summary>
    Chain,

    /// <summary>Lightning drawn to a Stormbranded foe.</summary>
    Brand,

    /// <summary>Life drawn from a target back to the caster.</summary>
    Tether,

    /// <summary>A status jumping to a new bearer when its old one died.</summary>
    Spread,
}

/// <summary>A school or status effect going off on a bearer.</summary>
public enum SpellProcKind
{
    /// <summary>A ward absorbed its last point and broke.</summary>
    WardBreak,

    /// <summary>A stacking status detonated (Kindle at full stacks).</summary>
    Detonation,

    /// <summary>A chilled target froze.</summary>
    Freeze,

    /// <summary>A fire hit fed a Kindled target another Burning stack.</summary>
    KindleFed,

    /// <summary>An arcane hit tore a buff off.</summary>
    Dispel,

    /// <summary>A ward soaked part of a blow and still stands.</summary>
    WardHit,
}

/// <summary>One spell impact, as the code that resolved it knows it.</summary>
/// <param name="Position">Where it struck, in world space.</param>
/// <param name="Normal">Unit vector pointing back the way the spell came.</param>
/// <param name="Target">Who was struck, or null for geometry and thin air.</param>
/// <param name="Kind">What it struck.</param>
/// <param name="Charge">The charge (0..1) a held cast reached.</param>
/// <param name="Damage">Health actually taken, after mitigation.</param>
/// <param name="Crit">Whether it was a critical hit.</param>
/// <param name="Killed">Whether it killed the target.</param>
/// <param name="Consumed">Stacks of the spell's <c>ConsumesStatusId</c> it ate off the target.</param>
public readonly record struct SpellImpactInfo(
    Vector3 Position,
    Vector3 Normal,
    IEntity? Target,
    SpellImpactKind Kind,
    float Charge = 0f,
    float Damage = 0f,
    bool Crit = false,
    bool Killed = false,
    int Consumed = 0);

/// <summary>
/// The one door every spell effect goes through. Gameplay code says what happened (a wind-up began,
/// a bolt left, a burst landed) and never how it looks; what is drawn is decided behind this class,
/// by <see cref="SpellVfxDirector"/> and the recipes in <see cref="SpellVfxCatalog"/>.
///
/// <para><b>Every method is a no-op when there is no director or the display is headless</b>, so a
/// probe, a bare scene and the headless gates build no effect nodes at all. An <c>Attach…</c> method
/// that returns false tells its caller the facade drew nothing, and the caller then builds the
/// plain shape it always did.</para>
///
/// <para>Presentation only: nothing here reads back into a rule, and a call may be dropped (over
/// budget, too far away) without anything else changing.</para>
/// </summary>
public static class SpellVfx
{
    private static SpellVfxDirector? _director;

    /// <summary>Whether effects are being drawn: a director is live and there is a display.</summary>
    public static bool Active =>
        _director != null && GodotObject.IsInstanceValid(_director) && _director.IsInsideTree() && _director.Enabled;

    /// <summary>The node every effect is parented under, or null when nothing is drawn.</summary>
    private static Node3D? Root => Active ? _director!.VfxRoot : null;

    internal static void Bind(SpellVfxDirector director) => _director = director;

    internal static void Unbind(SpellVfxDirector director)
    {
        if (ReferenceEquals(_director, director))
        {
            _director = null;
        }
    }

    /// <summary>Forgets the director. Called between sessions.</summary>
    public static void Reset() => _director = null;

    /// <summary>
    /// Drops everything the facade tracks about live effects (caster to aura, bolt to trail, node to
    /// zone). <see cref="SpellVfxDirector.KillAll"/> calls it after freeing the effect nodes, on a
    /// load and as the director leaves the tree, so nothing here may touch a node again.
    /// </summary>
    internal static void Clear()
    {
    }

    // --- the cast --------------------------------------------------------------------------------

    /// <summary>
    /// A caster began winding up, charging or sustaining <paramref name="spell"/>. One caster has one
    /// such aura at a time: a new call replaces the last (a charge handing over to its cast wind-up, a
    /// wind-up handing over to its channel). <paramref name="seconds"/> is how long the wind-up or
    /// the full charge takes (0 for a channel); <paramref name="charge"/> is what a held cast reached.
    /// </summary>
    public static void Windup(IEntity caster, SpellResource spell, SpellWindupKind kind, float seconds, float charge = 0f)
    {
    }

    /// <summary>How full the caster's held charge is now (0..1). Called every frame it is held.</summary>
    public static void WindupProgress(IEntity caster, float progress)
    {
    }

    /// <summary>The caster's wind-up, charge or channel is over. Safe to call with none running.</summary>
    public static void WindupEnd(IEntity caster, SpellWindupEnd how)
    {
    }

    /// <summary>The spell left the caster: the release frame, for every delivery and every channel
    /// tick. <paramref name="origin"/> and <paramref name="direction"/> are the true aim.</summary>
    public static void Release(IEntity caster, SpellResource spell, Vector3 origin, Vector3 direction, float charge)
    {
    }

    /// <summary>The cast beat at the casting hand, when the cast animation starts.</summary>
    public static void HandFlash(IEntity caster, SpellResource spell, Vector3 hand) =>
        Flash(hand, 0.5f, SpellSchools.Color(spell.School));

    /// <summary>
    /// Where a caster's effects start from: the casting hand when the body has one, else
    /// <paramref name="fallback"/> (the aim origin). For drawing only; the aim, the muzzle and the
    /// collision path never move.
    /// </summary>
    public static Vector3 CastOrigin(IEntity? caster, Vector3 fallback)
    {
        if (!Active || caster == null)
        {
            return fallback;
        }

        CharacterAnimationComponent? animation = caster.GetComponent<CharacterAnimationComponent>();
        return animation != null && GodotObject.IsInstanceValid(animation) &&
               animation.TryGetCastingHand(out Vector3 hand)
            ? hand
            : fallback;
    }

    // --- projectiles and beams -------------------------------------------------------------------

    /// <summary>
    /// A bolt was launched. <paramref name="visualOrigin"/> is where its picture should start (the
    /// casting hand) before it settles onto the bolt's true path. Returns true when the facade drew
    /// the bolt, in which case the projectile hides its own plain sphere and light.
    /// <paramref name="charge"/> is what a held cast reached (0..1).
    /// </summary>
    public static bool AttachProjectile(
        Node3D projectile, SpellResource spell, IEntity? caster, Vector3 visualOrigin, Vector3 direction,
        float charge) => false;

    /// <summary>The bolt resolved, was cancelled or left the tree. May be called more than once for
    /// one flight, and for a bolt that was never attached.</summary>
    public static void DetachProjectile(Node3D projectile)
    {
    }

    /// <summary>One tick of a channelled bolt spell, as the line it travels: from the casting hand to
    /// the end of its range. A sustained beam is redrawn from these. <paramref name="to"/> is the full
    /// range and is not clipped to what the bolt strikes; that arrives later as its <see cref="Impact"/>.</summary>
    public static void Beam(IEntity caster, SpellResource spell, Vector3 from, Vector3 to)
    {
    }

    // --- impacts ---------------------------------------------------------------------------------

    /// <summary>A spell struck something: a target, a guard, a wall, a barrier, or nothing at the end
    /// of its range.</summary>
    public static void Impact(SpellResource spell, IEntity? caster, in SpellImpactInfo hit)
    {
    }

    /// <summary>A spell burst over <paramref name="radius"/> at <paramref name="position"/>, which is
    /// the centre of the damaged sphere and not a point on the floor. <paramref name="charge"/> is
    /// what a held cast reached (0..1).</summary>
    public static void Burst(
        SpellResource spell, IEntity? caster, Vector3 position, float radius, SpellBurstSource source,
        float charge = 0f) =>
        Flash(position, radius, SpellSchools.Color(spell.School));

    /// <summary>A wedge swept out from <paramref name="origin"/>: a breath, a word of power.</summary>
    public static void Cone(
        SpellResource spell, IEntity? caster, Vector3 origin, Vector3 direction, float range, float angleDegrees)
    {
        if (!Active || direction.LengthSquared() < 0.0001f || range <= 0f)
        {
            return;
        }

        // A line of widening flashes along the axis, each as wide as the cone is there, so the shape
        // and reach of the damaged volume can be read.
        const int Puffs = 4;
        Vector3 axis = direction.Normalized();
        float halfAngle = Mathf.DegToRad(angleDegrees * 0.5f);
        Color color = SpellSchools.Color(spell.School);
        for (int i = 1; i <= Puffs; i++)
        {
            float travelled = range * i / Puffs;
            Flash(origin + (axis * travelled), travelled * Mathf.Tan(halfAngle), color);
        }
    }

    /// <summary>A line of <paramref name="school"/> between two points: a chained bolt, a life tether,
    /// a status jumping bearers. <paramref name="source"/> is whose effect it is, and
    /// <paramref name="spell"/> the spell whose hit drew it (null for a status spreading by itself).</summary>
    public static void Arc(
        DamageType school, IEntity? source, Vector3 from, Vector3 to, SpellArcKind kind, SpellResource? spell = null)
    {
    }

    /// <summary>A spell combo went off on <paramref name="target"/>: <paramref name="spell"/> struck a
    /// bearer of the status the combo <paramref name="comboId"/> (<c>combo.*</c>) needs.
    /// <paramref name="position"/> is the struck volume, read before the bonus damage landed.</summary>
    public static void Combo(string comboId, SpellResource spell, IEntity? caster, IEntity target, Vector3 position)
    {
    }

    // --- placed spells ---------------------------------------------------------------------------

    /// <summary>
    /// A ground spell began its delay. <paramref name="ground"/> is the node waiting at the landing
    /// point; it is positioned just after this call, so read its transform on the next frame and
    /// follow its validity. A spell whose recipe says so falls as a <see cref="Meteor"/> instead.
    /// </summary>
    public static void GroundTelegraph(Node3D ground, SpellResource spell, IEntity? caster, float radius, float delay)
    {
        if (!Active)
        {
            return;
        }

        if (SpellVfxCatalog.For(spell).Ground == VfxGroundStyle.Meteor)
        {
            Meteor(ground, spell, caster, radius, delay);
        }
    }

    /// <summary>Something falls onto <paramref name="ground"/> for <paramref name="delay"/> seconds and
    /// lands as the delay ends. The landing itself arrives as a <see cref="Burst"/>.</summary>
    public static void Meteor(Node3D ground, SpellResource spell, IEntity? caster, float radius, float delay)
    {
    }

    /// <summary>The ground spell's delay is over: <paramref name="landed"/> when it came down (its
    /// <see cref="Burst"/> or <see cref="AttachZone"/> follows at once), false when it was cancelled or
    /// left the tree first. May be called more than once; the first call is the one that counts.</summary>
    public static void GroundEnd(Node3D ground, bool landed)
    {
    }

    /// <summary>A lingering zone began. Each of its pulses arrives as a <see cref="Burst"/>.
    /// <paramref name="zone"/> is positioned just after this call, so read its transform on the next
    /// frame and follow its validity.</summary>
    public static void AttachZone(Node3D zone, SpellResource spell, IEntity? caster, float radius, float duration)
    {
    }

    /// <summary>The zone ran out, was cancelled or left the tree. May be called more than once.</summary>
    public static void ZoneEnd(Node3D zone)
    {
    }

    /// <summary>A wall began its delay. <paramref name="barrier"/> is positioned and turned just after
    /// this call, so read its transform on the next frame.</summary>
    public static void BarrierTelegraph(Node3D barrier, SpellResource spell, IEntity? caster, float width, float delay)
    {
    }

    /// <summary>A wall stands. Returns true when the facade drew it, in which case the barrier builds
    /// no plain face or base of its own. A wall with no delay is positioned and turned just after
    /// this call; one that had a telegraph already stands where it will.</summary>
    public static bool AttachBarrier(
        Node3D barrier, SpellResource spell, IEntity? caster, float width, float height, bool solid) => false;

    /// <summary>A wall ended: <paramref name="broken"/> when its health ran out, false when it expired
    /// or was cancelled. May be called for a wall that was never attached, and more than once (the
    /// wall says it again as it leaves the tree, always unbroken); the first call is the one that counts.</summary>
    public static void BarrierEnd(Node3D barrier, SpellResource spell, float width, bool broken)
    {
        if (broken && Active && GodotObject.IsInstanceValid(barrier) && barrier.IsInsideTree())
        {
            Flash(barrier.GlobalPosition + (Vector3.Up * 1.2f), width * 0.5f, SpellSchools.Color(spell.School));
        }
    }

    /// <summary>A totem was raised. Returns true when the facade drew it, in which case the totem
    /// builds no plain post of its own. <paramref name="totem"/> is positioned just after this call,
    /// so read its transform on the next frame.</summary>
    public static bool AttachTotem(Node3D totem, SpellResource? spell, IEntity? caster, Color tint) => false;

    /// <summary>A totem healed <paramref name="target"/> (its owner's body) this tick.</summary>
    public static void TotemPulse(Node3D totem, SpellResource? spell, Node3D? target)
    {
    }

    /// <summary>A totem ended: destroyed, expired or cancelled. May be called more than once (again,
    /// unbroken, as it leaves the tree); the first call is the one that counts.</summary>
    public static void TotemEnd(Node3D totem, SpellResource? spell, bool broken)
    {
    }

    // --- movement --------------------------------------------------------------------------------

    /// <summary>The caster dashed along the ground from <paramref name="from"/> to <paramref name="to"/>
    /// (both at the feet).</summary>
    public static void Dash(SpellResource spell, IEntity? caster, Vector3 from, Vector3 to)
    {
        if (!Active)
        {
            return;
        }

        // A streak of flashes along the line the caster travelled.
        int puffs = Mathf.Max(1, Mathf.CeilToInt(from.DistanceTo(to) / 2f));
        Color color = SpellSchools.Color(spell.School);
        for (int i = 0; i <= puffs; i++)
        {
            Flash(from.Lerp(to, (float)i / puffs) + Vector3.Up, 0.9f, color);
        }
    }

    /// <summary>The caster teleported from <paramref name="from"/> to <paramref name="to"/> (both at
    /// the feet).</summary>
    public static void Blink(SpellResource spell, IEntity? caster, Vector3 from, Vector3 to)
    {
        Color color = SpellSchools.Color(spell.School);
        Flash(from + Vector3.Up, 0.9f, color);
        Flash(to + Vector3.Up, 0.9f, color);
    }

    // --- statuses --------------------------------------------------------------------------------

    /// <summary>A status or school effect went off on <paramref name="target"/> at
    /// <paramref name="position"/> (its feet). <paramref name="radius"/> is how far it reached, 0 for
    /// one that touches only its bearer. <paramref name="spell"/> is the spell whose hit set it off,
    /// where there was one (a fed Kindle, a freeze, a dispel).</summary>
    public static void StatusProc(
        SpellProcKind kind, DamageType school, IEntity? target, Vector3 position, float radius,
        SpellResource? spell = null)
    {
        if (kind != SpellProcKind.WardBreak || !Active)
        {
            return;
        }

        // Scaled by the player's flash setting, so Reduced Motion shrinks it rather than strobing.
        float scale = Mathf.Max(0.3f, LiveComfort.Get().ScreenFlash);
        Flash(position + Vector3.Up, 1.6f * scale, SpellSchools.Color(school));
    }

    // --- the plain flash -------------------------------------------------------------------------

    /// <summary>An expanding, fading sphere of <paramref name="color"/>. The shape every effect was
    /// before it had one of its own, and what the forwards above still draw.</summary>
    public static void Flash(Vector3 position, float radius, Color color)
    {
        if (Root is { } root)
        {
            SpellFlash.Spawn(root, position, radius, color);
        }
    }
}
