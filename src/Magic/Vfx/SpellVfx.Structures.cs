using Embervale.Combat;
using Embervale.Entities;
using Godot;

namespace Embervale.Magic.Vfx;

/// <summary>
/// Things a spell stands up in the world: walls and totems. Both are gameplay objects the player has
/// to see wherever they are, so their effects are essential (never thinned, never recycled), and
/// both are positioned by their caller just after the call, so every block follows the node and is
/// built late (see <c>SpellVfx.Ground.cs</c>).
/// </summary>
public static partial class SpellVfx
{
    /// <summary>A wall began its delay. <paramref name="barrier"/> is positioned and turned just after
    /// this call, so read its transform on the next frame.</summary>
    public static void BarrierTelegraph(Node3D barrier, SpellResource spell, IEntity? caster, float width, float delay)
    {
        if (!Active || barrier == null || spell == null || !GodotObject.IsInstanceValid(barrier))
        {
            return;
        }

        ulong key = KeyOf(barrier);
        StopRig(BarrierTelegraphs, key);
        if (!Begin(spell, caster, Vector3.Zero, out VfxCast opened, essential: true, sustained: true))
        {
            return;
        }

        VfxCast cast = Late(opened);
        var rig = new VfxRig { Spell = spell };
        Prune(BarrierTelegraphs);
        BarrierTelegraphs[key] = rig;
        if (SpecialOf(spell)?.BarrierTelegraph is { } special && special(cast, rig, barrier, width, delay))
        {
            return;
        }

        // The line the wall will stand on, filling as the delay runs down.
        VfxDiscSpec line = VfxDiscSpec.At(Vector3.Zero, Mathf.Max(0.5f, width * 0.5f), cast.Colors);
        line.Life = Mathf.Max(0.1f, delay);
        line.Fills = true;
        line.Depth = Mathf.Clamp(1.2f / Mathf.Max(1f, width), 0.12f, 0.6f);
        line.Body = 0.45f;
        line.Rim = 0.6f;
        VfxHandle<VfxDisc> disc = rig.Add(cast.Fx.Disc(line));
        if (disc.Get is { } drawn)
        {
            drawn.Follow(VfxAnchor.To(barrier));
            drawn.OrientLike(barrier);
        }
    }

