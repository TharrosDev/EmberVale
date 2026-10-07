using Embervale.Combat;
using Embervale.Entities;
using Godot;

namespace Embervale.Magic.Vfx;

public static partial class SpellVfx
{
    /// <summary>
    /// The special cases of the fire, frost and lightning spells (the spells whose recipes live in
    /// <c>SpellVfxCatalog.Elemental.cs</c>). See <see cref="SpellVfxSpecial"/> for what a hook may
    /// do, and the header of <c>SpellVfx.cs</c> for when to reach for one.
    ///
    /// <para><b>Two rules every hook here keeps.</b></para>
    /// <list type="bullet">
    /// <item><b>A big effect is built from structure, not from a big flare.</b> A flare's halo is two
    /// and a half times its core, so a flare sized to a 4 m blast paints most of the frame one flat
    /// colour. Here a blast is a small core that is gone in about 0.14 s (never wider than
    /// <see cref="CoreCap"/>, and smaller the nearer the camera is), one shock ring out to the true
    /// radius at a fraction of the school's strength (<see cref="RingEnergy"/>), a body of eroding
    /// noise where the school has one, and particles, smoke and marks. Nothing that is centred on
    /// the caster uses a sphere at all: the camera sits inside it.</item>
    /// <item><b>The tiers are different pictures.</b> Performance is the core, one ring and one
    /// particle layer. Medium adds the bodies of noise, smoke and marks. High adds distortion, more
    /// strands and a second layer of most things; Ultra adds a third.</item>
    /// </list>
    /// </summary>
    private static void RegisterElementalSpecials(SpellVfxSpecialTable table)
    {
        SpellVfxSpecial emberlash = table.For("spell.emberlash");
        emberlash.Projectile = EmberlashBolt;
        emberlash.Proc = KindleCatches;

        SpellVfxSpecial flameLance = table.For("spell.flame_lance");
        flameLance.Release = FireRelease;
        flameLance.Projectile = FlameLanceBolt;
        flameLance.Proc = KindleCatches;

        SpellVfxSpecial pyreWall = table.For("spell.pyre_wall");
        pyreWall.BarrierTelegraph = WallLine;
        pyreWall.Barrier = PyreWallSmoke;
        pyreWall.Proc = KindleCatches;

        SpellVfxSpecial sunfall = table.For("spell.sunfall");
        sunfall.Release = FireRelease;
        sunfall.GroundTelegraph = SunfallMeteor;
        sunfall.Burst = SunfallBlast;
        sunfall.Proc = KindleCatches;

        table.For("spell.rime_shard").Impact = RimeShardHit;
        table.For("spell.frost_nova").Burst = FrostNovaBlast;

        SpellVfxSpecial blizzard = table.For("spell.blizzard");
        blizzard.Zone = BlizzardZone;
        blizzard.Burst = BlizzardPulse;

        SpellVfxSpecial bulwark = table.For("spell.glacial_bulwark");
        bulwark.BarrierTelegraph = WallLine;
        bulwark.Barrier = GlacialBulwarkWall;

        SpellVfxSpecial ballLightning = table.For("spell.ball_lightning");
        ballLightning.Projectile = BallLightningOrb;
        ballLightning.Impact = BallLightningHit;
        ballLightning.Arc = LightningArc;

        SpellVfxSpecial stormConduit = table.For("spell.storm_conduit");
        stormConduit.Beam = StormConduitBeam;
        stormConduit.Projectile = StormConduitTick;
        stormConduit.Impact = StormConduitHit;
        stormConduit.Arc = LightningArc;

        SpellVfxSpecial thunderStep = table.For("spell.thunder_step");
        thunderStep.Dash = ThunderStepStrike;
        thunderStep.Arc = LightningArc;

        SpellVfxSpecial stormbrand = table.For("spell.stormbrand");
        stormbrand.Impact = StormbrandHit;
        stormbrand.Arc = LightningArc;
    }

    /// <summary>The widest a blast's core flare is ever drawn, metres. Its halo is 2.5 times this.</summary>
    private const float CoreCap = 1.1f;

    /// <summary>Seconds a blast's white-hot core lasts.</summary>
    private const float CoreSeconds = 0.14f;

    /// <summary>
    /// A shock ring's strength against the school's. The ring block draws its band at the school's
    /// core energy and fills the inside of the band with a third of that, so at full strength a ring
    /// around the caster is a white disc on the floor (frost and lightning clip to white first):
    /// the dome this file exists to remove, lying down. At this strength the band keeps its colour
    /// and the fill is a faint wash.
    /// </summary>
    private const float RingEnergy = 0.3f;

    /// <summary>The widest a blast's core is drawn against its distance from the camera. The halo is
    /// 2.5 times the core, so this keeps the whole flash under about a quarter of the frame's width
    /// however close the blast is (a nova, a dash's end and a point-blank Sunfall are at the caster).</summary>
    private const float CorePerMetre = 0.12f;

    /// <summary>The colour cold mist is drawn in. Smoke is unlit, so this is as bright as it gets at
    /// any hour: a pale tint is a glowing white bank at dusk, and reads as the dome coming back.</summary>
    private static readonly Color MistTint = new(0.3f, 0.4f, 0.5f);

    /// <summary>The colour of the rock a meteor throws up: dull and hot, not white.</summary>
    private static readonly Color DebrisTint = new(1f, 0.42f, 0.1f);

    /// <summary>The colour of the screen flash for the one blast that has one: near white, so the
    /// frame is lifted for an instant and not tinted.</summary>
    private static readonly Color BlastWhite = new(1f, 0.97f, 0.92f);

    // --- shared pieces ---------------------------------------------------------------------------

    /// <summary>A number in 0..1 from the director's seeded stream.</summary>
    private static float Roll() => (_director!.NextSeed() & 0xFFFF) / 65535f;

    /// <summary>Whether a point at a caster's hand is at the camera: the player casting in first person.</summary>
    private static bool AtTheEye(Vector3 point) =>
        _director is { HasCamera: true } director && director.DistanceToCamera(point) < 1.1f;

    /// <summary>The brief hot heart of a blast: bright, small and gone in <see cref="CoreSeconds"/>.</summary>
    private static void BlastCore(in VfxCast cast, Vector3 at, float radius, float lightRange)
    {
        float near = _director is { HasCamera: true } director
            ? Mathf.Max(0.25f, director.DistanceToCamera(at) * CorePerMetre)
            : CoreCap;
        VfxFlareSpec core = VfxFlareSpec.At(at, Mathf.Min(Mathf.Min(CoreCap, near), radius), cast.Colors);
        core.Life = CoreSeconds;
        core.Light = VfxQuality.Budget.MaxLights > 0;
        core.LightRange = lightRange;
        cast.Fx.Flare(core);
    }

    /// <summary>A shock ring and nothing else: no core and no halo, at <see cref="RingEnergy"/> of
    /// the school's strength (times <paramref name="energy"/>, for a ring seen from far off).</summary>
    private static void ThinRing(
        in VfxCast cast, Vector3 at, float radius, float life, Vector3 normal = default, bool inward = false,
        float energy = 1f)
    {
        VfxFlareSpec ring = VfxFlareSpec.At(at, 0.2f, cast.Colors.Scaled(RingEnergy * energy, 1f));
        ring.NoCore = true;
        ring.Ring = true;
        ring.RingRadius = radius;
        ring.RingNormal = normal;
        ring.Inward = inward;
        ring.Life = life;
        cast.Fx.Flare(ring);
    }

    /// <summary>A jagged strike between two points. <paramref name="jitter"/> is its sideways kink as
    /// a fraction of its length: a short fork can be wild, a long strand has to stay on its line.</summary>
    private static void Fork(
        in VfxCast cast, Vector3 from, Vector3 to, float width, float life, int branches = 0, float jitter = 0.16f)
    {
        float length = from.DistanceTo(to);
        cast.Fx.Bolt(new VfxBoltSpec
        {
            Mode = VfxBoltMode.Lightning,
            From = from,
            To = to,
            Colors = cast.Colors,
            Width = width,
            Jitter = Kink(jitter, length),
            Segments = VfxRecipeRules.BoltSegments(VfxQuality.Budget, length),
            Branches = branches,
            Life = life,
            Seed = _director!.NextSeed(),
        });
    }

    /// <summary>A bolt's sideways kink for its length. The kink is a fraction of the length, so the
    /// same fraction that makes a 2 m fork lively throws the middle of an 18 m beam a metre off its
    /// line; this holds the swing of a long bolt to under half a metre.</summary>
    private static float Kink(float jitter, float length) => Mathf.Min(jitter, 0.9f / Mathf.Max(0.5f, length));

