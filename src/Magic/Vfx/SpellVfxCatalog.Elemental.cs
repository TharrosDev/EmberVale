using System.Collections.Generic;

namespace Embervale.Magic.Vfx;

/// <summary>
/// The fire, frost and lightning recipes.
///
/// <para>An authored recipe is drawn exactly as written, so every flag a beat should have is set
/// here. Several of these spells also have a special case in <c>SpellVfx.Special.Elemental.cs</c>
/// that replaces one beat outright (Sunfall's meteor and blast, Frost Nova's ring of ice, Blizzard's
/// snow, the Glacial Bulwark, Storm Conduit's beam, Thunder Step's strike). Where a beat is
/// replaced, its stage here is still written out in full: it is what the spell falls back to, and
/// what the splash hits inside the blast are coloured from.</para>
///
/// <para>Sizes: a single-target impact is drawn about <c>1.05 x (0.7 + 0.6 weight + 0.5 charge) x
/// Scale</c> metres across, and from 1.2 m up (Medium tier and above) fire and frost get a body of
/// flowing noise inside the flash. Fire wants that body, so its impacts are scaled past 1.2 m;
/// frost does not (a ball of white is not ice), so its single hits stay under it.</para>
/// </summary>
public static partial class SpellVfxCatalog
{
    /// <summary>Adds each elemental spell's recipe to <paramref name="recipes"/>, keyed by spell id.
    /// Until a spell is listed here it draws its school's fallback.</summary>
    private static void RegisterElemental(Dictionary<string, SpellVfxRecipe> recipes)
    {
        RegisterFire(recipes);
        RegisterFrost(recipes);
        RegisterLightning(recipes);
    }

    private static void RegisterFire(Dictionary<string, SpellVfxRecipe> recipes)
    {
        // Emberlash: the quick one. A small, hot ball of fire that pops as a real (if small)
        // explosion, with smoke rising off where it landed.
        recipes["spell.emberlash"] = new SpellVfxRecipe(
            Cast: new VfxStage { Flare = true, Particles = VfxParticles.Embers, Scale = 1.2f },
            Travel: new VfxStage { Flare = true, Particles = VfxParticles.Embers, Scale = 1.6f },
            Impact: new VfxStage
            {
                Flare = true,
                Ring = true,
                Particles = VfxParticles.Embers,
                Secondary = VfxParticles.Smoke,
                Mark = VfxMark.Scorch,
                Scale = 1.5f,
            },
            Linger: new VfxStage { Particles = VfxParticles.Embers, Secondary = VfxParticles.Smoke, Scale = 0.8f });

        // Flame Lance: grows with the charge in the hand, flies as a long body of fire, and bursts
        // on every foe it runs through.
        recipes["spell.flame_lance"] = new SpellVfxRecipe(
            Cast: new VfxStage { Flare = true, Particles = VfxParticles.Embers, Scale = 1.15f },
            Travel: new VfxStage { Flare = true, Particles = VfxParticles.Embers, Scale = 1.3f },
            Impact: new VfxStage
            {
                Flare = true,
                Ring = true,
                Particles = VfxParticles.Embers,
                Secondary = VfxParticles.Smoke,
                Mark = VfxMark.Scorch,
                Scale = 1.3f,
            },
            Linger: new VfxStage { Particles = VfxParticles.Embers, Secondary = VfxParticles.Smoke });

        // Pyre Wall: the wall is the spell. Its linger stage is the standing sheet of fire.
        recipes["spell.pyre_wall"] = new SpellVfxRecipe(
            Cast: new VfxStage { Flare = true, Particles = VfxParticles.Embers },
            Travel: VfxStage.None,
            Impact: new VfxStage { Flare = true, Particles = VfxParticles.Embers },
            Linger: new VfxStage
            {
                Flare = true,
                Particles = VfxParticles.Embers,
                Secondary = VfxParticles.Smoke,
                Mark = VfxMark.Scorch,
            });

        // Sunfall: the biggest fire in the game. A meteor for the delay, then the blast.
        recipes["spell.sunfall"] = new SpellVfxRecipe(
            Cast: new VfxStage { Flare = true, Particles = VfxParticles.Embers, Scale = 1.4f },
            Travel: VfxStage.None,
            Impact: new VfxStage
            {
                Flare = true,
                Ring = true,
                Particles = VfxParticles.Embers,
                Secondary = VfxParticles.Smoke,
                Distortion = true,
                Mark = VfxMark.Scorch,
                ScreenFlash = true,
            },
            Linger: new VfxStage { Secondary = VfxParticles.Smoke, Mark = VfxMark.Scorch },
            Ground: VfxGroundStyle.Meteor);
    }

