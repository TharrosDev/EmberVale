using Embervale.Combat;
using Embervale.Entities;
using Godot;

namespace Embervale.Magic.Vfx;

/// <summary>
/// The kit: what each school's blast is made of beyond the flags of its recipe, and the richer
/// building pieces a special case can call for. This is the layer that makes a fireball fire and
/// explosions and a lightning strike lightning and explosions, and that makes the tiers differ.
///
/// <para><b>HOW A RECIPE GETS THIS FOR FREE.</b> Nothing here needs a new <see cref="VfxStage"/> flag.
/// The generic interpreter calls <see cref="Signature"/> from <c>Blast</c> for every impact, burst
/// and self-cast, authored or not, and it adds by school and by tier
/// (<see cref="VfxRichness"/>, read through <c>VfxQuality.Rich</c>):</para>
/// <list type="bullet">
/// <item><b>Fire</b>: a billow of flame puffs that cool to rising smoke (<see cref="VfxEmitter.Flame"/>),
/// debris off the ground (<see cref="VfxEmitter.Chunks"/>), long-lived flying embers, and for a large
/// blast a column of smoke.</item>
/// <item><b>Frost</b>: thrown ice crystals (<see cref="VfxEmitter.Crystals"/>), cold mist rolling out
/// along the floor (<see cref="VfxEmitter.Mist"/>), glints.</item>
/// <item><b>Lightning</b>: forked bolts thrown from the centre to the ground around it
/// (<see cref="Forks"/>), debris, a wisp of smoke.</item>
/// <item><b>Arcane</b>: glints and, on the top tier, a second scatter of sparks.</item>
/// <item><b>Nature</b>: glints of pollen, and earth thrown up.</item>
/// <item><b>Necrotic</b>: soul-fire that cools to dark smoke.</item>
/// </list>
/// On the Performance tier none of it is drawn: a blast there is its flare, its ring and a few
/// particles.
///
/// <para><b>PIECES A SPECIAL CAN CALL</b> (all take the <see cref="VfxCast"/> a hook is handed, are
/// safe at any tier and distance, and are one-shots that end themselves):</para>
/// <list type="bullet">
/// <item><see cref="Explosion"/>: the whole school blast at a point (flare, rays, ring, ball,
/// signature, particles). The generic <c>Blast</c> with a plan built for you.</item>
/// <item><see cref="FireBillow"/>: flame puffs that turn to smoke.</item>
/// <item><see cref="SmokeColumn"/>: smoke rising from a point for a while, then stopping by itself.</item>
/// <item><see cref="Debris"/>: dark chunks thrown up off the ground.</item>
/// <item><see cref="ShardBurst"/>: ice crystals thrown point first, with glints.</item>
/// <item><see cref="GroundMist"/>: cold mist rolling out along the floor.</item>
/// <item><see cref="Forks"/>: forked lightning from a point to the ground around it.</item>
/// <item><see cref="FrostSpread"/>: frost creeping outward across the floor from a point.</item>
/// </list>
/// New block capabilities these rest on, all with defaults that leave older code as it was:
/// <c>VfxFlareSpec.Rays</c> and <c>RingStreaks</c>; <c>VfxShellSpec.Ball</c>, <c>IceWall</c>,
/// <c>IceShell</c> (and <c>Ragged</c>, <c>Cools</c>, <c>Ice</c>); <c>VfxDiscSpec.Pattern</c> and
/// <c>SpreadSeconds</c>; <c>VfxBoltSpec.Electric</c>; <c>VfxBurstSpec.Flatness</c> and
/// <c>StreamSeconds</c>; and <c>cast.Fx.Burst(VfxEmitter, spec)</c>.
///
/// <para><b>KIT API, AS HANDED TO THE RECIPE LANES (kept verbatim; edit both together).</b></para>
/// <code>
/// THE SECOND KIT (shapes, anchors, first person). Everything below is additive: no existing
/// signature, flag or preset name changed. All helpers take the VfxCast a hook is handed, are safe at
/// any tier and distance, and return an empty handle when nothing is drawn.
///
/// WHAT CHANGED UNDER EVERY RECIPE, WITH NO RECIPE EDIT
/// - VfxParticles.Motes is drawn with a small four-pointed mote, not the soft dot; for a Frost cast it is
///   a snowflake (VfxSprite.Flake), for Lightning and Arcane a glint. VfxParticles.Sparks thrown by a
///   Lightning cast are jagged (VfxSprite.Spark); thrown by a Frost cast they are streaks of snow
///   (VfxSprite.Snow), so a frost recipe that throws slanted Sparks is already a blizzard.
///   VfxParticles.Wisps are tendrils (VfxSprite.Wisp). A sprite standing in on an emitter that follows
///   its travel brings its own quad: a jagged spark or a streak of snow is drawn 0.28 wide to its
///   length, where the plain spark stays the 0.12 sliver.
///   The school comes from the VfxCast: cast.Fx knows it (VfxSpawner.School), so this applies to every
///   cast.Fx.Burst(VfxParticles.X, spec). Set spec.Sprite to force a shape.
/// - VfxEmitter.Flame is drawn with tongues of flame standing tip up (VfxSprite.Flame), still cooling
///   to smoke. The smoke puff (VfxSprite.Puff) has a lobed, ragged outline.
/// - Every wind-up gets its school's motif at the hand and every generic bolt gets its school's shaped
///   head (below). On Performance and Low the wind-up motif replaces the particle stream (one draw
///   for one), and a bolt's head replaces the white core of its glow.
/// - A held glow (wind-up aura, bolt head) is hand-sized and school-coloured at the first-person
///   casting point: VfxCoverageRules.NearStart/NearEnd are now 1.5 m / 5 m, and its energy and halo are
///   cut near the eye. The player's own bolt head is drawn at half size in first person.
/// - A flat ring or disc on the floor about the camera (a self-cast in first person) is cut to 16% while
///   its radius is under about 3 m (VfxScreenRules.SelfRing). Telegraphs (VfxDiscSpec.Fills) never are,
///   and neither is a Sustain ring or disc that is not the player's own (an enemy's zone to get out of):
///   the cut applies to the player's own effects and to anybody's one-shot rings (a hit landing on them).
/// - The generic Release calls SelfCastInView for every Self delivery cast by the player in first
///   person (not a blink), before the spell's own Release hook.
///
/// NEW SPRITES (enum VfxSprite, appended): Flame, Snow, Flake, Spark, Wisp, Ash, Mote.
///   VfxBurstSpec.Sprite (VfxSprite?, default null): draws a burst with another sprite. It must suit the
///   preset (VfxBurstPresets.Fits): Streak, Comet, Crystal, Snow, Spark, Wisp go on presets that follow
///   their travel (Sparks, Embers, Wisps, Crystals, Snow, FlameLick); Dot, Shard, Leaf, Puff, Glint,
///   Flake, Ash, Mote on the others; Flame on either. An unsuitable one is ignored.
///
/// NEW EMITTER PRESETS (enum VfxEmitter, appended; throw with cast.Fx.Burst(VfxEmitter.X, spec)):
///   VfxEmitter.Snow = 13      64 small fast streaks, 0.7 s, steady. Give it a slanted Direction, a
///                             narrow Spread (5 to 10) and a wide flat Extents box above the zone.
///   VfxEmitter.AshFlake = 14  26 dark tumbling flakes that hang 3 s (LifeScale 1.2 for 3.6 s), covering.
///                             spec.Tint recolours them.
///   VfxEmitter.FlameLick = 15 22 licks of flame along their travel, 0.5 s, rising, added light only.
///   VfxEmitter.Flurry = 16    30 fine snowflakes drifting down and twinkling, 2.2 s.
///   VfxEmitter.Mist = 10 is unchanged (the Mist preset asked for already existed under that name).
///
/// NEW BLOCK: VfxMotif, through cast.Fx.Motif(VfxMotifSpec) -&gt; VfxHandle&lt;VfxMotif&gt;. A handful of shaped
///   sprites moving in a pattern, one draw call, no particle simulation.
///   VfxMotifSpec.At(position, colors, VfxSprite sprite, VfxMotion motion, int count, float radius, float size)
///   VfxMotifSpec.Of(position, colors, VfxMotifStyle style, float glow)   // a school's style at a glow size
///   Fields: Tint, Aspect (width/length, 0.5), Speed (1), Energy (1, against MidEnergy), Opacity (1),
///     Pale (core colour), Sustain, Life (1 s), Level (1), Axis (Lance), Scatter (Lance), TrueSize
///     (do not shrink near the eye).
///   VfxMotion: Lick (tongues standing on a base), Orbit (circling, point first), Crackle (arcs that
///     jump 13 times a second), Swirl (climbing, tumbling, faces the camera), Inward (drawn in to the
///     centre), Lance (index 0 leads along Axis, the rest strung out behind).
///   VfxMotif.SetLevel(float), VfxMotif.Aim(Vector3 axis); Follow, SettleFrom, Glide as every block.
///   VfxMotifRules.Windup(school) / Head(school) -&gt; VfxMotifStyle; CountAt(style, tier); MaxCount = 12.
///   VfxRig.Motif: the wind-up motif of an aura (WindupProgress fills it).
///
/// NEW BLOCK FIELDS (defaults leave older code as it was)
///   VfxFlareSpec.HaloOnly      no core, keep halo and light (the glow behind a shaped head).
///   VfxShellSpec.Tongues       0..1, a Sheet of fire as licks standing on a hot ground line, ends faded out.
///   VfxShellSpec.LayerGap      metres either side of the line a layered sheet's two layers stand (0 = 0.12).
///   VfxShellSpec.SettleSeconds / SettleHold / SettleOpacity   a sustained shell holds SettleHold seconds,
///                              then fades over SettleSeconds to SettleOpacity of its opacity and stays.
///   VfxDistortionSpec.Shimmer  heat haze instead of a pressure wave: the image wavers across the whole
///                              shape, climbing, with no outline. Sustain it; Strength 0.01 is plenty.
///   VfxDistortionSpec.Stretch  the shape against a sphere of Radius along the block's own axes (turn it
///                              with OrientLike): (3, 1, 0.3) is a thin slab. Zero = a sphere.
///                              (Distortion is still High and Ultra only, never under Reduced Motion.)
///   VfxSpawner.School, VfxSpawner.ForSchool(school)           set for you by VfxCast.
///   VfxSpawner.ScreenEdge(Color school, float strength, float seconds = 0.9f) -&gt; bool
///                              a shimmer at the edges of the screen, middle clear; comfort-capped
///                              (VfxScreenRules.EdgePeak: 0.3 x the flash setting, 0.12 under Reduced Motion).
///
/// NEW HELPERS (SpellVfx.Kit.cs, internal static)
///   bool ViewIsFirstPerson(IEntity? caster)
///   void BodyFit(Node3D body, out Vector3 centre, out Vector3 size)
///       centre is an offset from body.GlobalPosition to the middle of its collision shape; size is
///       (width, height, width). Use VfxAnchor.To(body, centre) for anything worn on a body.
///   VfxHandle&lt;VfxMotif&gt; WindupMotif(in VfxCast cast, VfxRig rig, in VfxAnchor hand, Vector3 at, float glow, float level = 1f)
///   VfxHandle&lt;VfxMotif&gt; ProjectileHead(in VfxCast cast, VfxRig rig, Node3D projectile, Vector3 handOffset, Vector3 direction, float size)
///   float ProjectileViewScale(in VfxCast cast)
///       0.5 for the player's own bolt in first person, else 1. A Projectile hook that draws its own
///       head multiplies its sizes by it (the generic projectile already does).
///   VfxHandle&lt;VfxMotif&gt; ResidualCrackle(in VfxCast cast, Node3D body, float seconds, float size = 0f)
///       arcs over a body for a status's duration; ends itself.
///   VfxMotifSpec CrackleOver(Node3D body, in VfxSchoolColors colors, float size, out Vector3 centre)
///       the same arcs as a spec, for a rig of your own: set Sustain, rig.Add(cast.Fx.Motif(spec)),
///       Follow(VfxAnchor.To(body, centre)).
///   void TetherScatter(in VfxCast cast, Vector3 from, Vector3 to, float amount = 1f)
///       motes torn off `from` along the line, motes drawn in to `to`.
///   VfxAnchor MouthAnchor(IEntity caster)            // jaw/mouth/snout bone, else head, else 1.6 m up
///       VfxAnchor.ToMouth(caster, fallbackOffset), VfxAnchor.HasBone
///   void MouthGlow(in VfxCast cast, VfxRig rig, in VfxAnchor mouth, float size, float seconds)
///       a Windup hook's whole body for a breath: glow that builds, inhaled particles, the school motif.
///   VfxHandle&lt;VfxBurst&gt; BreathPuffs(in VfxCast cast, Vector3 origin, Vector3 axis, float range, float angleDegrees, VfxEmitter kind, float amount = 1f)
///   VfxHandle&lt;VfxBurst&gt; Snowfall(in VfxCast cast, Vector3 centre, float radius, float seconds = 0f, Vector3 wind = default, float amount = 1f)
///       returns the streak emitter (add to the rig, Follow the zone); with seconds &gt; 0 it also throws
///       a Flurry on tiers with glints.
///   VfxHandle&lt;VfxBurst&gt; AshFall(in VfxCast cast, Vector3 centre, float radius, float seconds = 0.8f, Color tint = default, float amount = 1f)
///   void SelfCastInView(in VfxCast cast, float strength = 0.6f)
///
/// NEW HOOK: SpellVfxSpecial.TotemPulse (delegate bool TotemPulseHook(in VfxCast cast, Node3D totem, Node3D? target)).
///   Runs at the top of SpellVfx.TotemPulse; true replaces the generic ring, beat and heal line.
///
/// GENERIC LOOKS THAT CHANGED (a hook returning false still gets them)
///   Pyre Wall (AttachBarrier, not solid): Tongues = 1, RiseSeconds 0.25, energy 0.45; Medium and up add
///     a second, lower pair of layers 0.34 m either side and FlameLick particles along the length;
///     Ultra adds a slab of heat haze (Shimmer) over it.
///   A lightning mark (StatusAura MarkRing with a Lightning school: the brand) wears CrackleOver arcs
///     for as long as the status lasts, above Performance. A recipe need not add its own.
///   VfxEmitter.Flame particles are 0.62 to 1.3 m (were 0.5 to 1.05 as round puffs): a tongue fills
///     about half the width of its quad.
///   Ward shell (StatusAura WardShell): fitted to the body, bright for 0.35 s, then fades over 0.7 s to
///     22%. A WardHit pulses the fitted shell; on the local player in first person it is a ScreenEdge.
///   Frozen shell (StatusAura IceShell, the Freeze proc, the frost impact shell): fitted with BodyFit.
///   FireBlast on Performance throws no separate spark burst; LightningBlast on Performance leaves no
///     cracks disc. One draw fewer each.
///
/// NOT POSSIBLE FROM THE KIT (frozen call sites)
///   A Release target for a self-buff cast on an ally: SpellVfx.Release(caster, spell, origin,
///   direction, charge) is not told a target, and both call sites (SpellcastingComponent) pass only
///   the caster. It needs a facade signature change in src/Magic.
/// </code>
/// </summary>
public static partial class SpellVfx
{
    /// <summary>The smallest blast that gets its school's extras: below this it is a spark.</summary>
    private const float SignatureRadius = 0.6f;