    /// <summary>A wall stands. Returns true when the facade drew it, in which case the barrier builds
    /// no plain face or base of its own. A wall with no delay is positioned and turned just after
    /// this call; one that had a telegraph already stands where it will.</summary>
    public static bool AttachBarrier(
        Node3D barrier, SpellResource spell, IEntity? caster, float width, float height, bool solid)
    {
        if (!Active || barrier == null || spell == null || !GodotObject.IsInstanceValid(barrier))
        {
            return false;
        }

        ulong key = KeyOf(barrier);
        StopRig(BarrierTelegraphs, key);
        StopRig(Barriers, key);
        if (!Begin(spell, caster, Vector3.Zero, out VfxCast opened, essential: true, sustained: true))
        {
            return false;
        }

        VfxCast cast = Late(opened);
        var rig = new VfxRig { Spell = spell };
        if (SpecialOf(spell)?.Barrier is { } special && special(cast, rig, barrier, width, height))
        {
            return TrackBarrier(rig, key);
        }

        width = Mathf.Max(0.5f, width);
        height = Mathf.Max(0.5f, height);
        VfxPlan plan = cast.Plan(VfxRole.Linger, width * 0.5f);
        VfxAnchor foot = VfxAnchor.To(barrier);
        VfxBudget budget = VfxQuality.Budget;

        // The wall itself. A wall that stops bodies is ice: it covers what is behind it and barely
        // moves. One that only burns is flame: added light, climbing and thinning toward its top.
        VfxShellSpec sheet = VfxShellSpec.Sheet(Vector3.Zero, width, height, cast.Colors);
        if (solid)
        {
            // Ice: translucent plates with bright seams, a lit rim and a jagged crystal crest
            // (vfx_ice), in two layers a hand apart so it has depth from the side.
            sheet = VfxShellSpec.IceWall(Vector3.Zero, width, height, cast.Colors);
        }
        else
        {
            sheet.Size = new Vector3(width, height * 1.15f, 1f);
            sheet.Energy = 1.2f;
        }

        VfxHandle<VfxShell> wall = rig.Add(cast.Fx.Shell(sheet));
        if (wall.Get is { } standing)
        {
            standing.Follow(foot);
            standing.OrientLike(barrier);
        }

        rig.Drew = wall.IsLive;
        if (!rig.Drew)
        {
            // Nothing stands: let the barrier build its plain face.
            rig.Stop();
            return false;
        }

        // What it throws off along its length.
        VfxParticles thrown = plan.Particles != VfxParticles.None
            ? plan.Particles
            : SpellVfxCatalog.SchoolParticles(cast.School);
        VfxBurstSpec along = VfxBurstSpec.At(
            Vector3.Zero, cast.Colors, Mathf.Max(plan.Density, budget.ParticleMultiplier) * Mathf.Clamp(width * 0.3f, 0.5f, 2f));
        along.Continuous = true;
        along.Extents = new Vector3(width * 0.5f, solid ? height * 0.4f : 0.2f, 0.18f);
        along.Direction = Vector3.Up;
        along.Spread = solid ? 180f : 30f;
        along.SpeedScale = solid ? 0.3f : 0.8f;
        VfxHandle<VfxBurst> stream = rig.Add(cast.Fx.Burst(thrown, along));
        if (stream.Get is { } emitter)
        {
            emitter.Follow(VfxAnchor.To(barrier, Vector3.Up * (solid ? height * 0.5f : 0.3f)));
            emitter.OrientLike(barrier);
        }

        // The burst as it rises.
        VfxBurstSpec rise = VfxBurstSpec.At(Vector3.Zero, cast.Colors, budget.ParticleMultiplier * Mathf.Clamp(width * 0.35f, 0.6f, 2f));
        rise.Extents = new Vector3(width * 0.5f, 0.2f, 0.2f);
        rise.Direction = Vector3.Up;
        rise.Spread = 35f;
        rise.SpeedScale = 1.3f;
        VfxHandle<VfxBurst> up = rig.Add(cast.Fx.Burst(solid ? VfxParticles.Shards : thrown, rise));
        if (up.Get is { } burst)
        {
            burst.Follow(VfxAnchor.To(barrier, Vector3.Up * 0.2f));
            burst.OrientLike(barrier);
        }

        if (solid && VfxQuality.Rich.Billow)
        {
            // Crystals thrown up as the ice breaks the ground, and cold mist pooling at its foot.
            VfxHandle<VfxBurst> crystals = rig.Add(cast.Fx.Burst(VfxEmitter.Crystals, rise));
            if (crystals.Get is { } thrownUp)
            {
                thrownUp.Follow(VfxAnchor.To(barrier, Vector3.Up * 0.2f));
                thrownUp.OrientLike(barrier);
            }

            VfxBurstSpec mist = VfxBurstSpec.At(Vector3.Zero, cast.Colors, budget.ParticleMultiplier * Mathf.Clamp(width * 0.2f, 0.4f, 1.4f));
            mist.Continuous = true;
            mist.Extents = new Vector3(width * 0.5f, 0.08f, 0.5f);
            mist.Direction = Vector3.Right;
            mist.Flatness = 1f;
            mist.SpeedScale = 0.5f;
            mist.SizeScale = 0.8f;
            VfxHandle<VfxBurst> pooled = rig.Add(cast.Fx.Burst(VfxEmitter.Mist, mist));
            if (pooled.Get is { } low)
            {
                low.Follow(VfxAnchor.To(barrier, Vector3.Up * 0.25f));
                low.OrientLike(barrier);
            }
        }

        // Its light, with no flare of its own to compete with the sheet.
        if (plan.Light)
        {
            VfxFlareSpec glow = VfxFlareSpec.At(Vector3.Zero, 0.5f, cast.Colors);
            glow.Sustain = true;
            glow.NoCore = true;
            glow.Level = solid ? 0.45f : 0.9f;
            glow.Light = true;
            glow.LightRange = Mathf.Max(4f, width * 1.4f);
            rig.Flare = rig.Add(cast.Fx.Flare(glow));
            rig.Flare.Get?.Follow(VfxAnchor.To(barrier, Vector3.Up * (height * 0.5f)));
        }

        if (plan.Mark != VfxMark.None)
        {
            VfxHandle<VfxGroundMark> mark = rig.Add(cast.Fx.Mark(new VfxGroundMarkSpec
            {
                Mark = plan.Mark,
                Size = width * 1.1f,
                Depth = Mathf.Clamp(1.6f / width, 0.15f, 1f),
                Colors = cast.Colors,
                Life = Mathf.Max(6f, spell.BarrierDuration + 4f),
                Reach = 1.2f,
            }));
            if (mark.Get is { } scorch)
            {
                scorch.Follow(foot);
                scorch.OrientLike(barrier);
            }
        }

        return TrackBarrier(rig, key);
    }

