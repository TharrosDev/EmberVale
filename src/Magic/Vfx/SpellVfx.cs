using System.Collections.Generic;
using Embervale.Animation;
using Embervale.Combat;
using Embervale.Core.Services;
using Embervale.Entities;
using Embervale.Player;
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
///
/// <para><b>HOW THIS IS LAID OUT (read before adding a spell's look).</b> One partial file per
/// concern; every public signature is frozen, every body is the generic interpreter:</para>
/// <list type="bullet">
/// <item><c>SpellVfx.cs</c> (this file): binding, the per-call context (<see cref="VfxCast"/>), the
/// tracking tables and <see cref="Clear"/>, <see cref="CastOrigin"/>, the plain <see cref="Flash"/>.</item>
/// <item><c>SpellVfx.Cast.cs</c>: <c>Windup</c>, <c>WindupProgress</c>, <c>WindupEnd</c>,
/// <c>Release</c>, <c>HandFlash</c>.</item>
/// <item><c>SpellVfx.Projectile.cs</c>: <c>AttachProjectile</c>, <c>DetachProjectile</c>, <c>Beam</c>.</item>
/// <item><c>SpellVfx.Impact.cs</c>: <c>Impact</c>, <c>Burst</c>, <c>Cone</c>, <c>Arc</c>, <c>Combo</c>,
/// and <c>Blast</c>, the one routine that turns a <see cref="VfxPlan"/> into blocks at a point.</item>
/// <item><c>SpellVfx.Ground.cs</c>: <c>GroundTelegraph</c>, <c>Meteor</c>, <c>GroundEnd</c>,
/// <c>AttachZone</c>, <c>ZoneEnd</c>.</item>
/// <item><c>SpellVfx.Structures.cs</c>: the barrier and totem calls.</item>
/// <item><c>SpellVfx.Movement.cs</c>: <c>Dash</c>, <c>Blink</c>.</item>
/// <item><c>SpellVfx.Status.cs</c>: <c>StatusProc</c>.</item>
/// <item><c>SpellVfx.Special.cs</c>: the per-spell hook table (<see cref="SpellVfxSpecial"/>), filled
/// by <c>SpellVfx.Special.Elemental.cs</c> and <c>SpellVfx.Special.Arcana.cs</c>.</item>
/// <item><c>SpellVfx.Kit.cs</c>: what each school's blast is made of beyond its recipe's flags, and
/// the richer pieces a special can call (<c>Explosion</c>, <c>FireBillow</c>, <c>SmokeColumn</c>,
/// <c>Debris</c>, <c>ShardBurst</c>, <c>GroundMist</c>, <c>Forks</c>, <c>FrostSpread</c>). Read its
/// header for the list of block capabilities added after the first renders.</item>
/// </list>
///
/// <para><b>THREE RULES EVERY LARGE ELEMENT OBEYS</b> (the first renders were white-outs):
/// soft layers pass through <see cref="VfxCoverageRules"/> (opacity falls with the share of the
/// frame covered; the white core is capped and brief); a blast is built from structure (rays, a thin
/// shock ring, an eroding ball, particles, debris, smoke), never from a bigger disc; and the tiers
/// differ in what is built (<see cref="VfxRichness"/>), not only in how many particles.</para>
///
/// <para><b>THE THREE EXTENSION POINTS, cheapest first.</b></para>
/// <list type="number">
/// <item><b>A recipe.</b> Register a <see cref="SpellVfxRecipe"/> for the spell id in
/// <c>SpellVfxCatalog.Elemental.cs</c> or <c>SpellVfxCatalog.Arcana.cs</c>. Each
/// <see cref="VfxStage"/> flag switches one block on in one beat; the interpreter here does the
/// rest. An authored recipe is drawn exactly as written: only a spell with no recipe gets the
/// automatic extras of <see cref="VfxRecipeRules.Enrich"/>.</item>
/// <item><b>A special.</b> Register a <see cref="SpellVfxSpecial"/> for the spell id in one of the
/// two <c>SpellVfx.Special.*.cs</c> files. Each hook runs at the top of one facade call with the
/// same <see cref="VfxCast"/> the generic code gets, builds blocks through <c>cast.Fx</c>
/// (<see cref="VfxSpawner"/>) and returns true to replace the generic effect or false to add to it.</item>
/// <item><b>A new block or preset.</b> A new <see cref="VfxEffect"/> subclass with a pool in the
/// director and a method on <see cref="VfxSpawner"/>. Only when a look cannot be built from the
/// blocks there are.</item>
/// </list>
/// </summary>
public static partial class SpellVfx
{
    private static SpellVfxDirector? _director;