    /// <summary>How far above and below the floor a mark on it reaches. Kept low: a mark is a decal,
    /// and a decal paints everything inside its box, not only the ground.</summary>
    private static float FloorReach(float radius) => Mathf.Clamp(radius * 0.15f, 0.5f, 0.9f);

    /// <summary>The school's colours for a body of fire: its heart is the hot body colour, not the
    /// near-white of a flare's core, so a billow is flame all the way through and not a white ball.</summary>
    private static VfxSchoolColors Flame(in VfxSchoolColors colors) => colors with
    {
        Core = colors.Core.Lerp(colors.Mid, 0.55f),
        CoreEnergy = colors.MidEnergy * 1.25f,
    };

    /// <summary>Forks thrown out from a point, biased down toward the floor: where lightning grounds.</summary>
    private static void GroundForks(in VfxCast cast, Vector3 from, int count, float reach, float drop, float width)
    {
        float turn = Roll() * Mathf.Tau;
        for (int i = 0; i < count; i++)
        {
            float angle = turn + ((i + (Roll() * 0.6f)) * Mathf.Tau / count);
            float far = reach * (0.6f + (0.4f * Roll()));
            var to = new Vector3(from.X + (Mathf.Cos(angle) * far), from.Y - drop, from.Z + (Mathf.Sin(angle) * far));
            Fork(cast, from, to, width, 0.16f + (0.08f * Roll()));
        }
    }

    /// <summary>
    /// The line a wall is about to stand on: a narrow strip of light that fills for the delay, with
    /// the school's particles lifting off its length. Slimmer and fainter than the generic line,
    /// which with the spell's own telegraph ring around it reads as a stack of flat discs.
    /// </summary>
    private static bool WallLine(in VfxCast cast, VfxRig rig, Node3D barrier, float width, float delay)
    {
        width = Mathf.Max(0.5f, width);
        VfxDiscSpec line = VfxDiscSpec.At(Vector3.Zero, width * 0.5f, cast.Colors);
        line.Life = Mathf.Max(0.1f, delay);
        line.Fills = true;
        line.Depth = Mathf.Clamp(0.7f / width, 0.08f, 0.4f);
        line.Body = 0.35f;
        line.Rim = 0.4f;
        line.Energy = 0.8f;
        VfxHandle<VfxDisc> disc = rig.Add(cast.Fx.Disc(line));
        if (disc.Get is { } drawn)
        {
            drawn.Follow(VfxAnchor.To(barrier));
            drawn.OrientLike(barrier);
        }

        if (delay < 0.25f)
        {
            return true;
        }

        VfxParticles lifting = cast.School == DamageType.Frost ? VfxParticles.Motes : VfxParticles.Embers;
        VfxBurstSpec along = VfxBurstSpec.At(
            Vector3.Zero, cast.Colors, VfxQuality.Budget.ParticleMultiplier * Mathf.Clamp(width * 0.25f, 0.5f, 1.5f));
        along.Continuous = true;
        along.Extents = new Vector3(width * 0.5f, 0.05f, 0.12f);
        along.Direction = Vector3.Up;
        along.Spread = 15f;
        along.SpeedScale = 0.8f;
        VfxHandle<VfxBurst> stream = rig.Add(cast.Fx.Burst(lifting, along));
        if (stream.Get is { } emitter)
        {
            emitter.Follow(VfxAnchor.To(barrier, Vector3.Up * 0.15f));
            emitter.OrientLike(barrier);
        }

        return true;
    }

    // --- fire ------------------------------------------------------------------------------------

    /// <summary>
    /// The release of a heavy fire spell. The generic snap adds a streak across the whole frame for a
    /// heavy or fully charged cast; a fire spell leaves the hand as a gout instead: a flash the size
    /// of the hand's glow, embers along the aim and (richer tiers) sparks with them.
    /// </summary>
    private static bool FireRelease(in VfxCast cast, Vector3 hand, Vector3 direction, float charge)
    {
        VfxPlan plan = cast.Plan(VfxRole.Cast);
        VfxBudget budget = VfxQuality.Budget;
        float full = Mathf.Clamp(charge, 0f, 1f);

        // The hand is two or three metres from the third-person camera, and a flare's halo is six
        // times its radius across: past this size the release is a flash over half the frame.
        float size = Mathf.Min(0.45f, (0.24f + (0.16f * cast.Weight) + (0.16f * full)) * plan.Scale);
        VfxFlareSpec snap = VfxFlareSpec.At(hand, size, cast.Colors);
        snap.Life = 0.18f;
        snap.Light = plan.Light;
        snap.LightRange = 5f;
        cast.Fx.Flare(snap);

        // A ground spell is thrown up at the sky, a bolt along the aim.
        Vector3 along = cast.Spell?.Delivery == SpellDelivery.Ground || direction.LengthSquared() < 0.0001f
            ? Vector3.Up
            : direction.Normalized();
        VfxBurstSpec spit = VfxBurstSpec.At(hand, cast.Colors, plan.Density * (0.7f + (0.6f * full)));
        spit.Direction = along;
        spit.Spread = 24f;
        spit.SpeedScale = 1.6f;
        spit.LifeScale = 0.6f;
        cast.Fx.Burst(VfxParticles.Embers, spit);
        if (budget.SecondaryDebris)
        {
            spit.Density = budget.ParticleMultiplier * (0.4f + (0.5f * full));
            spit.Spread = 16f;
            spit.SpeedScale = 1.1f;
            cast.Fx.Burst(VfxParticles.Sparks, spit);
        }

        return true;
    }

    private static bool EmberlashBolt(
        in VfxCast cast, VfxRig rig, Node3D projectile, Vector3 handOffset, Vector3 direction, float charge)
    {
        FireBody(cast, rig, projectile, handOffset, direction, charge, stretch: 1f);
        return false;
    }

    private static bool FlameLanceBolt(
        in VfxCast cast, VfxRig rig, Node3D projectile, Vector3 handOffset, Vector3 direction, float charge)
    {
        // A lance: three times as long as it is wide, and longer still for a full charge.
        FireBody(cast, rig, projectile, handOffset, direction, charge, stretch: 3f + Mathf.Clamp(charge, 0f, 1f));
        return false;
    }

    /// <summary>
    /// The body of a bolt of fire: flowing noise around the generic core (Medium and up), and dark
    /// smoke shed behind it (High and up). Added to the bolt's rig, so it ends with the bolt.
    /// </summary>
    private static void FireBody(
        in VfxCast cast, VfxRig rig, Node3D projectile, Vector3 handOffset, Vector3 direction, float charge,
        float stretch)
    {
        VfxBudget budget = VfxQuality.Budget;
        if (!budget.SecondaryDebris || cast.Spell is not { } spell)
        {
            return;
        }

        float full = Mathf.Clamp(charge, 0f, 1f);
        float size = (0.2f + (0.16f * cast.Weight) + (0.2f * full)) * cast.Plan(VfxRole.Travel).Scale;
        Vector3 origin = projectile.GlobalPosition + handOffset;
        VfxAnchor anchor = VfxAnchor.To(projectile);
        bool aimed = direction.LengthSquared() > 0.0001f;
        Vector3 aim = aimed ? direction.Normalized() : Vector3.Forward;
        Vector3 velocity = aimed ? aim * Mathf.Max(0f, spell.ProjectileSpeed) : Vector3.Zero;

        VfxShellSpec body = VfxShellSpec.Sphere(origin, size * 0.8f, Flame(cast.Colors));
        body.Sustain = true;
        body.Layered = true;
        body.Scroll = new Vector2(0.3f, 1.7f);
        if (stretch > 1f && aimed)
        {
            body.Size = new Vector3(size * 1.3f, size * 1.3f, size * 1.3f * stretch);
            body.Forward = aim;
        }

        VfxHandle<VfxShell> shell = rig.Add(cast.Fx.Shell(body));
        if (shell.Get is { } flow)
        {
            flow.Follow(anchor);
            flow.SettleFrom(handOffset);
            flow.Glide(velocity);
        }

        if (VfxQuality.Tier < VfxTier.High)
        {
            return;
        }

        // Thin and short lived: it flies down the player's own line of sight.
        VfxBurstSpec smoke = VfxBurstSpec.At(origin, cast.Colors, budget.ParticleMultiplier * budget.DebrisMultiplier * 0.35f);
        smoke.Continuous = true;
        smoke.Extents = Vector3.One * (size * 0.25f);
        smoke.SpeedScale = 0.3f;
        smoke.SizeScale = Mathf.Clamp(size * 1.4f, 0.3f, 0.75f);
        smoke.LifeScale = 0.5f;
        Shed(rig, cast, VfxParticles.Smoke, smoke, anchor, handOffset, velocity);
    }

