using System.Collections.Generic;

namespace Embervale.Magic.Vfx;

/// <summary>The arcane, nature and necrotic recipes, and the enemy-only spells.</summary>
public static partial class SpellVfxCatalog
{
    /// <summary>
    /// Adds each of these spells' recipes to <paramref name="recipes"/>, keyed by spell id.
    ///
    /// <para>None of them asks for a screen flash: the heaviest thing here is a breath or a pull, and
    /// a tinted frame reads as mud, not as magic. Smoke is always a <em>secondary</em> layer, so the
    /// lean tiers never pay for it. Where a beat is large (a ground burst, a breath, a spell on
    /// oneself) the stage below is what the beat is made of, and a special in
    /// <c>SpellVfx.Special.Arcana.cs</c> lays those blocks out by hand instead of scaling one flare
    /// up to the radius.</para>
    /// </summary>
    private static void RegisterArcana(Dictionary<string, SpellVfxRecipe> recipes)
    {
        // --- arcane ------------------------------------------------------------------------------

        // A silver lance: a rune ring gathers at the hand, the bolt is a rim-lit needle, and where it
        // lands the air folds in on the hit and a glyph breaks.
        recipes["spell.null_lance"] = new SpellVfxRecipe(
            Cast: new VfxStage { Flare = true, Particles = VfxParticles.Motes, Inward = true, Sigil = true, Scale = 0.8f },
            Travel: new VfxStage { Flare = true, Shell = true, Particles = VfxParticles.Motes },
            Impact: new VfxStage
            {
                Flare = true,
                Ring = true,
                Inward = true,
                Particles = VfxParticles.Motes,
                Secondary = VfxParticles.Sparks,
                Distortion = true,
                Sigil = true,
                Scale = 0.85f,
            },
            Linger: VfxStage.None);

        // A ward closing over the caster: shell, rune circle underfoot, motes drawn in.
        recipes["spell.arcane_shield"] = new SpellVfxRecipe(
            Cast: new VfxStage { Flare = true, Particles = VfxParticles.Motes, Inward = true, Sigil = true },
            Travel: VfxStage.None,
            Impact: new VfxStage { Flare = true, Ring = true, Shell = true, Sigil = true, Particles = VfxParticles.Motes },
            Linger: new VfxStage { Shell = true });

        // The air folds shut where the caster was and bursts open where they are.
        recipes["spell.blink"] = new SpellVfxRecipe(
            Cast: new VfxStage { Flare = true, Particles = VfxParticles.Motes, Inward = true, Scale = 0.8f },
            Travel: VfxStage.None,
            Impact: new VfxStage
            {
                Flare = true,
                Ring = true,
                Particles = VfxParticles.Motes,
                Secondary = VfxParticles.Sparks,
                Distortion = true,
            },
            Linger: VfxStage.None);

        // Everything in the circle is drawn to one point: closing rings, streaks falling inward, a
        // small hot heart, and a rune circle left turning on the floor.
        recipes["spell.gravity_well"] = new SpellVfxRecipe(
            Cast: new VfxStage { Flare = true, Particles = VfxParticles.Motes, Inward = true, Sigil = true, Scale = 0.8f },
            Travel: VfxStage.None,
            Impact: new VfxStage
            {
                Flare = true,
                Ring = true,
                Inward = true,
                Particles = VfxParticles.Motes,
                Secondary = VfxParticles.Sparks,
                Distortion = true,
                Mark = VfxMark.Rune,
            },
            Linger: new VfxStage { Particles = VfxParticles.Motes, Inward = true, Sigil = true, Mark = VfxMark.Rune });

        // --- nature ------------------------------------------------------------------------------

        // Leaves and light climb the caster and a bloom opens on the ground under them.
        recipes["spell.mending_bloom"] = new SpellVfxRecipe(
            Cast: new VfxStage { Flare = true, Particles = VfxParticles.Motes },
            Travel: VfxStage.None,
            Impact: new VfxStage { Flare = true, Ring = true, Particles = VfxParticles.Leaves, Secondary = VfxParticles.Motes },
            Linger: new VfxStage { Flare = true, Particles = VfxParticles.Motes });

        // The cast is quiet: the totem sprouting is the picture, and it stands in drifting leaves.
        recipes["spell.lifebloom_totem"] = new SpellVfxRecipe(
            Cast: new VfxStage { Flare = true, Particles = VfxParticles.Motes },
            Travel: VfxStage.None,
            Impact: new VfxStage { Flare = true, Ring = true, Particles = VfxParticles.Leaves },
            Linger: new VfxStage { Flare = true, Particles = VfxParticles.Leaves });

        // Roots crack the circle for the delay, then thorns drive up out of all of it.
        recipes["spell.thornsnare"] = new SpellVfxRecipe(
            Cast: new VfxStage { Flare = true, Particles = VfxParticles.Leaves, Mark = VfxMark.Roots },
            Travel: VfxStage.None,
            Impact: new VfxStage
            {
                Flare = true,
                Ring = true,
                Particles = VfxParticles.Shards,
                Secondary = VfxParticles.Leaves,
                Mark = VfxMark.Roots,
            },
            Linger: new VfxStage { Particles = VfxParticles.Leaves, Mark = VfxMark.Roots });

        // Not a bolt: a darting cluster of specks with a faint heart, which bursts over what it hits.
        recipes["spell.stinging_swarm"] = new SpellVfxRecipe(
            Cast: new VfxStage { Flare = true, Particles = VfxParticles.Motes, Scale = 0.75f },
            Travel: new VfxStage { Flare = true, Particles = VfxParticles.Motes },
            Impact: new VfxStage { Flare = true, Particles = VfxParticles.Motes, Secondary = VfxParticles.Leaves, Scale = 0.7f },
            Linger: new VfxStage { Particles = VfxParticles.Motes, Scale = 0.7f });

        // Bands of bark close up the body from the shins to the chest, in a shower of chips.
        recipes["spell.barkskin"] = new SpellVfxRecipe(
            Cast: new VfxStage { Flare = true, Particles = VfxParticles.Leaves, Inward = true },
            Travel: VfxStage.None,
            Impact: new VfxStage
            {
                Flare = true,
                Ring = true,
                Inward = true,
                Particles = VfxParticles.Leaves,
                Secondary = VfxParticles.Motes,
                Mark = VfxMark.Roots,
            },
            Linger: new VfxStage { Particles = VfxParticles.Motes });

        // --- necrotic ----------------------------------------------------------------------------

        // A smouldering dark bolt; what it takes runs back up a tether to the caster (the tether is
        // drawn from the lifesteal's own arc, so the impact does not ask for a second one).
        recipes["spell.ember_siphon"] = new SpellVfxRecipe(
            Cast: new VfxStage { Flare = true, Particles = VfxParticles.Embers, Scale = 0.9f },
            Travel: new VfxStage { Flare = true, Particles = VfxParticles.Embers, Secondary = VfxParticles.Smoke },
            Impact: new VfxStage { Flare = true, Particles = VfxParticles.Embers, Secondary = VfxParticles.Smoke, Scale = 0.85f },
            Linger: VfxStage.None);

        // Paid for in blood: red leaves the caster, the bolt is heavy and trails smoke, and it lands
        // hard. A kill sends a wisp of mana home.
        recipes["spell.soul_tithe"] = new SpellVfxRecipe(
            Cast: new VfxStage { Flare = true, Particles = VfxParticles.Wisps, Scale = 0.85f },
            Travel: new VfxStage { Flare = true, Particles = VfxParticles.Wisps, Secondary = VfxParticles.Smoke },
            Impact: new VfxStage
            {
                Flare = true,
                Ring = true,
                Particles = VfxParticles.Wisps,
                Secondary = VfxParticles.Smoke,
                Scale = 1f,
            },
            Linger: new VfxStage { Secondary = VfxParticles.Smoke, Scale = 0.6f });

        // Bone-white light drawn in and stitched shut, the rot falling away.
        recipes["spell.knit_bone"] = new SpellVfxRecipe(
            Cast: new VfxStage { Flare = true, Particles = VfxParticles.Motes, Inward = true },
            Travel: VfxStage.None,
            Impact: new VfxStage { Flare = true, Ring = true, Inward = true, Particles = VfxParticles.Motes, Secondary = VfxParticles.Sparks },
            Linger: new VfxStage { Flare = true });

        // A sigil at the hand, a quick dim bolt, and the same sigil stamped on the target and left
        // hanging over it.
        recipes["spell.grave_mark"] = new SpellVfxRecipe(
            Cast: new VfxStage { Flare = true, Sigil = true, Particles = VfxParticles.Wisps, Scale = 0.8f },
            Travel: new VfxStage { Flare = true, Scale = 0.7f },
            Impact: new VfxStage { Flare = true, Ring = true, Sigil = true, Particles = VfxParticles.Wisps, Scale = 0.8f },
            Linger: new VfxStage { Sigil = true });

        // --- enemy-only --------------------------------------------------------------------------

        // The three breaths and the word are drawn tick by tick by one routine (ArcanaBreath): a
        // spray down the wedge, haze behind it, a small hot mouth, and never a flare the width of
        // the cone.
        recipes["spell.ash_breath"] = new SpellVfxRecipe(
            Cast: new VfxStage { Flare = true, Particles = VfxParticles.Embers },
            Travel: new VfxStage { Flare = true, Particles = VfxParticles.Embers, Secondary = VfxParticles.Smoke },
            Impact: new VfxStage { Flare = true, Particles = VfxParticles.Embers },
            Linger: new VfxStage { Secondary = VfxParticles.Smoke });

        recipes["spell.dragon_breath"] = new SpellVfxRecipe(
            Cast: new VfxStage { Flare = true, Particles = VfxParticles.Embers, Scale = 1.2f },
            Travel: new VfxStage
            {
                Flare = true,
                Particles = VfxParticles.Embers,
                Secondary = VfxParticles.Smoke,
                Distortion = true,
            },
            Impact: new VfxStage { Flare = true, Particles = VfxParticles.Embers },
            Linger: new VfxStage { Mark = VfxMark.Scorch });

        recipes["spell.drake_breath"] = new SpellVfxRecipe(
            Cast: new VfxStage { Flare = true, Particles = VfxParticles.Motes },
            Travel: new VfxStage { Flare = true, Particles = VfxParticles.Shards, Secondary = VfxParticles.Motes },
            Impact: new VfxStage { Flare = true, Particles = VfxParticles.Shards },
            Linger: new VfxStage { Mark = VfxMark.Frost });

        recipes["spell.elder_word"] = new SpellVfxRecipe(
            Cast: new VfxStage { Flare = true, Sigil = true, Particles = VfxParticles.Motes },
            Travel: new VfxStage { Ring = true, Particles = VfxParticles.Motes, Secondary = VfxParticles.Sparks },
            Impact: new VfxStage { Flare = true, Sigil = true, Particles = VfxParticles.Motes },
            Linger: VfxStage.None);

        // A slow dark bolt that smokes as it comes and leaves rot hanging on what it hits.
        recipes["spell.wither"] = new SpellVfxRecipe(
            Cast: new VfxStage { Flare = true, Particles = VfxParticles.Wisps },
            Travel: new VfxStage { Flare = true, Particles = VfxParticles.Wisps, Secondary = VfxParticles.Smoke, Scale = 0.9f },
            Impact: new VfxStage { Flare = true, Particles = VfxParticles.Wisps, Secondary = VfxParticles.Smoke, Scale = 0.8f },
            Linger: new VfxStage { Particles = VfxParticles.Wisps, Scale = 0.5f });
    }
}