    // What the facade remembers about effects that outlive the call that made them. Keyed by the
    // caster's runtime id or the placed node's instance id; holds handles only, never a node it
    // would have to free. Clear() empties all of it.
    private static readonly Dictionary<ulong, VfxRig> Auras = new();
    private static readonly Dictionary<ulong, VfxRig> Beams = new();
    private static readonly Dictionary<ulong, VfxRig> Projectiles = new();
    private static readonly Dictionary<ulong, VfxRig> Grounds = new();
    private static readonly Dictionary<ulong, VfxRig> Zones = new();
    private static readonly Dictionary<ulong, VfxRig> BarrierTelegraphs = new();
    private static readonly Dictionary<ulong, VfxRig> Barriers = new();
    private static readonly Dictionary<ulong, VfxRig> Totems = new();
    private static readonly (Vector3 Position, Color Tint, double At)[] RecentImpacts =
        new (Vector3, Color, double)[8];

    private static int _recentImpact;

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

    /// <summary>Forgets the director and everything tracked under it. Called between sessions.</summary>
    public static void Reset()
    {
        _director = null;
        Clear();
    }

    /// <summary>
    /// Drops everything the facade tracks about live effects (caster to aura, bolt to trail, node to
    /// zone). <see cref="SpellVfxDirector.KillAll"/> calls it after freeing the effect nodes, on a
    /// load and as the director leaves the tree, so nothing here may touch a node again.
    /// </summary>
    internal static void Clear()
    {
        Auras.Clear();
        Beams.Clear();
        Projectiles.Clear();
        Grounds.Clear();
        Zones.Clear();
        BarrierTelegraphs.Clear();
        Barriers.Clear();
        Totems.Clear();
        System.Array.Clear(RecentImpacts);
        _recentImpact = 0;
    }

    /// <summary>
    /// Where a caster's effects start from: the casting hand when the body has one, else
    /// <paramref name="fallback"/> (the aim origin). For drawing only; the aim, the muzzle and the
    /// collision path never move.
    /// </summary>
    public static Vector3 CastOrigin(IEntity? caster, Vector3 fallback)
    {
        if (!Active || caster == null || VfxAnchor.BodyOf(caster) == null)
        {
            return fallback;
        }

        // The player's own hand is off the bottom of the frame in first person: their effects
        // start from the casting point fixed in the view instead.
        if (IsPlayer(caster) && TryFirstPersonHand(VfxAnchor.BodyOf(caster)!, out Vector3 inView))
        {
            return inView;
        }

        CharacterAnimationComponent? animation = caster.GetComponent<CharacterAnimationComponent>();
        return animation != null && GodotObject.IsInstanceValid(animation) &&
               animation.TryGetCastingHand(out Vector3 hand)
            ? hand
            : fallback;
    }

    /// <summary>Whether <paramref name="entity"/> is the player (for an anchor deciding whether the
    /// first-person casting point applies to it).</summary>
    internal static bool IsLocalPlayer(IEntity? entity) => IsPlayer(entity);

    /// <summary>
    /// The first-person casting point, when the camera is in <paramref name="body"/>'s head: a point
    /// low and to the left in the view (the left hand casts), past the near fade and clear of the crosshair
    /// (<see cref="VfxViewRules"/>). False in third person, or with no camera.
    /// </summary>
    internal static bool TryFirstPersonHand(Node3D body, out Vector3 position)
    {
        position = default;
        if (_director is not { HasCamera: true } director || !GodotObject.IsInstanceValid(director))
        {
            return false;
        }

        Vector3 eye = body.GlobalPosition + (Vector3.Up * VfxViewRules.EyeHeight);
        if (!VfxViewRules.IsFirstPerson(director.CameraPosition - eye))
        {
            return false;
        }

        position = VfxViewRules.HandPoint(director.CameraPosition, director.CameraBasis);
        return true;
    }