    /// <summary>
    /// A fire hit fed a Kindled target another stack: a tongue of flame up the bearer, a thin ring
    /// around its chest, and over its head one pip of fire for every stack it now burns with, so the
    /// count to the detonation can be read. (The detonation is drawn by the status itself.)
    /// </summary>
    private static bool KindleCatches(
        in VfxCast cast, SpellProcKind kind, IEntity? target, Vector3 position, float radius)
    {
        if (kind != SpellProcKind.KindleFed)
        {
            return false;
        }

        VfxBudget budget = VfxQuality.Budget;
        Vector3 chest = position + Vector3.Up;
        VfxFlareSpec lick = VfxFlareSpec.At(chest, 0.36f, cast.Colors);
        lick.Life = 0.2f;
        VfxHandle<VfxFlare> flare = cast.Fx.Flare(lick);
        if (VfxAnchor.BodyOf(target) is { } body)
        {
            flare.Get?.Follow(VfxAnchor.To(body, Vector3.Up));
        }

        ThinRing(cast, chest, 0.75f, 0.3f, energy: 1.5f);
        KindlePips(cast, target);
        VfxBurstSpec embers = VfxBurstSpec.At(position + (Vector3.Up * 0.5f), cast.Colors, budget.ParticleMultiplier * 0.9f);
        embers.Direction = Vector3.Up;
        embers.Spread = 18f;
        embers.SpeedScale = 1.5f;
        embers.Extents = new Vector3(0.3f, 0.4f, 0.3f);
        cast.Fx.Burst(VfxParticles.Embers, embers);
        if (budget.SecondaryDebris)
        {
            VfxBurstSpec smoke = VfxBurstSpec.At(chest + (Vector3.Up * 0.6f), cast.Colors, budget.ParticleMultiplier * 0.4f);
            smoke.SizeScale = 0.5f;
            smoke.LifeScale = 0.6f;
            cast.Fx.Burst(VfxParticles.Smoke, smoke);
        }

        return true;
    }

    /// <summary>The most Burning stacks a bearer's pips count out.</summary>
    private const int MaxKindlePips = 5;

    /// <summary>A row of small flames over a Kindled bearer's head, one for each Burning stack. The
    /// count is only read to be drawn.</summary>
    private static void KindlePips(in VfxCast cast, IEntity? target)
    {
        if (VfxAnchor.BodyOf(target) is not { } body || _director is not { HasCamera: true } director)
        {
            return;
        }

        int stacks = Mathf.Min(
            MaxKindlePips, target!.GetComponent<StatusEffectsComponent>()?.StacksOf(StatusIds.Burning) ?? 0);
        if (stacks <= 0)
        {
            return;
        }

        // Laid out across the view, so the row is a row from wherever it is seen.
        Vector3 across = director.CameraForward.Cross(Vector3.Up);
        across = across.LengthSquared() < 0.0001f ? Vector3.Right : across.Normalized();
        for (int i = 0; i < stacks; i++)
        {
            Vector3 offset = (Vector3.Up * 2.25f) + (across * ((i - ((stacks - 1) * 0.5f)) * 0.2f));
            VfxFlareSpec pip = VfxFlareSpec.At(body.GlobalPosition + offset, 0.06f, cast.Colors);
            pip.Life = 0.9f;
            cast.Fx.Flare(pip).Get?.Follow(VfxAnchor.To(body, offset));
        }
    }

    /// <summary>Smoke rolling off the top of the wall of fire (Medium and up) and sparks spat out of
    /// it (Ultra). The wall itself is the generic sheet, which is left exactly as it is.</summary>
    private static bool PyreWallSmoke(in VfxCast cast, VfxRig rig, Node3D barrier, float width, float height)
    {
        VfxBudget budget = VfxQuality.Budget;
        if (!budget.SecondaryDebris)
        {
            return false;
        }

        width = Mathf.Max(0.5f, width);
        float along = Mathf.Clamp(width * 0.25f, 0.5f, 1.6f);
        VfxBurstSpec smoke = VfxBurstSpec.At(
            Vector3.Zero, cast.Colors, budget.ParticleMultiplier * budget.DebrisMultiplier * 0.5f * along);
        smoke.Continuous = true;
        smoke.Extents = new Vector3(width * 0.45f, 0.2f, 0.15f);
        smoke.Direction = Vector3.Up;
        smoke.Spread = 20f;
        smoke.SpeedScale = 0.9f;
        smoke.SizeScale = 0.9f;
        VfxHandle<VfxBurst> rising = rig.Add(cast.Fx.Burst(VfxParticles.Smoke, smoke));
        if (rising.Get is { } emitter)
        {
            emitter.Follow(VfxAnchor.To(barrier, Vector3.Up * Mathf.Max(0.5f, height)));
            emitter.OrientLike(barrier);
        }

        if (VfxQuality.Tier == VfxTier.Ultra)
        {
            VfxBurstSpec sparks = VfxBurstSpec.At(Vector3.Zero, cast.Colors, budget.ParticleMultiplier * 0.3f * along);
            sparks.Continuous = true;
            sparks.Extents = new Vector3(width * 0.5f, 0.5f, 0.1f);
            sparks.Direction = Vector3.Up;
            sparks.Spread = 50f;
            sparks.SpeedScale = 0.5f;
            sparks.GravityScale = 0.3f;
            VfxHandle<VfxBurst> spat = rig.Add(cast.Fx.Burst(VfxParticles.Sparks, sparks));
            if (spat.Get is { } spitting)
            {
                spitting.Follow(VfxAnchor.To(barrier, Vector3.Up * 0.8f));
                spitting.OrientLike(barrier);
            }
        }

        return false;
    }

    /// <summary>
    /// Sunfall's delay: a thin-rimmed mark on the ground that fills, embers lifting off it, and the
    /// meteor itself. The head is a ball of fire with a hot core (not a streak), it drags a long fat
    /// trail, and it sheds embers, smoke and (Ultra) sparks all the way down.
    /// </summary>
    private static bool SunfallMeteor(in VfxCast cast, VfxRig rig, Node3D ground, float radius, float delay)
    {
        radius = Mathf.Max(0.5f, radius);
        VfxBudget budget = VfxQuality.Budget;
        bool rich = budget.SecondaryDebris;
        float density = budget.ParticleMultiplier;
        VfxAnchor floor = VfxAnchor.To(ground);

        // The telegraph ring the spell already draws says where; this only says "fire".
        VfxDiscSpec disc = VfxDiscSpec.At(Vector3.Zero, radius, cast.Colors);
        disc.Life = Mathf.Max(0.1f, delay);
        disc.Fills = true;
        disc.Spin = 0.5f;
        disc.Flow = 0.6f;
        disc.Body = 0.16f;
        disc.Rim = 0.55f;
        disc.Energy = 0.7f;
        rig.Add(cast.Fx.Disc(disc)).Get?.Follow(floor);

        if (delay >= 0.3f)
        {
            VfxBurstSpec lift = VfxBurstSpec.At(Vector3.Zero, cast.Colors, density * Mathf.Clamp(radius * 0.3f, 0.5f, 1.6f));
            lift.Continuous = true;
            lift.SpeedScale = 0.6f;
            lift.Direction = Vector3.Up;
            lift.Spread = 20f;
            lift.Extents = new Vector3(radius * 0.7f, 0.1f, radius * 0.7f);
            rig.Stream = rig.Add(cast.Fx.Burst(VfxParticles.Embers, lift));
            rig.Stream.Get?.Follow(VfxAnchor.To(ground, Vector3.Up * 0.3f));
        }

        if (delay < 0.15f)
        {
            return true;
        }

        // In at a slant from high up, slow at first and fast at the end, down as the delay runs out.
        float height = Mathf.Min(36f, 16f + (delay * 10f));
        float slant = height * 0.4f;
        float turn = Roll() * Mathf.Tau;
        var sky = new Vector3(Mathf.Cos(turn) * slant, height, Mathf.Sin(turn) * slant);
        VfxAnchor anchor = VfxAnchor.To(ground, Vector3.Up * 0.3f);
        float size = Mathf.Clamp(0.7f + (radius * 0.2f), 0.7f, 1.8f);

        VfxFlareSpec head = VfxFlareSpec.At(Vector3.Zero, size * 0.7f, cast.Colors);
        head.Sustain = true;
        head.Light = budget.MaxLights > 0;
        head.LightRange = Mathf.Max(10f, radius * 3.5f);
        rig.Flare = rig.Add(cast.Fx.Flare(head));
        if (rig.Flare.Get is { } flare)
        {
            flare.Follow(anchor);
            flare.SettleFrom(sky, delay, accelerate: true);
        }

        if (rich)
        {
            VfxShellSpec rock = VfxShellSpec.Sphere(Vector3.Zero, size, Flame(cast.Colors));
            rock.Sustain = true;
            rock.Layered = true;
            rock.Scroll = new Vector2(0.3f, 1.8f);
            VfxHandle<VfxShell> body = rig.Add(cast.Fx.Shell(rock));
            if (body.Get is { } shell)
            {
                shell.Follow(anchor);
                shell.SettleFrom(sky, delay, accelerate: true);
            }
        }

        VfxHandle<VfxBolt> tail = rig.Add(cast.Fx.Bolt(new VfxBoltSpec
        {
            Mode = VfxBoltMode.Trail,
            Colors = cast.Colors,
            Width = size * (rich ? 0.75f : 0.5f),
            TrailSeconds = Mathf.Max(0.3f, VfxRecipeRules.TrailSeconds(budget.Trail) * 2.6f),
        }));
        if (tail.Get is { } ribbon)
        {
            ribbon.FollowTo(anchor);
            ribbon.SettleTrailFrom(sky, delay, accelerate: true);
        }

        VfxBurstSpec shed = VfxBurstSpec.At(Vector3.Zero, cast.Colors, density * 1.6f);
        shed.Continuous = true;
        shed.Extents = Vector3.One * (size * 0.45f);
        shed.SpeedScale = 0.5f;
        shed.SizeScale = 1.3f;
        ShedFalling(rig, cast, VfxParticles.Embers, shed, anchor, sky, delay);
        if (rich)
        {
            shed.Density = density * budget.DebrisMultiplier;
            shed.SizeScale = Mathf.Clamp(size * 1.3f, 0.8f, 2.2f);
            shed.SpeedScale = 0.4f;
            ShedFalling(rig, cast, VfxParticles.Smoke, shed, anchor, sky, delay);
        }

        if (VfxQuality.Tier == VfxTier.Ultra)
        {
            shed.Density = density * 0.6f;
            shed.SizeScale = 1f;
            shed.SpeedScale = 0.5f;
            ShedFalling(rig, cast, VfxParticles.Sparks, shed, anchor, sky, delay);
        }

        return true;
    }

