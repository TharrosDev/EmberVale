using Embervale.Combat;
using Embervale.Entities;
using Godot;

namespace Embervale.Magic.Vfx;

/// <summary>Spells that move the caster: a dash along the ground, a blink across it.</summary>
public static partial class SpellVfx
{
    /// <summary>The caster dashed along the ground from <paramref name="from"/> to <paramref name="to"/>
    /// (both at the feet).</summary>
    public static void Dash(SpellResource spell, IEntity? caster, Vector3 from, Vector3 to)
    {
        if (!Begin(spell, caster, (from + to) * 0.5f, out VfxCast cast))
        {
            return;
        }

        if (SpecialOf(spell)?.Dash is { } special && special(cast, from, to))
        {
            return;
        }

        VfxBudget budget = VfxQuality.Budget;
        VfxPlan plan = cast.Plan(VfxRole.Travel);
        Vector3 start = from + Vector3.Up;
        Vector3 end = to + Vector3.Up;
        float length = start.DistanceTo(end);

        // The line travelled: lightning for a school that has it, a hot streak for one that does not.
        bool strike = plan.Bolt || cast.School == DamageType.Lightning;
        cast.Fx.Bolt(new VfxBoltSpec
        {
            Mode = strike ? VfxBoltMode.Lightning : VfxBoltMode.Tether,
            From = start,
            To = end,
            Colors = cast.Colors,
            Width = strike ? 0.1f : 0.22f,
            Jitter = strike ? 0.07f : 0.012f,
            Segments = VfxRecipeRules.BoltSegments(budget, length),
            Branches = strike ? budget.BoltBranches : 0,
            Life = strike ? 0.26f : 0.32f,
            Seed = _director!.NextSeed(),
        });

        // Afterimages along it, each a little later than the last, and what the school throws.
        int ghosts = cast.Fx.Full ? Mathf.Clamp(Mathf.CeilToInt(length / 2.2f), 1, 6) : 0;
        for (int i = 0; i < ghosts; i++)
        {
            float along = (i + 0.5f) / ghosts;
            VfxFlareSpec ghost = VfxFlareSpec.At(start.Lerp(end, along), 0.55f, cast.Colors.Scaled(0.6f, 0.9f));
            ghost.Life = 0.22f + (0.16f * along);
            cast.Fx.Flare(ghost);
        }

        if (plan.Particles != VfxParticles.None)
        {
            VfxBurstSpec wake = VfxBurstSpec.At((start + end) * 0.5f, cast.Colors, plan.Density * Mathf.Clamp(length * 0.2f, 0.5f, 1.8f));
            Vector3 line = end - start;
            wake.Extents = new Vector3(
                Mathf.Max(0.2f, Mathf.Abs(line.X) * 0.5f), 0.5f, Mathf.Max(0.2f, Mathf.Abs(line.Z) * 0.5f));
            wake.SpeedScale = 0.6f;
            cast.Fx.Burst(plan.Particles, wake);
        }

        // Where it stops.
        VfxFlareSpec arrive = VfxFlareSpec.At(end, 0.7f, cast.Colors);
        arrive.Ring = true;
        arrive.RingRadius = 1.4f;
        arrive.Light = plan.Light;
        arrive.LightRange = 5f;
        cast.Fx.Flare(arrive);
        VfxFlareSpec leave = VfxFlareSpec.At(start, 0.5f, cast.Colors);
        leave.Life = 0.22f;
        cast.Fx.Flare(leave);

        // A line of marks on the floor it crossed.
        VfxMark mark = cast.Authored ? cast.PlanOf(cast.Recipe.Linger).Mark : SpellVfxCatalog.SchoolMark(cast.School);
        if (mark != VfxMark.None && budget.GroundMarks > 0)
        {
            int marks = Mathf.Clamp(Mathf.CeilToInt(length / 2.6f), 1, Mathf.Max(1, budget.GroundMarks / 3));
            for (int i = 0; i < marks; i++)
            {
                cast.Fx.Mark(new VfxGroundMarkSpec
                {
                    Mark = mark,
                    Position = from.Lerp(to, (i + 0.5f) / marks),
                    Size = 1.5f,
                    Colors = cast.Colors,
                    Life = 6f,
                    Reach = 1.2f,
                });
            }
        }
    }

    /// <summary>The caster teleported from <paramref name="from"/> to <paramref name="to"/> (both at
    /// the feet).</summary>
    public static void Blink(SpellResource spell, IEntity? caster, Vector3 from, Vector3 to)
    {
        if (!Begin(spell, caster, to, out VfxCast cast))
        {
            return;
        }

        if (SpecialOf(spell)?.Blink is { } special && special(cast, from, to))
        {
            return;
        }

        VfxBudget budget = VfxQuality.Budget;
        Vector3 gone = from + Vector3.Up;
        Vector3 here = to + Vector3.Up;
        VfxParticles thrown = SpellVfxCatalog.SchoolParticles(cast.School);

        // Where they were: the air folds in on the gap.
        VfxFlareSpec vanish = VfxFlareSpec.At(gone, 0.75f, cast.Colors);
        vanish.Ring = true;
        vanish.RingRadius = 1.3f;
        vanish.Inward = true;
        cast.Fx.Flare(vanish);
        VfxBurstSpec drawn = VfxBurstSpec.At(gone, cast.Colors, budget.ParticleMultiplier * 0.8f);
        drawn.Inward = true;
        drawn.Extents = Vector3.One * 1.1f;
        drawn.LifeScale = 0.45f;
        cast.Fx.Burst(thrown, drawn);
        cast.Fx.Distortion(new VfxDistortionSpec { Position = gone, Radius = 1.4f, Life = 0.35f, Inward = true });

        // The streak between.
        cast.Fx.Bolt(new VfxBoltSpec
        {
            Mode = VfxBoltMode.Tether,
            From = gone,
            To = here,
            Colors = cast.Colors,
            Width = 0.13f,
            Jitter = 0.01f,
            Segments = 4,
            Life = 0.26f,
            Seed = _director!.NextSeed(),
        });

        // Where they are: it bursts back out.
        VfxFlareSpec appear = VfxFlareSpec.At(here, 0.85f, cast.Colors);
        appear.Ring = true;
        appear.RingRadius = 1.5f;
        appear.Light = budget.MaxLights > 0;
        appear.LightRange = 5f;
        cast.Fx.Flare(appear);
        VfxBurstSpec thrownOut = VfxBurstSpec.At(here, cast.Colors, budget.ParticleMultiplier * 0.8f);
        thrownOut.SpeedScale = 1.2f;
        cast.Fx.Burst(thrown, thrownOut);
        cast.Fx.Distortion(new VfxDistortionSpec { Position = here, Radius = 1.5f, Life = 0.4f });
    }
}