    /// <summary>
    /// The school colour of a spell that struck within reach of <paramref name="position"/> in the
    /// last few frames. The melee spark (<c>ImpactEffect</c>) is told only a colour and a size by the
    /// combat feedback layer, never a school, so this is how it finds out it is marking a spell hit.
    /// </summary>
    internal static bool TryRecentImpactTint(Vector3 position, out Color tint)
    {
        tint = default;
        if (!Active)
        {
            return false;
        }

        double now = _director!.Now;
        float best = RecentImpactReach * RecentImpactReach;
        bool found = false;
        for (int i = 0; i < RecentImpacts.Length; i++)
        {
            (Vector3 at, Color colour, double when) = RecentImpacts[i];
            if (colour.A <= 0f || now - when > RecentImpactSeconds || now < when)
            {
                continue;
            }

            float distance = at.DistanceSquaredTo(position);
            if (distance <= best)
            {
                best = distance;
                tint = colour;
                found = true;
            }
        }

        return found;
    }

    private const float RecentImpactReach = 2f;
    private const double RecentImpactSeconds = 0.12d;

    // --- the plain flash -------------------------------------------------------------------------

    /// <summary>An expanding, fading flash of <paramref name="color"/>. The shape every effect was
    /// before it had one of its own; <see cref="SpellFlash"/> now draws it as a flare.</summary>
    public static void Flash(Vector3 position, float radius, Color color)
    {
        if (Root is { } root)
        {
            SpellFlash.Spawn(root, position, radius, color);
        }
    }

    /// <summary>The flare a plain <see cref="SpellFlash"/> is drawn as, in a colour with no school
    /// behind it. False when nothing was drawn and the caller should draw its own sphere.</summary>
    internal static bool FlareFlash(Vector3 position, float radius, Color color)
    {
        if (!Active)
        {
            return false;
        }

        VfxSpawner fx = _director!.Open(position, byPlayer: false);
        if (fx.IsNone)
        {
            return true; // dropped by the budget or the distance, which is still "handled"
        }

        var colors = new VfxSchoolColors(
            color.Lerp(Colors.White, 0.6f), color, color.Darkened(0.3f), 6f, 3.5f, 1.6f, 0.35f);
        VfxFlareSpec flare = VfxFlareSpec.At(position, Mathf.Max(0.1f, radius * 0.55f), colors);
        flare.Ring = radius >= 0.8f;
        flare.RingRadius = radius;
        flare.Light = radius >= 0.8f;
        flare.LightRange = Mathf.Max(2f, radius * 2.5f);
        fx.Flare(flare);
        return true;
    }

    // --- the per-call context --------------------------------------------------------------------

    /// <summary>
    /// Opens an effect for <paramref name="spell"/> at <paramref name="at"/>. False when nothing is
    /// drawn (no director, headless, too far), in which case the caller simply returns.
    /// <paramref name="essential"/>, <paramref name="sustained"/> and <paramref name="measured"/>
    /// are <see cref="SpellVfxDirector.Open"/>'s.
    /// </summary>
    private static bool Begin(
        SpellResource? spell, IEntity? caster, Vector3 at, out VfxCast cast, bool essential = false,
        bool sustained = false, bool measured = false)
    {
        cast = default;
        if (!Active || spell == null)
        {
            return false;
        }

        bool byPlayer = IsPlayer(caster);
        VfxSpawner fx = _director!.Open(at, byPlayer, essential, sustained, measured);
        if (fx.IsNone)
        {
            return false;
        }

        cast = new VfxCast(
            spell, caster, spell.School, byPlayer, VfxPalette.For(spell.School, byPlayer),
            SpellVfxCatalog.For(spell), SpellVfxCatalog.Has(spell.Id), Mathf.Clamp(spell.ImpactWeight, 0f, 1f), fx);
        return true;
    }