    /// <summary>
    /// Sunfall lands. Performance: a core that is gone in a blink, one shock ring and a fountain of
    /// embers. Medium adds the billow of fire that erodes into smoke, glowing debris and the
    /// scorch. High adds the pressure wave, a rising cap of fire over the billow and a second
    /// smoke column; Ultra a ring of embers kicked up at the blast's edge and a spray of sparks.
    /// </summary>
    private static bool SunfallBlast(in VfxCast cast, Vector3 position, float radius, SpellBurstSource source, float charge)
    {
        radius = Mathf.Max(1f, radius);
        VfxBudget budget = VfxQuality.Budget;
        VfxSpawner fx = cast.Fx;
        bool rich = budget.SecondaryDebris;
        bool lavish = VfxQuality.Tier >= VfxTier.High;
        bool ultra = VfxQuality.Tier == VfxTier.Ultra;
        float density = budget.ParticleMultiplier;
        float full = Mathf.Clamp(charge, 0f, 1f);
        float floorY = source == SpellBurstSource.Ground ? position.Y - 0.3f : position.Y - 1f;
        var floor = new Vector3(position.X, floorY, position.Z);
        Vector3 heart = floor + (Vector3.Up * Mathf.Min(1.2f, radius * 0.3f));
        bool hitsPlayer = !cast.ByPlayer && PlayerWithin(position, radius + 0.5f);

        BlastCore(cast, heart, radius * 0.26f, radius * 3.4f);
        ThinRing(cast, floor + (Vector3.Up * 0.12f), radius * 1.15f, 0.34f, energy: 1.6f);

        // The fountain: straight up and hot, on every tier.
        VfxBurstSpec fountain = VfxBurstSpec.At(heart, cast.Colors, density * (1.7f + (0.3f * full)));
        fountain.Extents = new Vector3(radius * 0.25f, 0.2f, radius * 0.25f);
        fountain.Direction = Vector3.Up;
        fountain.Spread = 50f;
        fountain.SpeedScale = 2.4f;
        fountain.SizeScale = 1.4f;
        fx.Burst(VfxParticles.Embers, fountain);

        VfxBurstSpec spray = VfxBurstSpec.At(floor + (Vector3.Up * 0.3f), cast.Colors, density * 1.4f);
        spray.Extents = new Vector3(radius * 0.2f, 0.1f, radius * 0.2f);
        spray.Direction = Vector3.Up;
        spray.Spread = 84f;
        spray.SpeedScale = 1.5f;
        fx.Burst(VfxParticles.Sparks, spray);

        if (!rich)
        {
            return true;
        }

        // The billow: swells from a quarter of its size, and burns away from the inside into smoke.
        float ball = Mathf.Min(2.4f, radius * 0.5f);
        VfxSchoolColors flame = Flame(cast.Colors);
        VfxShellSpec billow = VfxShellSpec.Sphere(heart, ball, flame);
        billow.Life = 0.62f;
        billow.BurnsAway = true;
        billow.StartScale = 0.25f;
        billow.Layered = true;
        billow.Scroll = new Vector2(0.12f, 0.75f);
        billow.Opacity = 0.85f;
        fx.Shell(billow);

        // An afterglow where the core was: dim, and it only has to outlast the billow.
        VfxFlareSpec glow = VfxFlareSpec.At(heart, Mathf.Min(0.9f, radius * 0.22f), cast.Colors.Scaled(0.4f, 0.5f));
        glow.Life = 0.55f;
        fx.Flare(glow);

        VfxBurstSpec debris = VfxBurstSpec.At(floor + (Vector3.Up * 0.4f), cast.Colors, density * 1.2f);
        debris.Tint = DebrisTint;
        debris.Extents = new Vector3(radius * 0.3f, 0.15f, radius * 0.3f);
        debris.Direction = Vector3.Up;
        debris.Spread = 62f;
        debris.SpeedScale = 1.5f;
        debris.SizeScale = 1.2f;
        fx.Burst(VfxParticles.Shards, debris);

        VfxBurstSpec smoke = VfxBurstSpec.At(heart, cast.Colors, density * budget.DebrisMultiplier * 1.3f);
        smoke.Extents = new Vector3(radius * 0.35f, 0.4f, radius * 0.35f);
        smoke.Direction = Vector3.Up;
        smoke.Spread = 40f;
        smoke.SpeedScale = 1.3f;
        smoke.SizeScale = Mathf.Clamp(radius * 0.5f, 0.8f, 2.4f);
        smoke.LifeScale = 1.35f;
        fx.Burst(VfxParticles.Smoke, smoke);

        fx.Mark(new VfxGroundMarkSpec
        {
            Mark = VfxMark.Scorch,
            Position = floor,
            Size = radius * 2.1f,
            Colors = cast.Colors,
            Life = 12f,
            Reach = FloorReach(radius),
        });

        // The one blast with a screen flash: near white, faint, still under every comfort cap, and
        // only for a blast the camera is all but inside. One seen from across the field lights
        // the field, not the lens.
        float away = _director is { HasCamera: true } director ? director.DistanceToCamera(heart) : 0f;
        float close = 1f - Mathf.Clamp((away - radius) / (radius * 1.5f), 0f, 1f);
        if (close > 0.05f)
        {
            fx.Screen(BlastWhite, (0.22f + (0.08f * full)) * close, hitsPlayer);
        }

        if (!lavish)
        {
            return true;
        }

        fx.Distortion(new VfxDistortionSpec
        {
            Position = heart,
            Radius = radius * 1.3f,
            Life = 0.45f,
            Strength = 0.045f,
        });

        // The cap: a second, smaller ball that climbs out of the first as it dies.
        VfxShellSpec cap = VfxShellSpec.Sphere(heart + (Vector3.Up * (ball * 0.9f)), ball * 0.6f, flame.Scaled(0.8f, 1f));
        cap.Life = 0.9f;
        cap.BurnsAway = true;
        cap.StartScale = 0.35f;
        cap.Layered = true;
        cap.Scroll = new Vector2(0.08f, 1.1f);
        cap.Opacity = 0.7f;
        fx.Shell(cap);

        smoke.Position = heart + (Vector3.Up * ball);
        smoke.Density = density * budget.DebrisMultiplier * 0.8f;
        smoke.Extents = new Vector3(radius * 0.2f, 0.5f, radius * 0.2f);
        smoke.Spread = 22f;
        smoke.SpeedScale = 1.8f;
        smoke.LifeScale = 1.6f;
        fx.Burst(VfxParticles.Smoke, smoke);

        if (ultra)
        {
            VfxBurstSpec kicked = VfxBurstSpec.At(floor + (Vector3.Up * 0.2f), cast.Colors, density);
            kicked.Extents = new Vector3(radius * 0.8f, 0.05f, radius * 0.8f);
            kicked.Direction = Vector3.Up;
            kicked.Spread = 25f;
            kicked.SpeedScale = 0.9f;
            fx.Burst(VfxParticles.Embers, kicked);

            fountain.Density = density;
            fountain.SpeedScale = 3.2f;
            fountain.Spread = 30f;
            fx.Burst(VfxParticles.Sparks, fountain);
        }

        return true;
    }