    private static void RegisterFrost(Dictionary<string, SpellVfxRecipe> recipes)
    {
        // Rime Shard: cold drawn into the hand, a rim-lit shard of ice in flight, and a burst of
        // splinters and frost where it lands.
        recipes["spell.rime_shard"] = new SpellVfxRecipe(
            Cast: new VfxStage { Flare = true, Particles = VfxParticles.Motes, Inward = true },
            Travel: new VfxStage { Flare = true, Particles = VfxParticles.Shards, Shell = true, Scale = 1.15f },
            Impact: new VfxStage
            {
                Flare = true,
                Particles = VfxParticles.Shards,
                Secondary = VfxParticles.Motes,
                Mark = VfxMark.Frost,
                Scale = 1.05f,
            },
            Linger: new VfxStage { Particles = VfxParticles.Motes, Scale = 0.8f });

        // Frost Nova: a ring of ice thrown out along the ground from the caster.
        recipes["spell.frost_nova"] = new SpellVfxRecipe(
            Cast: new VfxStage { Flare = true, Particles = VfxParticles.Motes, Inward = true, Scale = 1.2f },
            Travel: VfxStage.None,
            Impact: new VfxStage
            {
                Ring = true,
                Particles = VfxParticles.Shards,
                Secondary = VfxParticles.Motes,
                Mark = VfxMark.Frost,
            },
            Linger: new VfxStage { Particles = VfxParticles.Motes });

        // Blizzard: the zone is the spell. Its linger stage is the snow that falls in it.
        recipes["spell.blizzard"] = new SpellVfxRecipe(
            Cast: new VfxStage { Flare = true, Particles = VfxParticles.Motes, Inward = true, Scale = 1.3f },
            Travel: VfxStage.None,
            Impact: new VfxStage { Ring = true, Particles = VfxParticles.Shards },
            Linger: new VfxStage
            {
                Particles = VfxParticles.Motes,
                Secondary = VfxParticles.Smoke,
                Mark = VfxMark.Frost,
            });

        // Glacial Bulwark: the wall is the spell. Its linger stage is the standing ice.
        recipes["spell.glacial_bulwark"] = new SpellVfxRecipe(
            Cast: new VfxStage { Flare = true, Particles = VfxParticles.Motes, Inward = true },
            Travel: VfxStage.None,
            Impact: new VfxStage { Flare = true, Particles = VfxParticles.Shards },
            Linger: new VfxStage
            {
                Flare = true,
                Particles = VfxParticles.Motes,
                Shell = true,
                Mark = VfxMark.Frost,
            });
    }

    private static void RegisterLightning(Dictionary<string, SpellVfxRecipe> recipes)
    {
        // Ball Lightning: a slow rim-lit orb that arcs to the ground as it goes, and forks where
        // it bursts.
        recipes["spell.ball_lightning"] = new SpellVfxRecipe(
            Cast: new VfxStage { Flare = true, Particles = VfxParticles.Sparks, Scale = 1.15f },
            Travel: new VfxStage
            {
                Flare = true,
                Particles = VfxParticles.Sparks,
                Bolt = true,
                Shell = true,
                Scale = 1.5f,
            },
            Impact: new VfxStage
            {
                Flare = true,
                Ring = true,
                Particles = VfxParticles.Sparks,
                Secondary = VfxParticles.Smoke,
                Mark = VfxMark.Scorch,
                Scale = 1.25f,
            },
            Linger: VfxStage.None);

        // Storm Conduit: the beam is the spell; the stages are what crackles at its two ends.
        recipes["spell.storm_conduit"] = new SpellVfxRecipe(
            Cast: new VfxStage { Flare = true, Particles = VfxParticles.Sparks },
            Travel: new VfxStage { Flare = true, Bolt = true, Scale = 0.8f },
            Impact: new VfxStage { Flare = true, Particles = VfxParticles.Sparks },
            Linger: VfxStage.None);

        // Thunder Step: a strike along the dash, and a line of scorch where it crossed the floor.
        recipes["spell.thunder_step"] = new SpellVfxRecipe(
            Cast: new VfxStage { Flare = true, Particles = VfxParticles.Sparks },
            Travel: new VfxStage { Flare = true, Particles = VfxParticles.Sparks, Bolt = true },
            Impact: new VfxStage { Flare = true, Particles = VfxParticles.Sparks },
            Linger: new VfxStage { Mark = VfxMark.Scorch });

        // Stormbrand: a fast thin bolt that stamps its brand on what it strikes, and leaves it
        // hanging there for the arcs that follow.
        recipes["spell.stormbrand"] = new SpellVfxRecipe(
            Cast: new VfxStage { Flare = true, Particles = VfxParticles.Sparks, Scale = 0.9f },
            Travel: new VfxStage { Flare = true, Particles = VfxParticles.Sparks, Bolt = true, Scale = 0.8f },
            Impact: new VfxStage
            {
                Flare = true,
                Ring = true,
                Particles = VfxParticles.Sparks,
                Sigil = true,
                Scale = 1.1f,
            },
            Linger: new VfxStage { Sigil = true });
    }
}