    /// <summary>A wall ended: <paramref name="broken"/> when its health ran out, false when it expired
    /// or was cancelled. May be called for a wall that was never attached, and more than once (the
    /// wall says it again as it leaves the tree, always unbroken); the first call is the one that counts.</summary>
    public static void BarrierEnd(Node3D barrier, SpellResource spell, float width, bool broken)
    {
        if (barrier == null || !GodotObject.IsInstanceValid(barrier))
        {
            return;
        }

        ulong key = KeyOf(barrier);
        StopRig(BarrierTelegraphs, key);
        bool stood = StopRig(Barriers, key);
        if (!stood || !broken || !barrier.IsInsideTree())
        {
            return;
        }

        // Broken down: it goes in pieces, not quietly.
        Vector3 centre = barrier.GlobalPosition + (Vector3.Up * 1.2f);
        if (!Begin(spell, caster: null, centre, out VfxCast opened))
        {
            return;
        }

        // Nobody's hit: drawn at full strength whoever raised it.
        VfxCast cast = new(
            opened.Spell, null, opened.School, true, VfxPalette.For(opened.School), opened.Recipe, opened.Authored,
            opened.Weight, opened.Fx);
        float reach = Mathf.Max(1f, width * 0.5f);
        var stage = new VfxStage
        {
            Flare = true,
            Ring = true,
            Particles = spell.BarrierBlocksBodies ? VfxParticles.Shards : SpellVfxCatalog.SchoolParticles(cast.School),
            Secondary = VfxParticles.Sparks,
        };
        Blast(cast, cast.PlanOf(stage, reach) with { Fireball = false }, centre, reach, Vector3.Zero, barrier.GlobalPosition.Y, false);
    }

    /// <summary>A totem was raised. Returns true when the facade drew it, in which case the totem
    /// builds no plain post of its own. <paramref name="totem"/> is positioned just after this call,
    /// so read its transform on the next frame.</summary>
    public static bool AttachTotem(Node3D totem, SpellResource? spell, IEntity? caster, Color tint)
    {
        if (!Active || totem == null || !GodotObject.IsInstanceValid(totem))
        {
            return false;
        }

        ulong key = KeyOf(totem);
        StopRig(Totems, key);
        if (!BeginTotem(spell, caster, Vector3.Zero, out VfxCast opened, sustained: true))
        {
            return false;
        }

        VfxCast cast = Late(opened);
        var rig = new VfxRig { Spell = spell };
        if (SpecialOf(spell)?.Totem is { } special && special(cast, rig, totem, 0f, 0f))
        {
            return TrackTotem(rig, key);
        }

        // The post: solid, in the school's colours, with a slow crawl of light over it.
        VfxShellSpec post = VfxShellSpec.Sphere(Vector3.Zero, 0.5f, cast.Colors);
        post.Shape = VfxShellShape.Post;
        post.Size = Vector3.One;
        post.Sustain = true;
        post.Occlude = 1f;
        post.Energy = 0.4f;
        post.Scroll = new Vector2(0.02f, 0.12f);
        post.Tiling = new Vector2(1f, 2f);
        VfxHandle<VfxShell> standing = rig.Add(cast.Fx.Shell(post));
        standing.Get?.Follow(VfxAnchor.To(totem));
        rig.Drew = standing.IsLive;
        if (!rig.Drew)
        {
            rig.Stop();
            return false;
        }

        VfxPlan plan = cast.Plan(VfxRole.Linger);
        VfxBudget budget = VfxQuality.Budget;

        // Its crown: the glow that says it is alive.
        VfxFlareSpec crown = VfxFlareSpec.At(Vector3.Zero, 0.26f, cast.Colors);
        crown.Sustain = true;
        crown.Level = 0.8f;
        crown.Light = budget.MaxLights > 0;
        crown.LightRange = 4.5f;
        rig.Flare = rig.Add(cast.Fx.Flare(crown));
        rig.Flare.Get?.Follow(VfxAnchor.To(totem, Vector3.Up * 1f));

        VfxParticles drifting = plan.Particles != VfxParticles.None
            ? plan.Particles
            : SpellVfxCatalog.SchoolParticles(cast.School);
        VfxBurstSpec motes = VfxBurstSpec.At(Vector3.Zero, cast.Colors, budget.ParticleMultiplier * 0.45f);
        motes.Continuous = true;
        motes.Extents = new Vector3(0.55f, 0.5f, 0.55f);
        motes.SpeedScale = 0.4f;
        motes.GravityScale = 0.3f;
        rig.Stream = rig.Add(cast.Fx.Burst(drifting, motes));
        rig.Stream.Get?.Follow(VfxAnchor.To(totem, Vector3.Up * 0.7f));

        VfxDiscSpec circle = VfxDiscSpec.At(Vector3.Zero, 0.95f, cast.Colors);
        circle.Sustain = true;
        circle.Rune = true;
        circle.Spin = 0.5f;
        circle.Body = 0.3f;
        rig.Add(cast.Fx.Disc(circle)).Get?.Follow(VfxAnchor.To(totem));

        return TrackTotem(rig, key);
    }