    // --- frost -----------------------------------------------------------------------------------

    /// <summary>Extra splinters off a Rime Shard that landed, a breath of cold mist under it, and
    /// frost on the floor at the feet of what it struck.</summary>
    private static bool RimeShardHit(in VfxCast cast, in SpellImpactInfo hit)
    {
        VfxBudget budget = VfxQuality.Budget;
        if (hit.Kind is SpellImpactKind.Blocked or SpellImpactKind.Expired || !budget.SecondaryDebris)
        {
            return false;
        }

        // Thrown back the way the shard came: it shatters against what it struck.
        VfxBurstSpec splinters = VfxBurstSpec.At(hit.Position, cast.Colors, budget.ParticleMultiplier * 0.8f);
        splinters.Direction = hit.Normal.LengthSquared() > 0.0001f ? hit.Normal : Vector3.Up;
        splinters.Spread = 55f;
        splinters.SpeedScale = 1.2f;
        splinters.SizeScale = 0.8f;
        cast.Fx.Burst(VfxParticles.Shards, splinters);

        VfxBurstSpec mist = VfxBurstSpec.At(
            hit.Position + (Vector3.Down * 0.3f), cast.Colors, budget.ParticleMultiplier * budget.DebrisMultiplier * 0.3f);
        mist.Tint = MistTint;
        mist.SizeScale = 0.5f;
        mist.SpeedScale = 0.5f;
        mist.GravityScale = -0.3f;
        mist.LifeScale = 0.7f;
        cast.Fx.Burst(VfxParticles.Smoke, mist);

        // At the feet of a body, or where the shard struck the world; never hung in the air.
        Node3D? struck = VfxAnchor.BodyOf(hit.Target);
        if (struck != null || hit.Kind == SpellImpactKind.World)
        {
            cast.Fx.Mark(new VfxGroundMarkSpec
            {
                Mark = VfxMark.Frost,
                Position = struck?.GlobalPosition ?? hit.Position,
                Size = 1.7f,
                Colors = cast.Colors,
                Life = 7f,
                Reach = 0.5f,
            });
        }

        return false;
    }

    /// <summary>
    /// Frost Nova: ice thrown out along the ground from the caster. Nothing here is a sphere or a
    /// large flare, because the camera is at its centre: a snap of light at the chest, one ring
    /// racing out along the floor, shards kicked up low and (Medium and up) a crown of crystals
    /// standing up at the nova's edge, mist rolling out over the floor and frost left under it.
    /// </summary>
    private static bool FrostNovaBlast(in VfxCast cast, Vector3 position, float radius, SpellBurstSource source, float charge)
    {
        radius = Mathf.Max(1f, radius);
        VfxBudget budget = VfxQuality.Budget;
        VfxSpawner fx = cast.Fx;
        bool rich = budget.SecondaryDebris;
        float density = budget.ParticleMultiplier;
        float floorY = source == SpellBurstSource.Caster
            ? VfxAnchor.BodyOf(cast.Caster)?.GlobalPosition.Y ?? position.Y - 1f
            : position.Y - 0.3f;
        var floor = new Vector3(position.X, floorY, position.Z);
        Vector3 low = floor + (Vector3.Up * 0.25f);

        BlastCore(cast, position, 0.42f, radius * 2.4f);
        ThinRing(cast, floor + (Vector3.Up * 0.12f), radius, 0.3f);

        // Kicked out low and fast: a skirt of ice, not a fountain.
        VfxBurstSpec skirt = VfxBurstSpec.At(low, cast.Colors, density * 1.8f);
        skirt.Extents = new Vector3(radius * 0.3f, 0.1f, radius * 0.3f);
        skirt.Direction = Vector3.Up;
        skirt.Spread = 82f;
        skirt.SpeedScale = 1.5f;
        fx.Burst(VfxParticles.Shards, skirt);

        VfxBurstSpec glints = VfxBurstSpec.At(low + (Vector3.Up * 0.5f), cast.Colors, density * 1.2f);
        glints.Extents = new Vector3(radius * 0.6f, 0.5f, radius * 0.6f);
        glints.SpeedScale = 0.8f;
        fx.Burst(VfxParticles.Motes, glints);

        if (!rich)
        {
            return true;
        }

        // The crown: big slow shards born across the outer half and thrown straight up.
        VfxBurstSpec crown = VfxBurstSpec.At(low, cast.Colors, density * 1.3f);
        crown.Extents = new Vector3(radius * 0.75f, 0.05f, radius * 0.75f);
        crown.Direction = Vector3.Up;
        crown.Spread = 16f;
        crown.SpeedScale = 0.7f;
        crown.SizeScale = 1.7f;
        crown.LifeScale = 1.2f;
        fx.Burst(VfxParticles.Shards, crown);

        // Mist that stays on the floor: pale smoke with its buoyancy all but taken away.
        // Small and thin: the caster and the camera are standing in it.
        VfxBurstSpec mist = VfxBurstSpec.At(low, cast.Colors, density * 0.9f);
        mist.Tint = MistTint;
        mist.Extents = new Vector3(radius * 0.6f, 0.08f, radius * 0.6f);
        mist.GravityScale = 0.05f;
        mist.SpeedScale = 1.4f;
        mist.SizeScale = Mathf.Clamp(radius * 0.17f, 0.5f, 0.8f);
        mist.LifeScale = 1.2f;
        fx.Burst(VfxParticles.Smoke, mist);

        fx.Mark(new VfxGroundMarkSpec
        {
            Mark = VfxMark.Frost,
            Position = floor,
            Size = radius * 2.1f,
            Colors = cast.Colors,
            Life = 5f,
            Reach = FloorReach(radius),
        });

        if (VfxQuality.Tier >= VfxTier.High)
        {
            fx.Distortion(new VfxDistortionSpec
            {
                Position = low,
                Radius = radius * 0.9f,
                Life = 0.35f,
                Strength = 0.022f,
            });

            // A second, slower skirt behind the first.
            skirt.Density = density * 1.2f;
            skirt.SpeedScale = 0.9f;
            skirt.SizeScale = 1.3f;
            fx.Burst(VfxParticles.Shards, skirt);
        }

        return true;
    }