    /// <summary>
    /// The whole school blast at <paramref name="centre"/> over <paramref name="radius"/> metres,
    /// for a special case that wants an explosion where the generic interpreter draws none.
    /// <paramref name="groundY"/> is the floor's height when known (NaN when not).
    /// </summary>
    internal static void Explosion(in VfxCast cast, Vector3 centre, float radius, float groundY = float.NaN)
    {
        var stage = new VfxStage
        {
            Flare = true,
            Ring = true,
            Particles = SpellVfxCatalog.SchoolParticles(cast.School),
            Secondary = VfxRecipeRules.SchoolSecondary(cast.School),
            Mark = SpellVfxCatalog.SchoolMark(cast.School),
            Distortion = radius >= VfxRecipeRules.DistortionRadius,
        };
        Blast(cast, cast.PlanOf(stage, radius), centre, radius, Vector3.Zero, groundY, hitsPlayer: false);
    }

    /// <summary>Flame puffs that swell, cool and end as smoke. <paramref name="size"/> is the
    /// radius of the fire, metres; <paramref name="amount"/> scales how many.</summary>
    internal static VfxHandle<VfxBurst> FireBillow(
        in VfxCast cast, Vector3 centre, float size, float amount = 1f, bool rising = true)
    {
        if (!VfxQuality.Rich.Billow)
        {
            return default;
        }

        VfxBurstSpec billow = VfxBurstSpec.At(
            centre, cast.Colors, VfxQuality.Budget.ParticleMultiplier * amount * Mathf.Clamp(0.5f + (size * 0.35f), 0.5f, 2.2f));
        billow.SizeScale = Mathf.Clamp(size * 0.6f, 0.4f, 2.6f);
        billow.Extents = Vector3.One * (size * 0.25f);
        billow.SpeedScale = Mathf.Clamp(0.5f + (size * 0.3f), 0.5f, 2.2f);
        billow.LifeScale = Mathf.Clamp(0.8f + (size * 0.1f), 0.8f, 1.5f) * VfxQuality.Rich.LifeScale;
        if (rising)
        {
            billow.Direction = Vector3.Up;
            billow.Spread = 75f;
        }

        return cast.Fx.Burst(VfxEmitter.Flame, billow);
    }