    /// <summary>Opens an effect that has a school but, perhaps, no spell (a status going off, a
    /// status jumping bearers). It is drawn from the school's fallback recipe.</summary>
    private static bool BeginSchool(
        DamageType school, IEntity? source, Vector3 at, SpellResource? spell, out VfxCast cast)
    {
        if (spell != null)
        {
            return Begin(spell, source, at, out cast);
        }

        cast = default;
        if (!Active)
        {
            return false;
        }

        bool byPlayer = IsPlayer(source);
        VfxSpawner fx = _director!.Open(at, byPlayer);
        if (fx.IsNone)
        {
            return false;
        }

        cast = new VfxCast(
            null, source, school, byPlayer, VfxPalette.For(school, byPlayer),
            SpellVfxCatalog.Fallback(school, SpellDelivery.Projectile), false, 0.5f, fx);
        return true;
    }

    /// <summary>Whether <paramref name="entity"/> is the player. A freed entity is nobody.</summary>
    private static bool IsPlayer(IEntity? entity) =>
        entity != null && VfxAnchor.BodyOf(entity) != null && CombatPerspective.IsPlayer(entity);

    /// <summary>Whether the player stands within <paramref name="radius"/> of a point.</summary>
    private static bool PlayerWithin(Vector3 centre, float radius)
    {
        if (ServiceLocator.Instance is not { } locator || !locator.TryGet(out PlayerCharacter player) ||
            VfxAnchor.BodyOf(player) is not { } body)
        {
            return false;
        }

        return (body.GlobalPosition + Vector3.Up).DistanceTo(centre) <= radius;
    }

    /// <summary>Metres from the player's chest to a point, or a very long way with no player.</summary>
    private static float PlayerDistance(Vector3 centre)
    {
        if (ServiceLocator.Instance is not { } locator || !locator.TryGet(out PlayerCharacter player) ||
            VfxAnchor.BodyOf(player) is not { } body)
        {
            return float.MaxValue;
        }

        return (body.GlobalPosition + Vector3.Up).DistanceTo(centre);
    }

    /// <summary>A node's key in the tracking tables.</summary>
    private static ulong KeyOf(Node node) => node.GetInstanceId();

    /// <summary>Stops and forgets whatever a table holds under <paramref name="key"/>.</summary>
    private static bool StopRig(Dictionary<ulong, VfxRig> table, ulong key)
    {
        if (!table.Remove(key, out VfxRig? rig))
        {
            return false;
        }

        rig.Stop();
        return true;
    }

    /// <summary>Drops table entries whose effects have all ended (recycled by the budget, or their
    /// owner never said goodbye). Called when a table is added to, so none of them grows.</summary>
    private static void Prune(Dictionary<ulong, VfxRig> table)
    {
        if (table.Count < 48)
        {
            return;
        }

        List<ulong>? dead = null;
        foreach (KeyValuePair<ulong, VfxRig> pair in table)
        {
            if (!pair.Value.AnyLive)
            {
                (dead ??= new List<ulong>()).Add(pair.Key);
            }
        }

        if (dead != null)
        {
            foreach (ulong key in dead)
            {
                table.Remove(key);
            }
        }
    }
}