    /// <summary>
    /// Blizzard's zone, drawn to be stood in. The generic zone fills its floor with a bright disc,
    /// which from inside is the whole frame; here the floor is only a faint rim at the edge of the
    /// danger, and the storm is what falls through it: snow coming down across the whole radius,
    /// (Medium and up) hail in it, mist lying on the floor and frost under that, and a cold light.
    /// </summary>
    private static bool BlizzardZone(in VfxCast cast, VfxRig rig, Node3D zone, float radius, float duration)
    {
        radius = Mathf.Max(0.5f, radius);
        VfxBudget budget = VfxQuality.Budget;
        bool rich = budget.SecondaryDebris;
        float density = budget.ParticleMultiplier;
        float area = Mathf.Clamp(radius * 0.35f, 0.6f, 2f);
        VfxAnchor floor = VfxAnchor.To(zone);

        VfxDiscSpec edge = VfxDiscSpec.At(Vector3.Zero, radius, cast.Colors);
        edge.Sustain = true;
        edge.Spin = 0.2f;
        edge.Flow = 0.25f;
        edge.Body = 0.08f;
        edge.Rim = 0.5f;
        edge.Energy = 0.6f;
        rig.Add(cast.Fx.Disc(edge)).Get?.Follow(floor);

        // Snow: small, slow, wandering, and falling the height of a room. One emitter holds only so
        // many flakes, so the richer tiers stack layers of it born at different heights.
        int layers = VfxQuality.Tier switch
        {
            VfxTier.Medium => 2,
            VfxTier.High => 3,
            VfxTier.Ultra => 4,
            _ => 1,
        };
        VfxBurstSpec snow = VfxBurstSpec.At(Vector3.Zero, cast.Colors, density * area * 3f);
        snow.Continuous = true;
        snow.Extents = new Vector3(radius * 0.85f, 0.3f, radius * 0.85f);
        snow.Direction = Vector3.Down;
        snow.Spread = 25f;
        snow.Speed = 2.6f;
        snow.GravityScale = -3f;
        snow.SizeScale = 1.8f;
        for (int i = 0; i < layers; i++)
        {
            VfxHandle<VfxBurst> falling = rig.Add(cast.Fx.Burst(VfxParticles.Motes, snow));
            falling.Get?.Follow(VfxAnchor.To(zone, Vector3.Up * (3.2f - (0.5f * i))));
            if (i == 0)
            {
                rig.Stream = falling;
            }
        }

        if (budget.MaxLights > 0)
        {
            VfxFlareSpec cold = VfxFlareSpec.At(Vector3.Zero, 0.4f, cast.Colors);
            cold.Sustain = true;
            cold.NoCore = true;
            cold.Level = 0.55f;
            cold.Light = true;
            cold.LightRange = Mathf.Max(5f, radius * 1.8f);
            rig.Flare = rig.Add(cast.Fx.Flare(cold));
            rig.Flare.Get?.Follow(VfxAnchor.To(zone, Vector3.Up * 2f));
        }

        if (!rich)
        {
            return true;
        }

        VfxBurstSpec hail = VfxBurstSpec.At(Vector3.Zero, cast.Colors, density * area * 0.5f);
        hail.Continuous = true;
        hail.Extents = new Vector3(radius * 0.8f, 0.3f, radius * 0.8f);
        hail.Direction = Vector3.Down;
        hail.Spread = 14f;
        hail.SpeedScale = 0.7f;
        hail.SizeScale = 0.6f;
        rig.Add(cast.Fx.Burst(VfxParticles.Shards, hail)).Get?.Follow(VfxAnchor.To(zone, Vector3.Up * 3.4f));

        // Six seconds of it around the caster: small puffs, and few of them, or it is a bank of fog
        // between the camera and the fight.
        VfxBurstSpec mist = VfxBurstSpec.At(Vector3.Zero, cast.Colors, density * area * 0.3f);
        mist.Continuous = true;
        mist.Tint = MistTint;
        mist.Extents = new Vector3(radius * 0.7f, 0.08f, radius * 0.7f);
        mist.GravityScale = 0.03f;
        mist.SpeedScale = 0.5f;
        mist.SizeScale = Mathf.Clamp(radius * 0.15f, 0.5f, 0.85f);
        rig.Add(cast.Fx.Burst(VfxParticles.Smoke, mist)).Get?.Follow(VfxAnchor.To(zone, Vector3.Up * 0.25f));

        VfxHandle<VfxGroundMark> frost = rig.Add(cast.Fx.Mark(new VfxGroundMarkSpec
        {
            Mark = VfxMark.Frost,
            Size = radius * 2f,
            Colors = cast.Colors,
            Sustain = true,
            Reach = FloorReach(radius),
        }));
        frost.Get?.Follow(floor);
        return true;
    }

    /// <summary>One pulse of the blizzard: a gust. A thin ring runs out to the edge along the floor
    /// and a squall of hail comes down with it. No flash: the caster is standing in it.</summary>
    private static bool BlizzardPulse(in VfxCast cast, Vector3 position, float radius, SpellBurstSource source, float charge)
    {
        if (source != SpellBurstSource.Zone)
        {
            return false;
        }

        radius = Mathf.Max(0.5f, radius);
        VfxBudget budget = VfxQuality.Budget;
        ThinRing(cast, position + (Vector3.Up * 0.05f), radius, 0.5f, energy: 0.7f);

        VfxBurstSpec squall = VfxBurstSpec.At(position + (Vector3.Up * 2.6f), cast.Colors, budget.ParticleMultiplier * 0.9f);
        squall.Extents = new Vector3(radius * 0.7f, 0.3f, radius * 0.7f);
        squall.Direction = Vector3.Down;
        squall.Spread = 30f;
        squall.SpeedScale = 0.9f;
        cast.Fx.Burst(VfxParticles.Shards, squall);

        if (VfxQuality.Tier >= VfxTier.High)
        {
            // Where the hail lands: a few glints across the floor.
            VfxBurstSpec strike = VfxBurstSpec.At(position + (Vector3.Up * 0.2f), cast.Colors, budget.ParticleMultiplier * 0.5f);
            strike.Extents = new Vector3(radius * 0.7f, 0.05f, radius * 0.7f);
            strike.Direction = Vector3.Up;
            strike.Spread = 60f;
            strike.SpeedScale = 0.4f;
            strike.Tint = cast.Colors.Core;
            cast.Fx.Burst(VfxParticles.Sparks, strike);
        }

        return true;
    }