    /// <summary>Smoke rising from <paramref name="foot"/> for <paramref name="seconds"/>, then
    /// stopping by itself. Drawn where the tier has smoke columns.</summary>
    internal static VfxHandle<VfxBurst> SmokeColumn(in VfxCast cast, Vector3 foot, float size, float seconds = 1.1f)
    {
        VfxBudget budget = VfxQuality.Budget;
        if (!VfxQuality.Rich.SmokeColumn || !budget.SecondaryDebris)
        {
            return default;
        }

        VfxBurstSpec column = VfxBurstSpec.At(foot, cast.Colors, budget.ParticleMultiplier * budget.DebrisMultiplier * 0.7f);
        column.Continuous = true;
        column.StreamSeconds = seconds;
        column.Extents = new Vector3(size * 0.3f, 0.1f, size * 0.3f);
        column.Direction = Vector3.Up;
        column.Spread = 16f;
        column.SpeedScale = 1.6f;
        column.SizeScale = Mathf.Clamp(size * 0.5f, 0.7f, 2.4f);
        return cast.Fx.Burst(VfxParticles.Smoke, column);
    }

    /// <summary>Dark chunks thrown up off the ground at <paramref name="foot"/>.</summary>
    internal static VfxHandle<VfxBurst> Debris(in VfxCast cast, Vector3 foot, float size)
    {
        VfxRichness rich = VfxQuality.Rich;
        if (rich.DebrisLayers <= 0)
        {
            return default;
        }

        VfxBurstSpec chunks = VfxBurstSpec.At(
            foot + (Vector3.Up * 0.15f), cast.Colors,
            VfxQuality.Budget.ParticleMultiplier * rich.DebrisLayers * Mathf.Clamp(0.5f + (size * 0.25f), 0.5f, 1.8f));
        chunks.Extents = new Vector3(size * 0.3f, 0.05f, size * 0.3f);
        chunks.Direction = Vector3.Up;
        chunks.Spread = 55f;
        chunks.SpeedScale = Mathf.Clamp(0.6f + (size * 0.2f), 0.6f, 1.6f);
        chunks.SizeScale = Mathf.Clamp(0.8f + (size * 0.15f), 0.8f, 1.8f);
        return cast.Fx.Burst(VfxEmitter.Chunks, chunks);
    }

    /// <summary>Ice crystals thrown point first from <paramref name="centre"/>, with glints hanging
    /// where they were. <paramref name="up"/> throws them off a floor instead of in every direction.</summary>
    internal static void ShardBurst(in VfxCast cast, Vector3 centre, float size, bool up)
    {
        VfxRichness rich = VfxQuality.Rich;
        float multiplier = VfxQuality.Budget.ParticleMultiplier;
        if (rich.Billow)
        {
            VfxBurstSpec crystals = VfxBurstSpec.At(centre, cast.Colors, multiplier * Mathf.Clamp(0.5f + (size * 0.3f), 0.5f, 2f));
            crystals.Extents = Vector3.One * (size * 0.15f);
            crystals.SpeedScale = Mathf.Clamp(0.6f + (size * 0.3f), 0.6f, 2.4f);
            crystals.SizeScale = Mathf.Clamp(0.8f + (size * 0.12f), 0.8f, 1.7f);
            if (up)
            {
                crystals.Direction = Vector3.Up;
                crystals.Spread = 80f;
            }

            cast.Fx.Burst(VfxEmitter.Crystals, crystals);
        }

        if (rich.Glints)
        {
            VfxBurstSpec glints = VfxBurstSpec.At(centre, cast.Colors, multiplier * Mathf.Clamp(0.4f + (size * 0.25f), 0.4f, 1.6f));
            glints.Extents = Vector3.One * Mathf.Max(0.2f, size * 0.55f);
            glints.LifeScale = rich.LifeScale;
            cast.Fx.Burst(VfxEmitter.Glints, glints);
        }
    }

    /// <summary>Cold mist rolling out along the floor from <paramref name="foot"/>.</summary>
    internal static VfxHandle<VfxBurst> GroundMist(in VfxCast cast, Vector3 foot, float size, float amount = 1f)
    {
        if (!VfxQuality.Rich.Billow)
        {
            return default;
        }

        VfxBurstSpec mist = VfxBurstSpec.At(
            foot + (Vector3.Up * 0.25f), cast.Colors,
            VfxQuality.Budget.ParticleMultiplier * amount * Mathf.Clamp(0.4f + (size * 0.3f), 0.4f, 1.8f));
        mist.Extents = new Vector3(size * 0.45f, 0.08f, size * 0.45f);
        mist.Direction = Vector3.Right;
        mist.Spread = 180f;
        mist.Flatness = 1f;
        mist.SpeedScale = Mathf.Clamp(0.6f + (size * 0.25f), 0.6f, 2f);
        mist.SizeScale = Mathf.Clamp(size * 0.4f, 0.6f, 2.2f);
        mist.LifeScale = VfxQuality.Rich.LifeScale;
        return cast.Fx.Burst(VfxEmitter.Mist, mist);
    }