/// <summary>
/// Everything one facade call knows, gathered once: the spell and who cast it, the school's colours
/// at the right strength (an enemy's are dimmer), the recipe, and the <see cref="VfxSpawner"/> the
/// blocks are built through. The generic interpreter and every <see cref="SpellVfxSpecial"/> hook
/// are handed the same one.
/// </summary>
internal readonly struct VfxCast
{
    public VfxCast(
        SpellResource? spell, IEntity? caster, DamageType school, bool byPlayer, VfxSchoolColors colors,
        SpellVfxRecipe recipe, bool authored, float weight, VfxSpawner fx)
    {
        Spell = spell;
        Caster = caster;
        School = school;
        ByPlayer = byPlayer;
        Colors = colors;
        Recipe = recipe;
        Authored = authored;
        Weight = weight;
        Fx = fx;
    }

    /// <summary>The spell, or null for an effect that has only a school (a status going off).</summary>
    public SpellResource? Spell { get; }

    public IEntity? Caster { get; }

    public DamageType School { get; }

    public bool ByPlayer { get; }

    /// <summary>The school's colours, already dimmed for a cast that is not the player's.</summary>
    public VfxSchoolColors Colors { get; }

    public SpellVfxRecipe Recipe { get; }

    /// <summary>Whether <see cref="Recipe"/> is the spell's own (drawn exactly) or a fallback
    /// (filled out by <see cref="VfxRecipeRules.Enrich"/>).</summary>
    public bool Authored { get; }

    /// <summary>The spell's <c>ImpactWeight</c>, 0..1.</summary>
    public float Weight { get; }

    /// <summary>Builds this effect's blocks.</summary>
    public VfxSpawner Fx { get; }

    /// <summary>The recipe's stage for a role, as written.</summary>
    public VfxStage StageOf(VfxRole role) => role switch
    {
        VfxRole.Cast => Recipe.Cast,
        VfxRole.Travel => Recipe.Travel,
        VfxRole.Linger => Recipe.Linger,
        _ => Recipe.Impact,
    };

    /// <summary>The blocks one beat is built from: the recipe's stage for <paramref name="role"/>,
    /// filled out when it is a fallback, then cut to the tier, the distance and the comfort settings.</summary>
    public VfxPlan Plan(VfxRole role, float radius = 0f, bool hitsPlayer = false)
    {
        var traits = new VfxTraits(
            School, Spell?.Delivery ?? SpellDelivery.Projectile, Weight, radius, Authored);
        return PlanOf(VfxRecipeRules.Enrich(StageOf(role), role, traits), radius, hitsPlayer);
    }

    /// <summary>The blocks for a stage exactly as given (never filled out), cut to the budget.</summary>
    public VfxPlan PlanOf(VfxStage stage, float radius = 0f, bool hitsPlayer = false) =>
        VfxRecipeRules.Plan(
            stage, VfxQuality.Budget, Fx.Detail, VfxQuality.ReducedMotion, ByPlayer, hitsPlayer, radius, School);
}

/// <summary>
/// The handles of one lasting effect (an aura, a bolt in flight, a zone, a wall), so the call that
/// ends it can stop every block it was built from. A few are named, for the calls that keep
/// adjusting one block while the effect lasts (a charge filling, a beam being re-aimed).
/// </summary>
internal sealed class VfxRig
{
    private readonly List<VfxHandle<VfxEffect>> _all = new();

    /// <summary>The spell this effect belongs to.</summary>
    public SpellResource? Spell { get; set; }

    public VfxHandle<VfxFlare> Flare { get; set; }

    public VfxHandle<VfxBurst> Stream { get; set; }

    public VfxHandle<VfxBolt> Bolt { get; set; }

    /// <summary>The density a stream was started at, for a charge that thickens it.</summary>
    public float Density { get; set; }

    /// <summary>Where a beam was last cut short by something it struck, and when.</summary>
    public float ClipDistance { get; set; }

    public double ClipAt { get; set; } = -10d;

    /// <summary>Whether the facade drew the thing itself (as opposed to adding to a plain shape).</summary>
    public bool Drew { get; set; }

    public bool AnyLive
    {
        get
        {
            for (int i = 0; i < _all.Count; i++)
            {
                if (_all[i].IsLive)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>Tracks a block. An empty handle (a block the budget did not draw) is ignored.</summary>
    public VfxHandle<T> Add<T>(VfxHandle<T> handle)
        where T : VfxEffect
    {
        if (handle.IsLive)
        {
            _all.Add(handle.Untyped());
        }

        return handle;
    }

    /// <summary>Ends every block gracefully: flares fade, emitters stop and let their particles die.</summary>
    public void Stop()
    {
        for (int i = 0; i < _all.Count; i++)
        {
            _all[i].Stop();
        }
    }

    /// <summary>Ends every block now.</summary>
    public void Kill()
    {
        for (int i = 0; i < _all.Count; i++)
        {
            _all[i].Kill();
        }
    }
}