    /// <summary>
    /// The Glacial Bulwark as ice. One flat sheet is a white rectangle; this is a slab of blue with
    /// a ragged top, and in front of and behind it a row of narrow spires of uneven height that fade
    /// at their sides and break up toward their tips, so the wall has a crystalline silhouette and
    /// its facets overlap. Glints drift off it, shards burst as it rises, and (Medium and up) cold
    /// mist lies along its foot over a line of frost.
    /// </summary>
    private static bool GlacialBulwarkWall(in VfxCast cast, VfxRig rig, Node3D barrier, float width, float height)
    {
        width = Mathf.Max(0.5f, width);
        height = Mathf.Max(0.5f, height);
        VfxBudget budget = VfxQuality.Budget;
        bool rich = budget.SecondaryDebris;
        float density = budget.ParticleMultiplier;
        VfxSchoolColors colors = cast.Colors;

        // Ice is blue to its heart and mostly covers what is behind it. Drawn at the school's full
        // energy with its near-white core colour, overlapping sheets add up to a white rectangle.
        VfxSchoolColors deep = colors with { Core = colors.Mid.Lerp(colors.Core, 0.3f), Edge = colors.Edge.Darkened(0.35f) };
        VfxAnchor foot = VfxAnchor.To(barrier);

        VfxShellSpec slab = VfxShellSpec.Sheet(Vector3.Zero, width, height * 0.9f, deep);
        slab.Occlude = 0.7f;
        slab.FadeTop = 0.5f;
        slab.FadeSides = 0.3f;
        slab.Scroll = new Vector2(0.002f, 0.006f);
        slab.Tiling = new Vector2(Mathf.Max(1f, width * 0.55f), 1.3f);
        slab.Energy = 0.26f;
        slab.RiseSeconds = 0.35f;
        slab.Layered = rich;
        VfxHandle<VfxShell> wall = rig.Add(cast.Fx.Shell(slab));
        if (wall.Get is { } standing)
        {
            standing.Follow(foot);
            standing.OrientLike(barrier);
        }

        rig.Drew = wall.IsLive;
        if (!rig.Drew)
        {
            // Nothing stands: the barrier builds its plain face.
            return true;
        }

        // A wall that had a telegraph already stands where it will, so its own axes can be read. One
        // raised with no delay is placed after this call: it gets the slab alone.
        bool placed = (cast.Spell?.GroundDelay ?? 0f) > 0f && barrier.IsInsideTree();
        if (placed)
        {
            Basis axes = barrier.GlobalBasis.Orthonormalized();
            int spires = VfxQuality.Tier switch
            {
                VfxTier.Performance => 2,
                VfxTier.Low => 3,
                VfxTier.Medium => 4,
                VfxTier.High => 5,
                _ => 6,
            };
            VfxSchoolColors facet = colors with { Core = colors.Mid.Lerp(colors.Core, 0.5f) };
            for (int i = 0; i < spires; i++)
            {
                float across = ((i + 0.25f + (0.5f * Roll())) / spires) - 0.5f;
                float middle = 1f - Mathf.Abs(across * 2f);
                float tall = height * (0.8f + (0.25f * middle) + (0.2f * Roll()));
                VfxShellSpec spire = VfxShellSpec.Sheet(Vector3.Zero, width / spires * 1.7f, tall, facet);
                spire.Occlude = 0.75f;
                spire.FadeTop = 1f;
                spire.FadeSides = 1f;
                spire.Scroll = new Vector2(0.003f, 0.01f);
                spire.Tiling = new Vector2(0.5f + (0.4f * Roll()), 0.7f + (0.5f * Roll()));
                spire.Energy = 0.22f;
                spire.RiseSeconds = 0.2f + (0.3f * Roll());
                spire.Layered = false;
                VfxHandle<VfxShell> raised = rig.Add(cast.Fx.Shell(spire));
                if (raised.Get is { } shard)
                {
                    // Alternately a hand in front of and behind the slab, so the facets overlap.
                    Vector3 offset = (axes.X * (across * width * 0.9f)) + (axes.Z * ((i % 2 == 0 ? 1f : -1f) * 0.2f));
                    shard.Follow(VfxAnchor.To(barrier, offset));
                    shard.OrientLike(barrier);
                }
            }
        }

        // Glints drifting off the face.
        VfxBurstSpec glints = VfxBurstSpec.At(Vector3.Zero, colors, density * Mathf.Clamp(width * 0.3f, 0.5f, 2f) * 0.6f);
        glints.Continuous = true;
        glints.Extents = new Vector3(width * 0.5f, height * 0.45f, 0.25f);
        glints.SpeedScale = 0.3f;
        VfxHandle<VfxBurst> drifting = rig.Add(cast.Fx.Burst(VfxParticles.Motes, glints));
        if (drifting.Get is { } emitter)
        {
            emitter.Follow(VfxAnchor.To(barrier, Vector3.Up * (height * 0.5f)));
            emitter.OrientLike(barrier);
        }

        // The burst as it rises.
        VfxBurstSpec rise = VfxBurstSpec.At(Vector3.Zero, colors, density * Mathf.Clamp(width * 0.4f, 0.6f, 2f));
        rise.Extents = new Vector3(width * 0.5f, 0.2f, 0.2f);
        rise.Direction = Vector3.Up;
        rise.Spread = 30f;
        rise.SpeedScale = 1.4f;
        VfxHandle<VfxBurst> up = rig.Add(cast.Fx.Burst(VfxParticles.Shards, rise));
        if (up.Get is { } burst)
        {
            burst.Follow(VfxAnchor.To(barrier, Vector3.Up * 0.2f));
            burst.OrientLike(barrier);
        }

        if (budget.MaxLights > 0)
        {
            VfxFlareSpec glow = VfxFlareSpec.At(Vector3.Zero, 0.5f, colors);
            glow.Sustain = true;
            glow.NoCore = true;
            glow.Level = 0.45f;
            glow.Light = true;
            glow.LightRange = Mathf.Max(4f, width * 1.4f);
            rig.Flare = rig.Add(cast.Fx.Flare(glow));
            rig.Flare.Get?.Follow(VfxAnchor.To(barrier, Vector3.Up * (height * 0.5f)));
        }

        if (!rich)
        {
            return true;
        }

        VfxBurstSpec mist = VfxBurstSpec.At(Vector3.Zero, colors, density * budget.DebrisMultiplier * 0.3f);
        mist.Continuous = true;
        mist.Tint = MistTint;
        mist.Extents = new Vector3(width * 0.5f, 0.08f, 0.4f);
        mist.GravityScale = 0.03f;
        mist.SpeedScale = 0.35f;
        mist.SizeScale = 0.6f;
        VfxHandle<VfxBurst> lying = rig.Add(cast.Fx.Burst(VfxParticles.Smoke, mist));
        if (lying.Get is { } cold)
        {
            cold.Follow(VfxAnchor.To(barrier, Vector3.Up * 0.2f));
            cold.OrientLike(barrier);
        }

        VfxHandle<VfxGroundMark> mark = rig.Add(cast.Fx.Mark(new VfxGroundMarkSpec
        {
            Mark = VfxMark.Frost,
            Size = width * 1.25f,
            Depth = Mathf.Clamp(1.8f / width, 0.15f, 1f),
            Colors = colors,
            Life = Mathf.Max(6f, (cast.Spell?.BarrierDuration ?? 0f) + 4f),
            Reach = 0.6f,
        }));
        if (mark.Get is { } frost)
        {
            frost.Follow(foot);
            frost.OrientLike(barrier);
        }

        return true;
    }

    // --- lightning -------------------------------------------------------------------------------

    /// <summary>A chained or branded arc gets a second, thinner, wilder strand beside the generic
    /// one (Medium and up), so an arc is a bundle of lightning and not one line.</summary>
    private static bool LightningArc(in VfxCast cast, Vector3 from, Vector3 to, SpellArcKind kind)
    {
        if (kind is SpellArcKind.Chain or SpellArcKind.Brand && VfxQuality.Budget.SecondaryDebris)
        {
            Fork(cast, from, to, 0.022f, 0.14f, jitter: 0.15f);
        }

        return false;
    }

    /// <summary>Ball Lightning grounds itself as it flies: thin arcs from the orb to the floor around
    /// it, re-shaped fifteen times a second (one on Medium, two on High, three on Ultra).</summary>
    private static bool BallLightningOrb(
        in VfxCast cast, VfxRig rig, Node3D projectile, Vector3 handOffset, Vector3 direction, float charge)
    {
        int arcs = VfxQuality.Tier switch
        {
            VfxTier.Medium => 1,
            VfxTier.High => 2,
            VfxTier.Ultra => 3,
            _ => 0,
        };
        Vector3 at = projectile.GlobalPosition;
        float turn = Roll() * Mathf.Tau;
        for (int i = 0; i < arcs; i++)
        {
            float angle = turn + (i * Mathf.Tau / arcs);
            float reach = 0.5f + (0.5f * Roll());
            var offset = new Vector3(Mathf.Cos(angle) * reach, -1.25f, Mathf.Sin(angle) * reach);
            VfxHandle<VfxBolt> arc = rig.Add(cast.Fx.Bolt(new VfxBoltSpec
            {
                Mode = VfxBoltMode.Beam,
                From = at,
                To = at + offset,
                Colors = cast.Colors.Scaled(0.8f, 1f),
                Width = 0.016f,
                Jitter = 0.18f,
                Segments = 6,
                Seed = _director!.NextSeed(),
            }));
            if (arc.Get is { } bolt)
            {
                bolt.FollowFrom(VfxAnchor.To(projectile));
                bolt.FollowTo(VfxAnchor.To(projectile, offset));
            }
        }

        return false;
    }

    /// <summary>Where Ball Lightning bursts it forks: short strikes out of the burst into the ground
    /// around it (Medium and up), over the generic flash, ring and sparks.</summary>
    private static bool BallLightningHit(in VfxCast cast, in SpellImpactInfo hit)
    {
        if (hit.Kind is SpellImpactKind.Blocked or SpellImpactKind.Expired || !VfxQuality.Budget.SecondaryDebris)
        {
            return false;
        }

        GroundForks(cast, hit.Position, VfxQuality.Tier == VfxTier.Ultra ? 5 : 3, 1.7f, 1.1f, 0.024f);
        return false;
    }

    /// <summary>
    /// Storm Conduit's beam: a thin, hard-kinked line in place of the generic one, which is drawn
    /// more than twice as wide and reads as a band of yellow (and, starting at the hand half a metre
    /// from the eye, as a wedge across the frame in first person). The strands that make it
    /// lightning are thrown with every tick (<see cref="StormConduitTick"/>).
    /// </summary>
    private static bool StormConduitBeam(in VfxCast cast, VfxRig rig, VfxAnchor hand, Vector3 from, Vector3 to)
    {
        if (cast.Spell is not { } spell)
        {
            return false;
        }

        float hold = (Mathf.Max(0.05f, spell.ChannelTickInterval) * 1.8f) + 0.08f;
        rig.Bolt = rig.Add(cast.Fx.Bolt(new VfxBoltSpec
        {
            Mode = VfxBoltMode.Beam,
            From = from,
            To = to,
            Colors = cast.Colors,
            Width = AtTheEye(from) ? 0.016f : 0.028f,
            Jitter = Kink(0.075f, from.DistanceTo(to)),
            Segments = VfxRecipeRules.BoltSegments(VfxQuality.Budget, from.DistanceTo(to)),
            Seed = _director!.NextSeed(),
        }));
        if (rig.Bolt.Get is { } bolt)
        {
            bolt.FollowFrom(hand);
            bolt.Refresh(from, to, hold);
        }

        return true;
    }