    /// <summary>
    /// Forked lightning from <paramref name="centre"/> to points around it, <paramref name="reach"/>
    /// metres out: on the floor at <paramref name="groundY"/> when it is known, in every direction
    /// when it is NaN. As many forks as the tier draws strands, plus <paramref name="extra"/>.
    /// </summary>
    internal static void Forks(in VfxCast cast, Vector3 centre, float reach, float groundY, int extra = 0)
    {
        if (!cast.Fx.Full || _director is not { } director)
        {
            return;
        }

        VfxBudget budget = VfxQuality.Budget;
        int forks = Mathf.Clamp(VfxQuality.Rich.BoltStrands + extra, 1, 6);
        uint state = unchecked((uint)director.NextSeed());
        float turn = VfxBoltPath.Unit(ref state) * Mathf.Tau;
        bool grounded = !float.IsNaN(groundY);
        for (int i = 0; i < forks; i++)
        {
            float angle = turn + (Mathf.Tau * i / forks) + (VfxBoltPath.Signed(ref state) * 0.5f);
            float far = reach * (0.55f + (0.45f * VfxBoltPath.Unit(ref state)));
            Vector3 to = grounded
                ? new Vector3(centre.X + (Mathf.Cos(angle) * far), groundY + 0.05f, centre.Z + (Mathf.Sin(angle) * far))
                : centre + (new Vector3(Mathf.Cos(angle), VfxBoltPath.Signed(ref state) * 0.7f, Mathf.Sin(angle)).Normalized() * far);
            cast.Fx.Bolt(new VfxBoltSpec
            {
                Mode = VfxBoltMode.Lightning,
                From = centre,
                To = to,
                Colors = cast.Colors,
                Width = 0.07f,
                Jitter = 0.16f,
                Segments = VfxRecipeRules.BoltSegments(budget, centre.DistanceTo(to)),
                Branches = budget.BoltBranches,
                Life = 0.16f + (0.05f * i),
                Seed = director.NextSeed(),
            });
        }
    }

    /// <summary>Frost creeping outward across the floor from <paramref name="foot"/> to
    /// <paramref name="radius"/> metres over <paramref name="seconds"/>, then fading.</summary>
    internal static VfxHandle<VfxDisc> FrostSpread(in VfxCast cast, Vector3 foot, float radius, float seconds = 0.7f)
    {
        VfxDiscSpec frost = VfxDiscSpec.At(foot, Mathf.Max(0.5f, radius), cast.Colors);
        frost.Pattern = VfxDiscPattern.Frost;
        frost.SpreadSeconds = Mathf.Max(0.2f, seconds);
        frost.Life = frost.SpreadSeconds * 2.4f;
        frost.Body = 0.25f;
        frost.Rim = 0f;
        frost.Flow = 0.15f;
        return cast.Fx.Disc(frost);
    }

    /// <summary>
    /// A fireball going off: the layered explosion a fire spell lands with. Every tier gets the
    /// short white-hot core with its pulse of light, the shock ring, a burst of large flame puffs,
    /// thrown sparks and a patch of glowing cracks left on the floor, which is a handful of draws.
    /// Medium and up add the eroding ball of fire that cools to smoke, a billow that rises, smoke
    /// rolling up after it, flying embers, debris and a scorch; High and up leave small flames
    /// burning on the ground and bend the air. <paramref name="radius"/> is the size of the fire
    /// (not a damage radius: nothing here is read back). <paramref name="groundY"/> is the floor
    /// under it, NaN when it went off in the air.
    /// </summary>
    internal static void FireBlast(in VfxCast cast, Vector3 centre, float radius, float groundY = float.NaN)
    {
        VfxSpawner fx = cast.Fx;
        VfxBudget budget = VfxQuality.Budget;
        VfxRichness rich = VfxQuality.Rich;
        VfxTier tier = VfxQuality.Tier;
        radius = Mathf.Clamp(radius, 0.6f, 6f);
        bool grounded = !float.IsNaN(groundY) && centre.Y - groundY <= Mathf.Max(2f, radius);
        var floor = new Vector3(centre.X, grounded ? groundY : centre.Y - (radius * 0.5f), centre.Z);
        VfxSchoolColors flame = Flame(cast.Colors);
        float density = budget.ParticleMultiplier;
        float amount = Mathf.Clamp(0.6f + (radius * 0.3f), 0.6f, 2f);

        BlastCore(cast, centre, radius * 0.4f, Mathf.Max(5f, radius * 3.5f));
        Vector3 facing = grounded || _director is not { HasCamera: true } eye ? default : eye.CameraPosition - centre;
        ThinRing(cast, grounded ? floor + (Vector3.Up * 0.12f) : centre, radius * 1.25f, 0.36f, facing, energy: 1.7f);

        // The body of the blast on every tier: a few large puffs of flame, at once. Below Medium
        // this is all the fire there is, so it is never thinned under about ten puffs.
        VfxBurstSpec puffs = VfxBurstSpec.At(centre, flame, Mathf.Max(0.6f, density * amount));
        puffs.Extents = Vector3.One * (radius * 0.22f);
        puffs.SizeScale = Mathf.Clamp(radius * 0.62f, 0.6f, 2.8f);
        puffs.SpeedScale = Mathf.Clamp(0.6f + (radius * 0.35f), 0.6f, 2.4f);
        puffs.LifeScale = rich.Billow ? 1.25f * rich.LifeScale : 0.6f;
        fx.Burst(VfxEmitter.Flame, puffs);

        // The leanest tier keeps the flame and the ring and does without the separate spark burst:
        // one emitter fewer on every fire hit.
        if (tier != VfxTier.Performance)
        {
            VfxBurstSpec sparks = VfxBurstSpec.At(centre, cast.Colors, density * amount);
            sparks.SpeedScale = Mathf.Clamp(0.9f + (radius * 0.3f), 0.9f, 2.4f);
            if (grounded)
            {
                sparks.Direction = Vector3.Up;
                sparks.Spread = 80f;
            }

            fx.Burst(VfxParticles.Sparks, sparks);
        }

        if (grounded)
        {
            // What is left when the fire has gone: the ground cracked and glowing. Drawn on every
            // tier (the two leanest have no scorch decals), and it outlasts everything else here.
            VfxDiscSpec cracks = VfxDiscSpec.At(floor, radius * 0.85f, cast.Colors);
            cracks.Pattern = VfxDiscPattern.Cracks;
            cracks.Life = rich.Billow ? 3.2f : 1.5f;
            cracks.Body = 0.12f;
            cracks.Rim = 0f;
            cracks.Flow = 0.1f;
            cracks.Energy = 0.9f;
            fx.Disc(cracks);
        }

        if (!rich.Billow || !fx.Full)
        {
            return;
        }

        VfxShellSpec ball = VfxShellSpec.Ball(centre, radius * 0.62f, flame, cools: true);
        ball.Life = Mathf.Clamp(0.5f + (radius * 0.06f), 0.5f, 0.8f) * rich.LifeScale;
        fx.Shell(ball);

        FireBillow(cast, centre + (Vector3.Up * (radius * 0.25f)), radius, 1.2f, rising: true);
        if (budget.SecondaryDebris)
        {
            // Smoke rolling up out of it, for a second and a half after the flame is gone.
            VfxBurstSpec smoke = VfxBurstSpec.At(
                centre + (Vector3.Up * (radius * 0.3f)), cast.Colors,
                density * budget.DebrisMultiplier * Mathf.Clamp(0.5f + (radius * 0.2f), 0.5f, 1.2f));
            smoke.Extents = Vector3.One * (radius * 0.3f);
            smoke.Direction = Vector3.Up;
            smoke.Spread = 40f;
            smoke.SpeedScale = 1.3f;
            smoke.SizeScale = Mathf.Clamp(radius * 0.5f, 0.6f, 2.2f);
            fx.Burst(VfxParticles.Smoke, smoke);

            VfxBurstSpec embers = VfxBurstSpec.At(centre, cast.Colors, density * amount);
            embers.SpeedScale = Mathf.Clamp(1.4f + (radius * 0.4f), 1.4f, 3.4f);
            embers.LifeScale = 1.7f;
            embers.GravityScale = 0.3f;
            embers.Damping = 0.8f;
            fx.Burst(VfxParticles.Embers, embers);
        }

        if (grounded)
        {
            Debris(cast, floor, radius);
            if (radius >= 2f)
            {
                SmokeColumn(cast, floor, radius * 0.8f, 1.6f);
            }

            if (budget.GroundMarks > 0)
            {
                fx.Mark(new VfxGroundMarkSpec
                {
                    Mark = VfxMark.Scorch,
                    Position = floor,
                    Size = radius * 1.9f,
                    Colors = cast.Colors,
                    Life = 8f + radius,
                    Reach = Mathf.Clamp(radius * 0.15f, 0.5f, 0.9f),
                });
            }

            if (tier >= VfxTier.High)
            {
                // Small flames left burning on the scorch.
                VfxBurstSpec burning = VfxBurstSpec.At(floor + (Vector3.Up * 0.15f), flame, density * 0.35f);
                burning.Continuous = true;
                burning.StreamSeconds = 2.2f;
                burning.Extents = new Vector3(radius * 0.4f, 0.05f, radius * 0.4f);
                burning.Direction = Vector3.Up;
                burning.Spread = 20f;
                burning.SizeScale = 0.45f;
                burning.SpeedScale = 0.6f;
                burning.LifeScale = 0.7f;
                fx.Burst(VfxEmitter.Flame, burning);
            }
        }

        if (budget.Distortion && !VfxQuality.ReducedMotion && radius >= 1.5f)
        {
            fx.Distortion(new VfxDistortionSpec
            {
                Position = centre,
                Radius = radius * 1.3f,
                Life = 0.4f,
                Strength = Mathf.Clamp(0.026f + (radius * 0.004f), 0.026f, 0.05f),
            });
        }
    }

