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
        emberlash.Impact = FireBoltHit;
        emberlash.Proc = KindleCatches;

        SpellVfxSpecial flameLance = table.For("spell.flame_lance");
        flameLance.Release = FireRelease;
        flameLance.Projectile = FlameLanceBolt;
        flameLance.Impact = FireBoltHit;
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
        thunderStep.Impact = ThunderStepHit;
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

    /// <summary>Whether a point is at the first-person casting point or nearer: the view-fixed hand
    /// (<see cref="VfxViewRules.HandOffset"/>) is a metre and a half from the eye, past <see cref="AtTheEye"/>.</summary>
    private static bool AtTheViewHand(Vector3 point) =>
        _director is { HasCamera: true } director &&
        director.DistanceToCamera(point) < VfxViewRules.HandOffset.Length() + 0.3f;

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

    /// <summary>The height of the floor a body stands on. Not its origin: a practice dummy's origin
    /// is the middle of its capsule, and a blast grounded there left its cracks, its scorch and the
    /// feet of its forks hanging a metre up, around the dummy's chest.</summary>
    private static float FloorUnder(Node3D body)
    {
        BodyFit(body, out Vector3 centre, out Vector3 fit);
        return body.GlobalPosition.Y + centre.Y - (fit.Y * 0.5f);
    }

    /// <summary>The school's colours for a body of fire: its heart is the hot body colour, not the
    /// near-white of a flare's core, so a billow is flame all the way through and not a white ball.</summary>
    private static VfxSchoolColors Flame(in VfxSchoolColors colors) => colors with
    {
        Core = colors.Core.Lerp(colors.Mid, 0.55f),
        CoreEnergy = colors.MidEnergy * 1.25f,
    };

    /// <summary>Forks thrown out from a point, biased down toward the floor: where lightning grounds.</summary>
    private static void GroundForks(
        in VfxCast cast, Vector3 from, int count, float reach, float drop, float width, int branches = 0)
    {
        float turn = Roll() * Mathf.Tau;
        for (int i = 0; i < count; i++)
        {
            float angle = turn + ((i + (Roll() * 0.6f)) * Mathf.Tau / count);
            float far = reach * (0.6f + (0.4f * Roll()));
            var to = new Vector3(from.X + (Mathf.Cos(angle) * far), from.Y - drop, from.Z + (Mathf.Sin(angle) * far));
            Fork(cast, from, to, width, 0.18f + (0.1f * Roll()), branches);
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
        snap.Life = AtTheViewHand(hand) ? 0.07f : 0.18f;
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
        // Half size for the player's own bolt in first person, as the generic head is: at full size
        // the body was a ball over the crosshair and whatever it was aimed at.
        float size = (0.2f + (0.16f * cast.Weight) + (0.2f * full)) * cast.Plan(VfxRole.Travel).Scale *
                     ProjectileViewScale(cast);
        Vector3 origin = projectile.GlobalPosition + handOffset;
        VfxAnchor anchor = VfxAnchor.To(projectile);
        bool aimed = direction.LengthSquared() > 0.0001f;
        Vector3 aim = aimed ? direction.Normalized() : Vector3.Forward;
        Vector3 velocity = aimed ? aim * Mathf.Max(0f, spell.ProjectileSpeed) : Vector3.Zero;

        VfxShellSpec body = VfxShellSpec.Sphere(origin, size * 0.8f, Flame(cast.Colors));
        body.Sustain = true;
        body.Layered = true;
        body.Scroll = new Vector2(0.3f, 1.7f);

        // Torn, not filled: the bolt's head is a shaped flame now, and a whole ball of noise drawn
        // over it was the round white disc again.
        body.Ragged = true;
        body.Opacity = 0.8f;
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
    /// Where a bolt of fire lands it explodes (<see cref="FireBlast"/>): the generic hit is a flash
    /// a metre across and a scatter of embers, which is a spark and not a fireball. The size follows
    /// the spell's weight and what a held cast reached, so a full Flame Lance is twice an Emberlash.
    /// One of a lance's pierced targets gets the blast too; a blocked or spent bolt keeps the
    /// generic gutter.
    /// </summary>
    private static bool FireBoltHit(in VfxCast cast, in SpellImpactInfo hit)
    {
        if (hit.Kind is SpellImpactKind.Blocked or SpellImpactKind.Expired)
        {
            return false;
        }

        float radius = (1.25f + (2.6f * cast.Weight) + (0.9f * Mathf.Clamp(hit.Charge, 0f, 1f))) *
                       (hit.Crit ? 1.12f : 1f) * (hit.Killed ? 1.1f : 1f);
        float groundY = float.NaN;
        if (VfxAnchor.BodyOf(hit.Target) is { } body)
        {
            // A hit above head height is on something tall, in the air (the blast checks).
            groundY = FloorUnder(body);
        }
        else if (hit.Normal.Y > 0.7f)
        {
            groundY = hit.Position.Y;
        }

        // On the first-person player the blast would be the whole frame: a spark, as any hit on them.
        if (hit.Target != null && IsPlayer(hit.Target) && AtTheEye(hit.Position + (Vector3.Up * 0.6f)))
        {
            return false;
        }

        FireBlast(cast, hit.Position, radius, groundY);
        return true;
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
            // The tongue itself: licks of flame climbing the bearer.
            VfxBurstSpec licks = VfxBurstSpec.At(position + (Vector3.Up * 0.4f), Flame(cast.Colors), budget.ParticleMultiplier * 0.6f);
            licks.Direction = Vector3.Up;
            licks.Spread = 12f;
            licks.Extents = new Vector3(0.28f, 0.5f, 0.28f);
            cast.Fx.Burst(VfxEmitter.FlameLick, licks);

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

        // Flame on every tier: a few very large puffs thrown up out of the strike. Below Medium this
        // is the whole fireball, so it is never thinned under about ten puffs.
        VfxBurstSpec gout = VfxBurstSpec.At(heart, Flame(cast.Colors), Mathf.Max(0.65f, density * 1.4f));
        gout.Extents = new Vector3(radius * 0.3f, 0.3f, radius * 0.3f);
        gout.Direction = Vector3.Up;
        gout.Spread = 65f;
        gout.SizeScale = Mathf.Clamp(radius * 0.6f, 1f, 2.8f);
        gout.SpeedScale = 2f;
        gout.LifeScale = rich ? 1.4f : 0.65f;
        fx.Burst(VfxEmitter.Flame, gout);

        // The crater: the ground cracked and glowing where it struck, long after the pillar.
        VfxDiscSpec crater = VfxDiscSpec.At(floor, radius * 0.9f, cast.Colors);
        crater.Pattern = VfxDiscPattern.Cracks;
        crater.Life = rich ? 4.5f : 2f;
        crater.Body = 0.14f;
        crater.Rim = 0f;
        crater.Flow = 0.1f;
        fx.Disc(crater);

        if (!rich)
        {
            return true;
        }

        // What burns on in the crater: smoke climbing out of it for seconds, and (High and up)
        // small fires scattered across the scorch.
        VfxBurstSpec column = VfxBurstSpec.At(floor + (Vector3.Up * 0.3f), cast.Colors, density * budget.DebrisMultiplier * 0.6f);
        column.Continuous = true;
        column.StreamSeconds = 3.2f;
        column.Extents = new Vector3(radius * 0.3f, 0.1f, radius * 0.3f);
        column.Direction = Vector3.Up;
        column.Spread = 14f;
        column.SpeedScale = 1.7f;
        column.SizeScale = Mathf.Clamp(radius * 0.45f, 0.9f, 2.2f);
        fx.Burst(VfxParticles.Smoke, column);
        if (lavish)
        {
            VfxBurstSpec fires = VfxBurstSpec.At(floor + (Vector3.Up * 0.15f), Flame(cast.Colors), density * 0.5f);
            fires.Continuous = true;
            fires.StreamSeconds = 3f;
            fires.Extents = new Vector3(radius * 0.55f, 0.05f, radius * 0.55f);
            fires.Direction = Vector3.Up;
            fires.Spread = 18f;
            fires.SizeScale = 0.55f;
            fires.SpeedScale = 0.6f;
            fires.LifeScale = 0.75f;
            fx.Burst(VfxEmitter.Flame, fires);
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

    /// <summary>
    /// A Rime Shard shatters on what it strikes and leaves the floor under it frozen. Above the
    /// leanest tier frost creeps out across the floor at the feet of what was struck and is gone in
    /// about two seconds; Medium and up throw the shard's crystals back the way it came with glints
    /// hanging where it broke, a breath of mist along the floor and the longer mark of frost. (The
    /// leanest tier keeps the generic hit and gains nothing.)
    /// </summary>
    private static bool RimeShardHit(in VfxCast cast, in SpellImpactInfo hit)
    {
        VfxBudget budget = VfxQuality.Budget;
        if (hit.Kind is SpellImpactKind.Blocked or SpellImpactKind.Expired || VfxQuality.Tier == VfxTier.Performance)
        {
            return false;
        }

        // At the feet of a body, or where the shard struck a floor; never hung in the air or laid
        // flat across a wall. Not under the first-person player: a patch of light about the camera
        // is the frame.
        Node3D? struck = VfxAnchor.BodyOf(hit.Target);
        bool floored = struck != null || (hit.Kind == SpellImpactKind.World && hit.Normal.Y > 0.7f);
        bool onTheEye = hit.Target != null && IsPlayer(hit.Target) && AtTheEye(hit.Position + (Vector3.Up * 0.6f));
        Vector3 foot = hit.Position;
        if (struck != null)
        {
            // A body's origin is not always its feet (a practice dummy's is its middle).
            foot = struck.GlobalPosition with { Y = FloorUnder(struck) };
        }

        if (floored && !onTheEye)
        {
            FrostSpread(cast, foot + (Vector3.Up * 0.04f), 1.15f + (0.5f * cast.Weight), VfxElementalRules.RimeFrostSeconds);
        }

        if (!budget.SecondaryDebris)
        {
            return false;
        }

        // Thrown back the way the shard came: it shatters against what it struck.
        Vector3 back = hit.Normal.LengthSquared() > 0.0001f ? hit.Normal.Normalized() : Vector3.Up;
        VfxBurstSpec crystals = VfxBurstSpec.At(hit.Position, cast.Colors, budget.ParticleMultiplier * 0.9f);
        crystals.Direction = back;
        crystals.Spread = 62f;
        crystals.SpeedScale = 1.1f;
        crystals.SizeScale = 0.9f;
        cast.Fx.Burst(VfxEmitter.Crystals, crystals);
        if (VfxQuality.Rich.Glints)
        {
            VfxBurstSpec glints = VfxBurstSpec.At(hit.Position, cast.Colors, budget.ParticleMultiplier * 0.6f);
            glints.Extents = Vector3.One * 0.45f;
            glints.LifeScale = VfxQuality.Rich.LifeScale;
            cast.Fx.Burst(VfxEmitter.Glints, glints);
        }

        if (floored && !onTheEye)
        {
            GroundMist(cast, foot, 1.6f, 0.5f);
            cast.Fx.Mark(new VfxGroundMarkSpec
            {
                Mark = VfxMark.Frost,
                Position = foot,
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
    /// danger, and the storm is what falls through it: fast streaks of snow driven across the whole
    /// radius on a slant, (Medium and up) mist hugging the floor with frost under it and a few
    /// fine flakes adrift, and a cold light.
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

        // Snow: many small fast streaks driven across the zone on a slant (the kit's snow), each
        // layer on a wind of its own so the streaks cross. One emitter holds only so many, so the
        // richer tiers stack them; the leanest draws the one.
        int layers = VfxElementalRules.SnowLayers(VfxQuality.Tier);
        for (int i = 0; i < layers; i++)
        {
            Vector3 wind = new Vector3(0.55f - (0.18f * i), -0.8f, 0.2f + (0.22f * i)).Normalized();
            VfxHandle<VfxBurst> falling = rig.Add(Snowfall(cast, Vector3.Zero, radius * 0.9f, 0f, wind, i == 0 ? 1.4f : 0.9f));
            falling.Get?.Follow(VfxAnchor.To(zone, (Vector3.Up * (3f + (0.4f * i))) - (wind * 2f)));
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

        // Mist hugging the ground: flat banks rolling out across the floor, never rising to the
        // height of a face. Few of them, or six seconds of it is a bank of fog over the fight.
        VfxBurstSpec mist = VfxBurstSpec.At(Vector3.Zero, cast.Colors, density * area * 0.45f);
        mist.Continuous = true;
        mist.Extents = new Vector3(radius * 0.7f, 0.06f, radius * 0.7f);
        mist.Direction = Vector3.Right;
        mist.Spread = 180f;
        mist.Flatness = 1f;
        mist.SpeedScale = 0.8f;

        // A puff of mist swells as it ages and is drawn about its middle: any larger than this and
        // its top is at the height of a face, for the whole of the storm, with the caster in it.
        mist.SizeScale = Mathf.Clamp(radius * 0.2f, 0.6f, VfxElementalRules.ZoneMistScale);
        rig.Add(cast.Fx.Burst(VfxEmitter.Mist, mist)).Get?.Follow(VfxAnchor.To(zone, Vector3.Up * 0.22f));

        if (VfxQuality.Rich.Glints)
        {
            // A few fine flakes drifting among the streaks. (The hail that fell here was a field of
            // large bright diamonds between the camera and the fight.)
            VfxBurstSpec flurry = VfxBurstSpec.At(Vector3.Zero, cast.Colors, density * area * 0.35f);
            flurry.Continuous = true;
            flurry.Extents = new Vector3(radius * 0.8f, 1.1f, radius * 0.8f);
            flurry.Direction = Vector3.Down;
            flurry.Spread = 40f;
            rig.Add(cast.Fx.Burst(VfxEmitter.Flurry, flurry)).Get?.Follow(VfxAnchor.To(zone, Vector3.Up * 1.7f));
        }

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
    /// and a squall of small hail is driven down on the slant of the snow with it. No flash: the
    /// caster is standing in it.</summary>
    private static bool BlizzardPulse(in VfxCast cast, Vector3 position, float radius, SpellBurstSource source, float charge)
    {
        if (source != SpellBurstSource.Zone)
        {
            return false;
        }

        radius = Mathf.Max(0.5f, radius);
        VfxBudget budget = VfxQuality.Budget;
        ThinRing(cast, position + (Vector3.Up * 0.05f), radius, 0.5f, energy: 0.7f);

        VfxBurstSpec squall = VfxBurstSpec.At(position + (Vector3.Up * 2.6f), cast.Colors, budget.ParticleMultiplier * 0.6f);
        squall.Extents = new Vector3(radius * 0.7f, 0.3f, radius * 0.7f);
        squall.Direction = new Vector3(0.45f, -1f, 0.18f);
        squall.Spread = 16f;
        squall.SpeedScale = 1.3f;

        // Small: at their own size, seen from inside the storm, these were diamonds a hand across
        // tumbling past the camera.
        squall.SizeScale = 0.45f;
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
    /// The Glacial Bulwark as a row of crystals. A flat sheet, however it is textured, is a backlit
    /// sign; this is jagged prisms of ice standing shoulder to shoulder along the wall's line, each
    /// with real depth, its own height (tallest in the middle) and its own lean, so the top edge is
    /// broken and the wall has a silhouette from every side. Between them, well under their tips,
    /// stands a low web of plate ice that closes the gaps a body cannot pass. Three crystals on the
    /// leanest tier, seven on the richest (<see cref="VfxElementalRules.PrismCount"/>). Glints drift
    /// off it, shards burst as it rises, and (Medium and up) cold mist lies along its foot over a
    /// line of frost. Let go, the ice shatters plate by plate.
    /// </summary>
    private static bool GlacialBulwarkWall(in VfxCast cast, VfxRig rig, Node3D barrier, float width, float height)
    {
        width = Mathf.Max(0.5f, width);
        height = Mathf.Max(0.5f, height);
        VfxBudget budget = VfxQuality.Budget;
        bool rich = budget.SecondaryDebris;
        float density = budget.ParticleMultiplier;
        VfxSchoolColors colors = cast.Colors;
        VfxAnchor foot = VfxAnchor.To(barrier);

        // A wall that had a telegraph already stands where it will, so its own axes can be read and
        // the crystals stood along them. One raised with no delay is placed after this call: it gets
        // the sheet of plate ice alone, at its full height.
        bool placed = (cast.Spell?.GroundDelay ?? 0f) > 0f && barrier.IsInsideTree();

        VfxShellSpec web = VfxShellSpec.IceWall(
            Vector3.Zero, width * (placed ? 0.92f : 1f), height * (placed ? VfxElementalRules.PrismWebHeight : 1f), colors);
        web.Occlude = placed ? 0.45f : 0.6f;
        web.Energy = placed ? 0.36f : 0.5f;
        web.RiseSeconds = 0.35f;
        web.Layered = rich;
        VfxHandle<VfxShell> wall = rig.Add(cast.Fx.Shell(web));
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

        if (placed)
        {
            Basis axes = barrier.GlobalBasis.Orthonormalized();
            int prisms = VfxElementalRules.PrismCount(VfxQuality.Tier);

            // The post is 0.9 m tall and 0.26 m across its foot, tapering to under half that: scaled
            // wide and deep it is a faceted crystal narrowing to a blunt tip.
            const float PostHeight = 0.9f;
            const float PostFoot = 0.26f;
            float girth = Mathf.Clamp(width / prisms * 1.45f, 0.45f, 1.5f);
            float depth = Mathf.Min(girth * VfxElementalRules.PrismDepthRatio, VfxElementalRules.PrismMaxDepth);
            for (int i = 0; i < prisms; i++)
            {
                float across = ((i + 0.3f + (0.4f * Roll())) / prisms) - 0.5f;
                float tall = height * VfxElementalRules.PrismHeight(across, Roll());
                float thick = girth * (0.8f + (0.4f * Roll()));

                // Each leans its own way: outward from the middle along the wall, and a little to
                // the front or the back, turned so no two faces line up.
                float lean = Mathf.DegToRad(VfxElementalRules.MaxPrismLean * (0.25f + (0.75f * Roll())));
                float side = i % 2 == 0 ? 1f : -1f;
                Vector3 toward = ((axes.Z * side) + (axes.X * ((across * 1.2f) + ((Roll() - 0.5f) * 0.5f)))).Normalized();

                VfxShellSpec prism = VfxShellSpec.IceShell(Vector3.Zero, 0.5f, colors);
                prism.Shape = VfxShellShape.Post;
                prism.Size = new Vector3(thick / PostFoot, tall / PostHeight, depth / PostFoot);
                prism.Sustain = true;
                prism.Fresnel = false;
                prism.Occlude = 0.55f;
                prism.Energy = 0.42f;
                prism.Tiling = new Vector2(2f + Roll(), 1.4f + Roll());

                // Up in a blink, ahead of the sheet between them. A post is stood on its base at
                // its full height and grows about its middle, so a slow rise shows its foot in the
                // air; this fast it is out of the ground before it has faded in.
                prism.RiseSeconds = 0.1f + (0.1f * Roll());

                // Looking along that heading tipped down by the lean stands the post's own up axis
                // off upright by the same angle, toward it.
                prism.Forward = (toward * Mathf.Cos(lean)) + (Vector3.Down * Mathf.Sin(lean));
                VfxHandle<VfxShell> raised = rig.Add(cast.Fx.Shell(prism));

                // Alternately a hand in front of and behind the line, so the crystals overlap.
                raised.Get?.Follow(VfxAnchor.To(barrier, (axes.X * (across * width * 0.94f)) + (axes.Z * (side * 0.12f))));
            }
        }

        // Glints drifting off the face.
        VfxBurstSpec glints = VfxBurstSpec.At(Vector3.Zero, colors, density * Mathf.Clamp(width * 0.3f, 0.5f, 2f) * 0.6f);
        glints.Continuous = true;
        glints.Extents = new Vector3(width * 0.5f, height * 0.45f, 0.25f);
        glints.SpeedScale = 0.3f;
        // Not on the leanest tier: its third crystal is drawn in their place.
        VfxHandle<VfxBurst> drifting = VfxQuality.Tier == VfxTier.Performance
            ? default
            : rig.Add(cast.Fx.Burst(VfxParticles.Motes, glints));
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
        VfxHandle<VfxBurst> up = rig.Add(
            rich ? cast.Fx.Burst(VfxEmitter.Crystals, rise) : cast.Fx.Burst(VfxParticles.Shards, rise));
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

        VfxBurstSpec mist = VfxBurstSpec.At(Vector3.Zero, colors, density * budget.DebrisMultiplier * 0.4f);
        mist.Continuous = true;
        mist.Extents = new Vector3(width * 0.5f, 0.06f, 0.4f);
        mist.Direction = Vector3.Right;
        mist.Spread = 180f;
        mist.Flatness = 1f;
        mist.SpeedScale = 0.5f;
        mist.SizeScale = 0.7f;
        VfxHandle<VfxBurst> lying = rig.Add(cast.Fx.Burst(VfxEmitter.Mist, mist));
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

    /// <summary>Where Ball Lightning bursts it detonates (<see cref="LightningBlast"/>).</summary>
    private static bool BallLightningHit(in VfxCast cast, in SpellImpactInfo hit)
    {
        if (hit.Kind is SpellImpactKind.Blocked or SpellImpactKind.Expired)
        {
            return false;
        }

        // The orb detonates: a strike's worth of forks into the floor, a clap and sparks, on every
        // tier, in place of the generic flash (which at this size was a soft glow).
        float groundY = VfxAnchor.BodyOf(hit.Target) is { } body ? FloorUnder(body) : float.NaN;
        LightningBlast(cast, hit.Position, 2.4f * (hit.Crit ? 1.15f : 1f), groundY);
        StruckCrackle(cast, hit.Target);
        return true;
    }

    /// <summary>Arcs left crackling over a body lightning has just struck, for a second or so after
    /// the flash is gone (not on the leanest tier, and not on the first-person player, whose own
    /// body is the camera).</summary>
    private static VfxHandle<VfxMotif> StruckCrackle(in VfxCast cast, IEntity? target)
    {
        if (!VfxElementalRules.StruckCrackle(VfxQuality.Tier) || VfxAnchor.BodyOf(target) is not { } struck ||
            (IsPlayer(target) && AtTheEye(struck.GlobalPosition + (Vector3.Up * 1.6f))))
        {
            return default;
        }

        return ResidualCrackle(cast, struck, VfxElementalRules.StruckCrackleSeconds);
    }

    /// <summary>What Thunder Step's strike lands on is left crackling, over the generic hit.</summary>
    private static bool ThunderStepHit(in VfxCast cast, in SpellImpactInfo hit)
    {
        if (hit.Kind == SpellImpactKind.Target)
        {
            StruckCrackle(cast, hit.Target);
        }

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
            Width = AtTheViewHand(from) ? 0.016f : 0.028f,
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
    /// and up) over the generic flicker and sparks, and what it is held on is left crackling. The
    /// beam ticks several times a second, so the arcs are one set held by the beam and renewed only
    /// when the last has run out: they outlast the channel by about a second.</summary>
    private static bool StormConduitHit(in VfxCast cast, in SpellImpactInfo hit)
    {
        if (hit.Kind != SpellImpactKind.Target)
        {
            return false;
        }

        if (cast.Caster is { } caster && Beams.TryGetValue(caster.RuntimeId, out VfxRig? beam) && !beam.Motif.IsLive)
        {
            // Held on the rig but not added to it: stopping the beam leaves them to run out.
            beam.Motif = StruckCrackle(cast, hit.Target);
        }

        if (!VfxQuality.Budget.SecondaryDebris)
        {
            return false;
        }

        GroundForks(cast, hit.Position, VfxQuality.Tier >= VfxTier.High ? 3 : 2, 0.9f, 0.4f, 0.012f);
        return false;
    }

    /// <summary>Stormbrand calls a strike down on what it brands: a thick bolt out of the sky onto
    /// the struck point and a blast where it grounds, over the generic flash and the brand's sigil.</summary>
    private static bool StormbrandHit(in VfxCast cast, in SpellImpactInfo hit)
    {
        if (hit.Kind != SpellImpactKind.Target)
        {
            return false;
        }

        // The strike: a wide soft channel, a bright body and a thin white heart on one line out of
        // the sky (the leanest tier keeps the two that carry it), then the ground answers.
        float lean = (Roll() - 0.5f) * 3f;
        Vector3 sky = hit.Position + new Vector3(lean, 9f, (Roll() - 0.5f) * 3f);
        bool rich = VfxQuality.Budget.SecondaryDebris;
        Fork(cast, sky, hit.Position, 0.16f, 0.3f, VfxQuality.Budget.BoltBranches, jitter: 0.08f);
        if (rich)
        {
            Fork(cast, sky, hit.Position, 0.07f, 0.24f, jitter: 0.1f);
            Fork(cast, sky, hit.Position, 0.03f, 0.18f, jitter: 0.14f);
        }

        float groundY = VfxAnchor.BodyOf(hit.Target) is { } body ? FloorUnder(body) : float.NaN;
        LightningBlast(cast, hit.Position, 2f, groundY);
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

        // One thick bolt from where the caster left to where they arrived, on every tier.
        Fork(cast, start, end, 0.12f, 0.34f, budget.BoltBranches, jitter: 0.075f);
        int extra = VfxQuality.Tier switch
        {
            VfxTier.Medium => 1,
            VfxTier.High or VfxTier.Ultra => 2,
            _ => 0,
        };
        for (int i = 0; i < extra; i++)
        {
            Fork(cast, start, end, 0.04f, 0.22f + (0.06f * i), jitter: 0.12f);
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
        float reach = Mathf.Max(1f, cast.Spell?.DashHitRadius ?? 1.4f);

        // Sized so the clap's first ring runs out to the reach of the hit and no further. The
        // caster stands at its centre with the camera a few metres behind: any wider and the second
        // ring of the richer tiers is a band of light across the lower half of the frame.
        LightningBlast(cast, end, reach * VfxElementalRules.DashClapScale, to.Y);

        if (!rich)
        {
            return true;
        }

        // A line of scorch along the floor it crossed, and a wider one where it stopped.
        if (budget.GroundMarks > 0)
        {
            int marks = Mathf.Clamp(Mathf.CeilToInt(length / 2.6f), 1, Mathf.Max(1, budget.GroundMarks / 3));
            for (int i = 0; i < marks; i++)
            {
                fx.Mark(new VfxGroundMarkSpec
                {
                    Mark = VfxMark.Scorch,
                    Position = from.Lerp(to, (i + 0.5f) / (marks + 1)),
                    Size = 1.2f,
                    Colors = cast.Colors,
                    Life = 6f,
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