    /// <summary>
    /// One tick of Storm Conduit. Each tick throws a fresh strike down the beam: hair-thin, wild,
    /// forked on the richer tiers, and gone before the next (none on Performance and Low, one on
    /// Medium, two on High and Ultra). With the steady line under them this is what makes the beam
    /// flicker and branch like lightning. In first person the strands start a little way down the
    /// beam, clear of the eye.
    /// </summary>
    private static bool StormConduitTick(
        in VfxCast cast, VfxRig rig, Node3D projectile, Vector3 handOffset, Vector3 direction, float charge)
    {
        // Five new shapes a second is a flicker, which is the point of it and exactly what Reduced
        // Motion asks not to be shown: there the steady line is the whole beam.
        VfxBudget budget = VfxQuality.Budget;
        if (!budget.SecondaryDebris || VfxQuality.ReducedMotion || cast.Spell is not { } spell ||
            direction.LengthSquared() < 0.0001f)
        {
            return false;
        }

        Vector3 aim = direction.Normalized();
        Vector3 origin = projectile.GlobalPosition;
        Vector3 from = origin + handOffset;
        float reach = Mathf.Max(1f, spell.Range);

        // Where the beam last struck something, while that is still true.
        float hold = (Mathf.Max(0.05f, spell.ChannelTickInterval) * 1.8f) + 0.08f;
        if (cast.Caster is { } caster && Beams.TryGetValue(caster.RuntimeId, out VfxRig? beam) &&
            _director!.Now - beam.ClipAt <= hold)
        {
            reach = Mathf.Clamp(beam.ClipDistance, 0.5f, reach);
        }

        // The beam runs from the hand to a point on the true aim (which starts at the aim origin,
        // not at the hand), so the strands do too. Drawn from the hand along the aim they would end
        // as far wide of the beam's end as the hand is from the aim origin.
        Vector3 to = origin + (aim * reach);
        if (AtTheEye(from))
        {
            from += (to - from).Normalized() * Mathf.Min(0.9f, reach * 0.3f);
        }

        int strands = VfxQuality.Tier >= VfxTier.High ? 2 : 1;
        float life = Mathf.Clamp(spell.ChannelTickInterval, 0.1f, 0.25f);
        for (int i = 0; i < strands; i++)
        {
            Fork(
                cast, from, to, i == 0 ? 0.016f : 0.011f, life, i == 0 ? Mathf.Max(0, budget.BoltBranches - 1) : 0,
                jitter: i == 0 ? 0.05f : 0.085f);
        }

        return false;
    }

    /// <summary>Where Storm Conduit's beam lands it spits: short forks off the struck point (Medium
    /// and up) over the generic flicker and sparks.</summary>
    private static bool StormConduitHit(in VfxCast cast, in SpellImpactInfo hit)
    {
        if (hit.Kind != SpellImpactKind.Target || !VfxQuality.Budget.SecondaryDebris)
        {
            return false;
        }

        GroundForks(cast, hit.Position, VfxQuality.Tier >= VfxTier.High ? 3 : 2, 0.9f, 0.4f, 0.012f);
        return false;
    }

    /// <summary>Stormbrand calls a strike down on what it brands: one bolt out of the sky onto the
    /// struck point, forked on the richer tiers, over the generic flash and the brand's sigil.</summary>
    private static bool StormbrandHit(in VfxCast cast, in SpellImpactInfo hit)
    {
        if (hit.Kind != SpellImpactKind.Target)
        {
            return false;
        }

        float lean = (Roll() - 0.5f) * 3f;
        Vector3 sky = hit.Position + new Vector3(lean, 8f, (Roll() - 0.5f) * 3f);
        Fork(cast, sky, hit.Position, 0.04f, 0.22f, VfxQuality.Budget.BoltBranches, jitter: 0.1f);
        if (VfxQuality.Budget.SecondaryDebris)
        {
            Fork(cast, sky, hit.Position, 0.018f, 0.16f, jitter: 0.14f);
        }

        return false;
    }

    /// <summary>
    /// Thunder Step as a lightning strike along the ground. The generic dash draws one thick bolt,
    /// big afterimages and a wide ring around the caster; this is a bundle of thin strands down the
    /// line (one on Performance and Low, two on Medium, three above), small afterimages, sparks
    /// thrown off the whole length, and at the far end a crack of light, one thin ring out to the
    /// reach of the hit, forks into the floor and a scorch.
    /// </summary>
    private static bool ThunderStepStrike(in VfxCast cast, Vector3 from, Vector3 to)
    {
        VfxBudget budget = VfxQuality.Budget;
        VfxSpawner fx = cast.Fx;
        bool rich = budget.SecondaryDebris;
        float density = budget.ParticleMultiplier;
        Vector3 start = from + Vector3.Up;
        Vector3 end = to + Vector3.Up;
        float length = start.DistanceTo(end);

        Fork(cast, start, end, 0.05f, 0.3f, budget.BoltBranches, jitter: 0.075f);
        int extra = VfxQuality.Tier switch
        {
            VfxTier.Medium => 1,
            VfxTier.High or VfxTier.Ultra => 2,
            _ => 0,
        };
        for (int i = 0; i < extra; i++)
        {
            Fork(cast, start, end, 0.022f, 0.2f + (0.06f * i), jitter: 0.12f);
        }

        // Afterimages: small, dim, each a little later than the last.
        int ghosts = fx.Full ? Mathf.Clamp(Mathf.CeilToInt(length / 3f), 1, 4) : 0;
        for (int i = 0; i < ghosts; i++)
        {
            float along = (i + 0.5f) / ghosts;
            VfxFlareSpec ghost = VfxFlareSpec.At(start.Lerp(end, along), 0.3f, cast.Colors.Scaled(0.5f, 0.5f));
            ghost.Life = 0.16f + (0.12f * along);
            fx.Flare(ghost);
        }

        Vector3 line = end - start;
        VfxBurstSpec wake = VfxBurstSpec.At((start + end) * 0.5f, cast.Colors, density * Mathf.Clamp(length * 0.25f, 0.6f, 2f));
        wake.Extents = new Vector3(
            Mathf.Max(0.2f, Mathf.Abs(line.X) * 0.5f), 0.4f, Mathf.Max(0.2f, Mathf.Abs(line.Z) * 0.5f));
        wake.SpeedScale = 0.7f;
        fx.Burst(VfxParticles.Sparks, wake);

        // Where it lands: the thunderclap.
        VfxFlareSpec depart = VfxFlareSpec.At(start, 0.3f, cast.Colors);
        depart.Life = 0.14f;
        fx.Flare(depart);
        BlastCore(cast, end, 0.42f, 6f);
        float reach = Mathf.Max(1f, cast.Spell?.DashHitRadius ?? 1.4f);
        ThinRing(cast, to + (Vector3.Up * 0.12f), reach, 0.28f, energy: 1.3f);

        VfxBurstSpec clap = VfxBurstSpec.At(to + (Vector3.Up * 0.3f), cast.Colors, density * 1.2f);
        clap.Direction = Vector3.Up;
        clap.Spread = 80f;
        clap.SpeedScale = 1.3f;
        fx.Burst(VfxParticles.Sparks, clap);

        if (!rich)
        {
            return true;
        }

        GroundForks(cast, end, VfxQuality.Tier == VfxTier.Ultra ? 5 : 3, reach * 1.2f, 0.95f, 0.026f);

        VfxBurstSpec smoke = VfxBurstSpec.At(to + (Vector3.Up * 0.3f), cast.Colors, density * budget.DebrisMultiplier * 0.4f);
        smoke.SizeScale = 0.7f;
        smoke.SpeedScale = 0.6f;
        fx.Burst(VfxParticles.Smoke, smoke);

        // A line of scorch along the floor it crossed, and a wider one where it stopped.
        if (budget.GroundMarks > 0)
        {
            int marks = Mathf.Clamp(Mathf.CeilToInt(length / 2.6f), 1, Mathf.Max(1, budget.GroundMarks / 3));
            for (int i = 0; i < marks; i++)
            {
                bool last = i == marks - 1;
                fx.Mark(new VfxGroundMarkSpec
                {
                    Mark = VfxMark.Scorch,
                    Position = last ? to : from.Lerp(to, (i + 0.5f) / marks),
                    Size = last ? reach * 1.6f : 1.2f,
                    Colors = cast.Colors,
                    Life = last ? 8f : 6f,
                    Reach = 0.6f,
                });
            }
        }

        if (VfxQuality.Tier >= VfxTier.High)
        {
            fx.Distortion(new VfxDistortionSpec { Position = end, Radius = reach * 1.2f, Life = 0.3f, Strength = 0.025f });
        }

        return true;
    }
}