    /// <summary>
    /// A lightning strike going off: the flash and its light, a thunderclap ring along the ground,
    /// thick forked bolts grounding into the floor around it and a shower of sparks, on every tier
    /// (two or three plain forks on the leanest). Medium and up add more forks with branches,
    /// debris, a wisp of smoke, cracks left glowing on the floor and a scorch.
    /// </summary>
    internal static void LightningBlast(in VfxCast cast, Vector3 centre, float radius, float groundY = float.NaN)
    {
        VfxSpawner fx = cast.Fx;
        VfxBudget budget = VfxQuality.Budget;
        VfxTier tier = VfxQuality.Tier;
        radius = Mathf.Clamp(radius, 0.8f, 5f);
        bool grounded = !float.IsNaN(groundY) && centre.Y - groundY <= Mathf.Max(2.5f, radius);
        float drop = grounded ? Mathf.Max(0.3f, centre.Y - groundY - 0.05f) : radius * 0.6f;
        var floor = new Vector3(centre.X, centre.Y - drop, centre.Z);

        BlastCore(cast, centre, radius * 0.4f, Mathf.Max(6f, radius * 4f));
        ThinRing(cast, floor + (Vector3.Up * 0.12f), radius * 1.2f, 0.3f, energy: 1.6f);

        int forks = tier switch
        {
            VfxTier.Performance => 2,
            VfxTier.Low => 3,
            VfxTier.Medium => 5,
            VfxTier.High => 7,
            _ => 9,
        };
        GroundForks(cast, centre, forks, radius, drop, 0.055f, budget.BoltBranches);

        VfxBurstSpec sparks = VfxBurstSpec.At(
            centre, cast.Colors, budget.ParticleMultiplier * Mathf.Clamp(0.7f + (radius * 0.3f), 0.7f, 2f));
        sparks.SpeedScale = Mathf.Clamp(1f + (radius * 0.25f), 1f, 2f);
        fx.Burst(VfxParticles.Sparks, sparks);

        // The leanest tier keeps the forks and the sparks and leaves no glowing cracks: one draw
        // fewer on every lightning blast.
        if (grounded && tier != VfxTier.Performance)
        {
            VfxDiscSpec cracks = VfxDiscSpec.At(floor, radius * 0.8f, cast.Colors);
            cracks.Pattern = VfxDiscPattern.Cracks;
            cracks.Life = budget.SecondaryDebris ? 1.8f : 1f;
            cracks.Body = 0.1f;
            cracks.Rim = 0f;
            cracks.Energy = 1.2f;
            fx.Disc(cracks);
        }

        if (!budget.SecondaryDebris || !fx.Full)
        {
            return;
        }

        if (grounded)
        {
            Debris(cast, floor, radius * 0.8f);
            if (budget.GroundMarks > 0)
            {
                fx.Mark(new VfxGroundMarkSpec
                {
                    Mark = VfxMark.Scorch,
                    Position = floor,
                    Size = radius * 1.5f,
                    Colors = cast.Colors,
                    Life = 7f,
                    Reach = 0.6f,
                });
            }
        }

        VfxBurstSpec smoke = VfxBurstSpec.At(
            floor + (Vector3.Up * 0.3f), cast.Colors, budget.ParticleMultiplier * budget.DebrisMultiplier * 0.35f);
        smoke.SizeScale = Mathf.Clamp(radius * 0.4f, 0.5f, 1.2f);
        smoke.SpeedScale = 0.6f;
        fx.Burst(VfxParticles.Smoke, smoke);

        if (tier >= VfxTier.High)
        {
            // A second, slower clap rolling out past the first.
            ThinRing(cast, floor + (Vector3.Up * 0.12f), radius * 1.7f, 0.55f, energy: 0.7f);
        }
    }

    /// <summary>The pattern a school draws on the ground under a telegraph or a zone.</summary>
    private static VfxDiscPattern SchoolPattern(DamageType school) => school switch
    {
        DamageType.Fire or DamageType.Lightning => VfxDiscPattern.Cracks,
        DamageType.Frost => VfxDiscPattern.Frost,
        DamageType.Arcane => VfxDiscPattern.Rune,
        DamageType.Nature => VfxDiscPattern.Roots,
        _ => VfxDiscPattern.None,
    };

    /// <summary>Whether a school's fire turns to smoke as it burns out.</summary>
    private static bool SchoolSmokes(DamageType school) => school is DamageType.Fire or DamageType.Necrotic;