    /// <summary>A totem healed <paramref name="target"/> (its owner's body) this tick.</summary>
    public static void TotemPulse(Node3D totem, SpellResource? spell, Node3D? target)
    {
        if (!Active || totem == null || !GodotObject.IsInstanceValid(totem) || !totem.IsInsideTree())
        {
            return;
        }

        Vector3 foot = totem.GlobalPosition;
        if (!BeginTotem(spell, null, foot, out VfxCast cast, sustained: false))
        {
            return;
        }

        // A ring out along the ground from the post.
        VfxFlareSpec ring = VfxFlareSpec.At(foot + (Vector3.Up * 0.12f), 0.3f, cast.Colors);
        ring.NoCore = true;
        ring.Ring = true;
        ring.RingRadius = 1.8f;
        ring.Life = 0.6f;
        cast.Fx.Flare(ring);
        VfxFlareSpec beat = VfxFlareSpec.At(foot + (Vector3.Up * 1f), 0.4f, cast.Colors);
        beat.Life = 0.3f;
        cast.Fx.Flare(beat);

        if (target == null || !GodotObject.IsInstanceValid(target) || !target.IsInsideTree())
        {
            return;
        }

        // The heal itself: a line from the crown to whoever it mends, and a few leaves where it lands.
        Vector3 chest = target.GlobalPosition + (Vector3.Up * 1.1f);
        VfxHandle<VfxBolt> line = cast.Fx.Bolt(new VfxBoltSpec
        {
            Mode = VfxBoltMode.Tether,
            From = foot + (Vector3.Up * 1f),
            To = chest,
            Colors = cast.Colors,
            Width = 0.055f,
            Segments = VfxRecipeRules.BoltSegments(VfxQuality.Budget, foot.DistanceTo(chest)),
            Seed = _director!.NextSeed(),
        });
        line.Get?.FollowTo(VfxAnchor.To(target, Vector3.Up * 1.1f));

        VfxBurstSpec mend = VfxBurstSpec.At(chest, cast.Colors, VfxQuality.Budget.ParticleMultiplier * 0.4f);
        mend.SpeedScale = 0.6f;
        cast.Fx.Burst(SpellVfxCatalog.SchoolParticles(cast.School), mend);
    }

    /// <summary>A totem ended: destroyed, expired or cancelled. May be called more than once (again,
    /// unbroken, as it leaves the tree); the first call is the one that counts.</summary>
    public static void TotemEnd(Node3D totem, SpellResource? spell, bool broken)
    {
        if (totem == null || !GodotObject.IsInstanceValid(totem))
        {
            return;
        }

        bool stood = StopRig(Totems, KeyOf(totem));
        if (!stood || !totem.IsInsideTree())
        {
            return;
        }

        Vector3 centre = totem.GlobalPosition + (Vector3.Up * 0.6f);
        if (!BeginTotem(spell, null, centre, out VfxCast cast, sustained: false))
        {
            return;
        }

        // Smashed, it goes in splinters; run out, it lets go of its light.
        var stage = new VfxStage
        {
            Flare = true,
            Ring = broken,
            Particles = broken ? VfxParticles.Shards : SpellVfxCatalog.SchoolParticles(cast.School),
        };
        Blast(cast, cast.PlanOf(stage) with { Fireball = false }, centre, broken ? 1.1f : 0.6f, Vector3.Zero, totem.GlobalPosition.Y, false);
    }

    /// <summary>Opens an effect for a totem, whose spell may be unknown (then it is a nature totem).
    /// A standing totem is essential; its pulses and its end are ordinary effects.</summary>
    private static bool BeginTotem(SpellResource? spell, IEntity? caster, Vector3 at, out VfxCast cast, bool sustained)
    {
        if (spell != null)
        {
            return Begin(spell, caster, at, out cast, essential: sustained, sustained: sustained);
        }

        cast = default;
        if (!Active)
        {
            return false;
        }

        bool byPlayer = IsPlayer(caster);
        VfxSpawner fx = _director!.Open(at, byPlayer, essential: sustained, sustained: sustained);
        if (fx.IsNone)
        {
            return false;
        }

        cast = new VfxCast(
            null, caster, DamageType.Nature, byPlayer, VfxPalette.For(DamageType.Nature, byPlayer),
            SpellVfxCatalog.Fallback(DamageType.Nature, SpellDelivery.Self), false, 0.5f, fx);
        return true;
    }

    private static bool TrackBarrier(VfxRig rig, ulong key)
    {
        if (!rig.AnyLive)
        {
            return false;
        }

        Prune(Barriers);
        Barriers[key] = rig;
        return rig.Drew;
    }

    private static bool TrackTotem(VfxRig rig, ulong key)
    {
        if (!rig.AnyLive)
        {
            return false;
        }

        Prune(Totems);
        Totems[key] = rig;
        return rig.Drew;
    }
}