    /// <summary>
    /// What a school's blast is made of beyond its recipe's flags. Called by <c>Blast</c> for every
    /// blast of <see cref="SignatureRadius"/> or more that has a flare; draws nothing on the leanest
    /// tier and more with each tier above it. <paramref name="body"/> is the size of the fire
    /// (already scaled for a meteor), <paramref name="radius"/> the true reach.
    /// </summary>
    private static void Signature(
        in VfxCast cast, in VfxPlan plan, Vector3 centre, Vector3 floor, float radius, float body, bool grounded)
    {
        if (!cast.Fx.Full || radius < SignatureRadius || plan.Inward)
        {
            return;
        }

        VfxRichness rich = VfxQuality.Rich;
        VfxBudget budget = VfxQuality.Budget;
        bool large = radius >= 2f;
        switch (cast.School)
        {
            case DamageType.Fire:
                FireBillow(cast, centre, body, 1f, rising: true);
                if (grounded && radius >= 1.2f)
                {
                    Debris(cast, floor, radius);
                }

                if (large && grounded)
                {
                    SmokeColumn(cast, floor, radius);
                }

                if (rich.DebrisLayers >= 2)
                {
                    // Embers that fly far and hang: the last thing left of a fireball.
                    VfxBurstSpec embers = VfxBurstSpec.At(centre, cast.Colors, budget.ParticleMultiplier * Mathf.Clamp(0.4f + (radius * 0.2f), 0.4f, 1.4f));
                    embers.SpeedScale = Mathf.Clamp(1.2f + (radius * 0.35f), 1.2f, 3.2f);
                    embers.LifeScale = 1.8f;
                    embers.GravityScale = 0.4f;
                    embers.Damping = 0.7f;
                    cast.Fx.Burst(VfxParticles.Embers, embers);
                }

                break;

            case DamageType.Frost:
                ShardBurst(cast, centre, radius, grounded);
                if (grounded)
                {
                    GroundMist(cast, floor, radius);
                    if (rich.Billow && radius >= 1.5f)
                    {
                        FrostSpread(cast, floor, radius, 0.5f + (radius * 0.08f));
                    }
                }

                break;

            case DamageType.Lightning:
                if (rich.Rays || large)
                {
                    Forks(cast, centre, Mathf.Max(1.2f, radius), grounded ? floor.Y : float.NaN, large ? 1 : 0);
                }

                if (grounded && radius >= 1.5f)
                {
                    Debris(cast, floor, radius * 0.7f);
                }

                if (large && grounded)
                {
                    SmokeColumn(cast, floor, radius * 0.6f, 0.6f);
                }

                break;

            case DamageType.Arcane:
                if (rich.Glints)
                {
                    VfxBurstSpec glints = VfxBurstSpec.At(centre, cast.Colors, budget.ParticleMultiplier * Mathf.Clamp(0.5f + (radius * 0.3f), 0.5f, 1.8f));
                    glints.Extents = Vector3.One * (radius * 0.6f);
                    glints.LifeScale = rich.LifeScale;
                    cast.Fx.Burst(VfxEmitter.Glints, glints);
                }

                if (rich.DebrisLayers >= 2 && plan.Particles != VfxParticles.Sparks && plan.Secondary != VfxParticles.Sparks)
                {
                    VfxBurstSpec sparks = VfxBurstSpec.At(centre, cast.Colors, budget.ParticleMultiplier * 0.6f);
                    sparks.SpeedScale = Mathf.Clamp(0.7f + (radius * 0.3f), 0.7f, 2.4f);
                    cast.Fx.Burst(VfxParticles.Sparks, sparks);
                }

                break;

            case DamageType.Nature:
                if (rich.Glints)
                {
                    VfxBurstSpec pollen = VfxBurstSpec.At(centre, cast.Colors, budget.ParticleMultiplier * 0.6f);
                    pollen.Extents = Vector3.One * (radius * 0.6f);
                    pollen.GravityScale = -0.6f;
                    pollen.LifeScale = rich.LifeScale;
                    cast.Fx.Burst(VfxEmitter.Glints, pollen);
                }

                if (grounded && radius >= 1.5f)
                {
                    Debris(cast, floor, radius * 0.6f);
                }

                break;

            case DamageType.Necrotic:
                FireBillow(cast, centre, body * 0.8f, 0.7f, rising: true);
                if (large && grounded)
                {
                    SmokeColumn(cast, floor, radius * 0.7f, 0.8f);
                }

                break;
        }
    }

    // --- the second kit: shapes, anchors and first-person pieces -----------------------------------

    /// <summary>Whether the camera is in the local player's own head right now (first person).</summary>
    internal static bool ViewIsFirstPerson(IEntity? caster) =>
        caster != null && IsPlayer(caster) && VfxAnchor.BodyOf(caster) is { } body && TryFirstPersonHand(body, out _);

    /// <summary>How much smaller the head of a bolt is drawn for who is watching it leave: half for
    /// the player's own bolt in first person (it flies straight down the crosshair), else full size.
    /// A <c>Projectile</c> hook that draws its own head multiplies its sizes by this.</summary>
    internal static float ProjectileViewScale(in VfxCast cast) =>
        cast.ByPlayer && ViewIsFirstPerson(cast.Caster) ? 0.5f : 1f;

    /// <summary>
    /// The upright space a body fills, read off its collision shape: <paramref name="centre"/> is
    /// the middle of it as an offset from the body's origin (so it is right for a body whose origin
    /// is at its feet and for one, like the practice dummies, whose origin is the middle of its
    /// capsule), <paramref name="size"/> its width, height and width. A body with no shape reads as
    /// a person standing on its origin. Presentation only.
    /// </summary>
    internal static void BodyFit(Node3D body, out Vector3 centre, out Vector3 size)
    {
        float foot = 0f;
        float height = 1.9f;
        float width = 0.8f;
        foreach (Node child in body.GetChildren())
        {
            if (child is not CollisionShape3D { Shape: { } shape } collider || collider.Disabled)
            {
                continue;
            }

            float tall;
            float wide;
            switch (shape)
            {
                case CapsuleShape3D capsule:
                    tall = capsule.Height;
                    wide = capsule.Radius * 2f;
                    break;
                case CylinderShape3D cylinder:
                    tall = cylinder.Height;
                    wide = cylinder.Radius * 2f;
                    break;
                case BoxShape3D box:
                    tall = box.Size.Y;
                    wide = Mathf.Max(box.Size.X, box.Size.Z);
                    break;
                case SphereShape3D sphere:
                    tall = sphere.Radius * 2f;
                    wide = tall;
                    break;
                default:
                    continue;
            }

            // A shape laid on its side (a beast's capsule along its spine) is as tall as it is thick.
            if (shape is CapsuleShape3D or CylinderShape3D && Mathf.Abs(collider.Basis.Y.Normalized().Y) < 0.7f)
            {
                (tall, wide) = (wide, tall);
            }

            if (tall > 0.2f && wide > 0.1f)
            {
                foot = collider.Position.Y - (tall * 0.5f);
                height = tall;
                width = wide;
                break;
            }
        }

        centre = new Vector3(0f, foot + (height * 0.5f), 0f);
        size = new Vector3(width, height, width);
    }

    /// <summary>
    /// The structured part of a wind-up, by school (<see cref="VfxMotifRules.Windup"/>): flames
    /// licking up off the hand, shards of ice circling it, small arcs crackling around it, leaves on
    /// a rising swirl, wisps drawn in. Follows <paramref name="hand"/>, is added to
    /// <paramref name="rig"/> (and set as its <see cref="VfxRig.Motif"/>, which
    /// <c>WindupProgress</c> fills with a held charge), and shrinks with the glow near the eye.
    /// <paramref name="glow"/> is the radius of the glow it is drawn around. Arcane has none (its
    /// glyph is its structure) and gets an empty handle. The generic wind-up already calls this.
    /// </summary>
    internal static VfxHandle<VfxMotif> WindupMotif(
        in VfxCast cast, VfxRig rig, in VfxAnchor hand, Vector3 at, float glow, float level = 1f)
    {
        VfxMotifStyle style = VfxMotifRules.Windup(cast.School);
        if (style.IsNone)
        {
            return default;
        }

        VfxMotifSpec spec = VfxMotifSpec.Of(at, cast.Colors, style, glow);
        spec.Sustain = true;
        spec.Level = level;
        VfxHandle<VfxMotif> motif = rig.Add(cast.Fx.Motif(spec));
        motif.Get?.Follow(hand);
        rig.Motif = motif;
        return motif;
    }

    /// <summary>
    /// The shaped head of a bolt, by school (<see cref="VfxMotifRules.Head"/>): a flame with licks
    /// strung out behind it, a shard of ice, a jagged bolt, an arcane lance, a cluster of thorns, a
    /// dark comet trailing wisps. Follows <paramref name="projectile"/>, settles from the hand
    /// (<paramref name="handOffset"/>) and glides like every other part of a bolt, and is added to
    /// <paramref name="rig"/>. <paramref name="size"/> is the radius of the glow it replaces the
    /// core of. The generic projectile already calls this; a <c>Projectile</c> hook that returns
    /// true calls it for a shaped head of its own school.
    /// </summary>
    internal static VfxHandle<VfxMotif> ProjectileHead(
        in VfxCast cast, VfxRig rig, Node3D projectile, Vector3 handOffset, Vector3 direction, float size)
    {
        VfxMotifStyle style = VfxMotifRules.Head(cast.School);
        if (style.IsNone || direction.LengthSquared() < 0.0001f)
        {
            return default;
        }

        VfxMotifSpec spec = VfxMotifSpec.Of(projectile.GlobalPosition + handOffset, cast.Colors, style, size);
        spec.Sustain = true;
        spec.Axis = direction.Normalized();
        VfxHandle<VfxMotif> head = rig.Add(cast.Fx.Motif(spec));
        if (head.Get is { } shaped)
        {
            shaped.Follow(VfxAnchor.To(projectile));
            shaped.SettleFrom(handOffset);
            shaped.Glide(spec.Axis * Mathf.Max(0f, cast.Spell?.ProjectileSpeed ?? 0f));
        }

        return head;
    }

    /// <summary>
    /// Arcs left crackling over <paramref name="body"/> for <paramref name="seconds"/>: what a
    /// struck or branded target wears for as long as the status lasts. It follows the body, ends
    /// by itself, and fades where it is if the body goes first. Drawn in the cast's colours (pass a
    /// lightning cast); one draw call, on every tier, with fewer arcs on the lean ones.
    /// <paramref name="size"/> is the reach of the arcs, 0 = fitted to the body.
    /// </summary>
    internal static VfxHandle<VfxMotif> ResidualCrackle(in VfxCast cast, Node3D body, float seconds, float size = 0f)
    {
        if (body == null || !GodotObject.IsInstanceValid(body) || !body.IsInsideTree() || seconds <= 0.05f)
        {
            return default;
        }

        VfxMotifSpec spec = CrackleOver(body, cast.Colors, size, out Vector3 centre);
        spec.Life = seconds;
        VfxHandle<VfxMotif> crackle = cast.Fx.Motif(spec);
        crackle.Get?.Follow(VfxAnchor.To(body, centre));
        return crackle;
    }

    /// <summary>The arcs of <see cref="ResidualCrackle"/> as a spec, for a caller with a rig of its
    /// own to hold them (the standing effect of a lightning mark). <paramref name="centre"/> is the
    /// offset from the body's origin to follow it at.</summary>
    internal static VfxMotifSpec CrackleOver(Node3D body, in VfxSchoolColors colors, float size, out Vector3 centre)
    {
        BodyFit(body, out centre, out Vector3 fit);
        float reach = size > 0f ? size : Mathf.Clamp(Mathf.Max(fit.X, fit.Y * 0.5f) * 0.75f, 0.45f, 1.6f);
        VfxMotifSpec spec = VfxMotifSpec.At(
            body.GlobalPosition + centre, colors, VfxSprite.Spark, VfxMotion.Crackle,
            VfxQuality.Tier switch { VfxTier.Performance => 3, VfxTier.Low => 4, VfxTier.Medium => 5, VfxTier.High => 6, _ => 8 },
            reach, reach * 0.9f);
        spec.Aspect = 0.24f;
        spec.Energy = 1.4f;
        spec.Pale = true;
        spec.TrueSize = true;
        return spec;
    }

    /// <summary>
    /// What a tether scatters at its two ends in place of a puff of round particles: the school's
    /// motes torn off <paramref name="from"/> (the drained) along the line, and motes drawn in to
    /// <paramref name="to"/> (the drinker) from all around it. <paramref name="amount"/> scales both.
    /// </summary>
    internal static void TetherScatter(in VfxCast cast, Vector3 from, Vector3 to, float amount = 1f)
    {
        float density = VfxQuality.Budget.ParticleMultiplier * amount;
        VfxParticles kind = cast.School == DamageType.Necrotic ? VfxParticles.Wisps : VfxParticles.Motes;
        Vector3 line = to - from;
        if (line.LengthSquared() > 0.01f)
        {
            VfxBurstSpec torn = VfxBurstSpec.At(from, cast.Colors, density * 0.5f);
            torn.Direction = line.Normalized();
            torn.Spread = 24f;
            torn.SpeedScale = 1.5f;
            torn.LifeScale = 0.7f;
            torn.GravityScale = 0f;
            cast.Fx.Burst(kind, torn);
        }

        VfxBurstSpec drawn = VfxBurstSpec.At(to, cast.Colors, density * 0.6f);
        drawn.Inward = true;
        drawn.Extents = Vector3.One * 0.7f;
        drawn.LifeScale = 0.55f;
        drawn.GravityScale = 0f;
        cast.Fx.Burst(VfxParticles.Motes, drawn);
    }

    /// <summary>Follows <paramref name="caster"/>'s mouth (a jaw, mouth or snout bone, failing that
    /// its head; failing that 1.6 m above its origin): where a breath gathers and leaves from.
    /// <see cref="VfxAnchor.HasBone"/> says whether a bone was found.</summary>
    internal static VfxAnchor MouthAnchor(IEntity caster) => VfxAnchor.ToMouth(caster, new Vector3(0f, 1.6f, 0f));

    /// <summary>
    /// The glow that gathers at a mouth (or any anchor) before a breath: a held glow with a light
    /// that builds to full over <paramref name="seconds"/>, the school's particles drawn in to it
    /// (the inhale), and the school's wind-up motif around it. Everything follows
    /// <paramref name="mouth"/> and is added to <paramref name="rig"/>, so the wind-up's own end
    /// stops it. For a <c>Windup</c> hook: build the anchor with <see cref="MouthAnchor"/>, call
    /// this, and return true.
    /// </summary>
    internal static void MouthGlow(in VfxCast cast, VfxRig rig, in VfxAnchor mouth, float size, float seconds)
    {
        if (!mouth.TryResolve(out Vector3 at))
        {
            return;
        }

        size = Mathf.Clamp(size, 0.15f, 1.2f);
        VfxFlareSpec glow = VfxFlareSpec.At(at, size, cast.Colors);
        glow.Sustain = true;
        glow.Level = 0.25f;
        glow.RampSeconds = Mathf.Max(0.05f, seconds);
        glow.Light = VfxQuality.Budget.MaxLights > 0;
        glow.LightRange = 5f;
        rig.Flare = rig.Add(cast.Fx.Flare(glow));
        rig.Flare.Get?.Follow(mouth);

        VfxBurstSpec inhale = VfxBurstSpec.At(at, cast.Colors, VfxQuality.Budget.ParticleMultiplier * 0.8f);
        inhale.Continuous = true;
        inhale.Inward = true;
        inhale.Extents = Vector3.One * (size * 3.5f);
        inhale.LifeScale = 0.5f;
        inhale.SizeScale = 0.8f;
        inhale.GravityScale = 0f;
        rig.Stream = rig.Add(cast.Fx.Burst(SpellVfxCatalog.SchoolParticles(cast.School), inhale));
        rig.Stream.Get?.Follow(mouth);
        rig.Density = VfxQuality.Budget.ParticleMultiplier * 0.8f;

        WindupMotif(cast, rig, mouth, at, size);
    }

    /// <summary>
    /// One gout of a breath driven from <paramref name="origin"/> down its cone: puffs of
    /// <paramref name="kind"/> (flame that cools to smoke, mist, ash, snow) thrown along
    /// <paramref name="axis"/> fast enough to reach <paramref name="range"/> and sized to fill the
    /// cone's width at its end. Drawn on every tier (never fewer than a handful of puffs).
    /// Returns the emitter, so a caller can <c>Follow</c> a mouth anchor with it.
    /// </summary>
    internal static VfxHandle<VfxBurst> BreathPuffs(
        in VfxCast cast, Vector3 origin, Vector3 axis, float range, float angleDegrees, VfxEmitter kind,
        float amount = 1f)
    {
        if (axis.LengthSquared() < 0.0001f || range <= 0f || kind == VfxEmitter.None)
        {
            return default;
        }

        Vector3 along = axis.Normalized();
        float half = Mathf.Clamp(angleDegrees * 0.5f, 4f, 85f);
        float widthAtEnd = range * Mathf.Tan(Mathf.DegToRad(half));
        VfxBurstPreset preset = VfxBurstPresets.For(kind);
        VfxBurstSpec puffs = VfxBurstSpec.At(
            origin + (along * 0.3f), cast.Colors, Mathf.Max(0.5f, VfxQuality.Budget.ParticleMultiplier * 1.2f * amount));
        puffs.Direction = along;
        puffs.Spread = half * 0.8f;
        puffs.LifeScale = Mathf.Clamp(0.7f / Mathf.Max(0.1f, preset.Life), 0.25f, 1f);
        puffs.Speed = range / (Mathf.Max(0.1f, preset.Life * puffs.LifeScale) * 0.75f);
        puffs.Damping = 1.2f;
        puffs.GravityScale = 0.4f;
        puffs.SizeScale = Mathf.Clamp(widthAtEnd * 0.45f / Mathf.Max(0.05f, preset.SizeMax), 0.5f, 2.4f);
        puffs.Extents = Vector3.One * 0.15f;
        return cast.Fx.Burst(kind, puffs);
    }

    /// <summary>
    /// Snow driven across a disc of <paramref name="radius"/> metres about <paramref name="centre"/>
    /// (a point on the floor) for <paramref name="seconds"/>, then stopping by itself (0 = until
    /// stopped): many small fast streaks on the slant of <paramref name="wind"/> (zero = a default
    /// slant), with fine flakes drifting among them where the tier has glints. Returns the streaks'
    /// emitter; add it to a rig and <c>Follow</c> the zone with it.
    /// </summary>
    internal static VfxHandle<VfxBurst> Snowfall(
        in VfxCast cast, Vector3 centre, float radius, float seconds = 0f, Vector3 wind = default, float amount = 1f)
    {
        radius = Mathf.Max(0.5f, radius);
        Vector3 slant = wind.LengthSquared() > 0.0001f ? wind.Normalized() : new Vector3(0.55f, -0.8f, 0.2f).Normalized();
        float density = VfxQuality.Budget.ParticleMultiplier * amount * Mathf.Clamp(radius * 0.35f, 0.5f, 2f);
        VfxBurstSpec snow = VfxBurstSpec.At(centre + (Vector3.Up * 3f) - (slant * 2f), cast.Colors, density);
        snow.Continuous = true;
        snow.StreamSeconds = seconds;
        snow.Extents = new Vector3(radius, 0.5f, radius);
        snow.Direction = slant;
        snow.Spread = 7f;
        VfxHandle<VfxBurst> streaks = cast.Fx.Burst(VfxEmitter.Snow, snow);

        if (VfxQuality.Rich.Glints && seconds > 0f)
        {
            VfxBurstSpec flurry = VfxBurstSpec.At(centre + (Vector3.Up * 1.6f), cast.Colors, density * 0.5f);
            flurry.Continuous = true;
            flurry.StreamSeconds = seconds;
            flurry.Extents = new Vector3(radius * 0.9f, 1.2f, radius * 0.9f);
            flurry.Direction = Vector3.Down;
            flurry.Spread = 40f;
            cast.Fx.Burst(VfxEmitter.Flurry, flurry);
        }

        return streaks;
    }

    /// <summary>
    /// Flakes of ash let go over <paramref name="radius"/> metres about <paramref name="centre"/>
    /// for <paramref name="seconds"/>: they tumble, drift down and hang for three or four seconds
    /// before they fade. Dark and covering; <paramref name="tint"/> (alpha above 0) recolours them.
    /// Drawn on every tier.
    /// </summary>
    internal static VfxHandle<VfxBurst> AshFall(
        in VfxCast cast, Vector3 centre, float radius, float seconds = 0.8f, Color tint = default, float amount = 1f)
    {
        radius = Mathf.Max(0.3f, radius);
        VfxBurstSpec ash = VfxBurstSpec.At(
            centre, cast.Colors, Mathf.Max(0.3f, VfxQuality.Budget.ParticleMultiplier * amount * Mathf.Clamp(radius * 0.5f, 0.5f, 2f)));
        ash.Continuous = seconds > 0f;
        ash.StreamSeconds = seconds;
        ash.Extents = new Vector3(radius, radius * 0.4f, radius);
        ash.LifeScale = 1.2f;
        ash.Tint = tint;
        return cast.Fx.Burst(VfxEmitter.AshFlake, ash);
    }

    /// <summary>
    /// What the local player sees of a spell cast on their own body in first person, where its
    /// rings and shells are at their feet or around the camera: a brief shimmer of the school's
    /// colour at the edges of the screen and (above the leanest tier) a column of the school's
    /// particles rising through the lower middle of the view. The generic <c>Release</c> already
    /// calls this for every self-cast in first person (a blink aside), before the spell's own
    /// <c>Release</c> hook runs; a hook may call it again for a stronger beat.
    /// </summary>
    internal static void SelfCastInView(in VfxCast cast, float strength = 0.6f)
    {
        if (_director is not { HasCamera: true } director)
        {
            return;
        }

        cast.Fx.ScreenEdge(SpellSchools.Color(cast.School), strength);
        if (VfxQuality.Tier == VfxTier.Performance)
        {
            return;
        }

        Vector3 ahead = director.CameraForward;
        ahead.Y = 0f;
        if (ahead.LengthSquared() < 0.0001f)
        {
            return;
        }

        Vector3 foot = director.CameraPosition + (ahead.Normalized() * 1.9f) + (Vector3.Down * 1.25f);
        VfxBurstSpec column = VfxBurstSpec.At(foot, cast.Colors, VfxQuality.Budget.ParticleMultiplier * 0.9f);
        column.Continuous = true;
        column.StreamSeconds = 0.7f;
        column.Extents = new Vector3(0.45f, 0.1f, 0.45f);
        column.Direction = Vector3.Up;
        column.Spread = 10f;
        column.SpeedScale = 1.3f;
        column.GravityScale = 0.1f;
        cast.Fx.Burst(SpellVfxCatalog.SchoolParticles(cast.School), column);
    }
}
