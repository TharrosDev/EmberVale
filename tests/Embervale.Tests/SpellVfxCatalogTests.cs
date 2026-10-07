using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Embervale.Combat;
using Embervale.Magic;
using Embervale.Magic.Vfx;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Every authored spell must resolve to an effect recipe, read straight from <c>data/spells</c> so a
/// spell added there cannot ship invisible. A Godot <c>Resource</c> cannot be built here, so the
/// files are parsed as the plain text they are.
/// </summary>
public class SpellVfxCatalogTests
{
    private sealed record Spell(string Id, DamageType School, SpellDelivery Delivery);

    private static string Root()
    {
        string? directory = AppContext.BaseDirectory;
        while (directory != null && !File.Exists(Path.Combine(directory, "Embervale.sln")))
        {
            directory = Path.GetDirectoryName(directory);
        }

        return directory ?? throw new DirectoryNotFoundException("Could not find Embervale.sln");
    }

    private static List<Spell> LoadSpells()
    {
        var list = new List<Spell>();
        foreach (string file in Directory.GetFiles(Path.Combine(Root(), "data", "spells"), "*.tres"))
        {
            // The resource's own defaults, for a field the file leaves out.
            string id = string.Empty;
            int school = (int)DamageType.Fire;
            int delivery = (int)SpellDelivery.Projectile;
            bool inResource = false;
            foreach (string raw in File.ReadAllLines(file))
            {
                string line = raw.Trim();
                if (line == "[resource]")
                {
                    inResource = true;
                    continue;
                }

                int eq = line.IndexOf(" = ", StringComparison.Ordinal);
                if (!inResource || eq <= 0)
                {
                    continue;
                }

                string value = line[(eq + 3)..];
                switch (line[..eq])
                {
                    case "Id":
                        id = value.Trim('"');
                        break;
                    case "School":
                        school = int.Parse(value);
                        break;
                    case "Delivery":
                        delivery = int.Parse(value);
                        break;
                }
            }

            list.Add(new Spell(id, (DamageType)school, (SpellDelivery)delivery));
        }

        return list;
    }

    [Fact]
    public void EveryAuthoredSpellHasARecipe()
    {
        List<Spell> spells = LoadSpells();
        Assert.NotEmpty(spells); // finding no files would pass everything

        foreach (Spell spell in spells)
        {
            Assert.False(string.IsNullOrEmpty(spell.Id));
            Assert.True(Enum.IsDefined(spell.School), $"{spell.Id} has an unknown school.");
            Assert.True(Enum.IsDefined(spell.Delivery), $"{spell.Id} has an unknown delivery.");

            SpellVfxRecipe recipe = SpellVfxCatalog.For(spell.Id, spell.School, spell.Delivery);
            Assert.NotNull(recipe);
            Assert.False(
                recipe.Cast.IsEmpty && recipe.Travel.IsEmpty && recipe.Impact.IsEmpty && recipe.Linger.IsEmpty,
                $"{spell.Id} resolves to a recipe that draws nothing.");
        }
    }

    /// <summary>
    /// The fallback keeps a forgotten spell visible, which is also what would hide it from the test
    /// above. Each recipe file is all or nothing: while it is still the empty seam nothing is asked of
    /// it, and from its first recipe on every spell it owns must have one.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ARecipeFileThatHasStartedCoversEverySpellItOwns(bool arcana)
    {
        var owned = new List<string>();
        foreach (Spell spell in LoadSpells())
        {
            bool elementalSchool = spell.School is DamageType.Fire or DamageType.Frost or DamageType.Lightning;
            bool inArcana = !elementalSchool || SpellVfxCatalog.ArcanaIdsOfElementalSchools.Contains(spell.Id);
            if (inArcana == arcana)
            {
                owned.Add(spell.Id);
            }
        }

        Assert.NotEmpty(owned);
        if (!owned.Exists(SpellVfxCatalog.Has))
        {
            return; // the seam: this file has no recipes yet
        }

        Assert.All(owned, id => Assert.True(SpellVfxCatalog.Has(id), $"{id} has no recipe of its own."));
    }

    [Fact]
    public void AnAuthoredRecipeNamesASpellThatExists()
    {
        var ids = new HashSet<string>();
        foreach (Spell spell in LoadSpells())
        {
            ids.Add(spell.Id);
        }

        Assert.All(SpellVfxCatalog.AuthoredIds, id => Assert.Contains(id, ids));
    }

    [Fact]
    public void EverySchoolAndDeliveryHasAFallbackThatDrawsSomething()
    {
        foreach (DamageType school in Enum.GetValues<DamageType>())
        {
            foreach (SpellDelivery delivery in Enum.GetValues<SpellDelivery>())
            {
                SpellVfxRecipe recipe = SpellVfxCatalog.Fallback(school, delivery);

                Assert.False(recipe.Cast.IsEmpty, $"{school} {delivery} has no cast beat.");
                Assert.False(recipe.Impact.IsEmpty, $"{school} {delivery} has no impact beat.");
                Assert.Equal(VfxGroundStyle.Telegraph, recipe.Ground);
                Assert.Same(recipe, SpellVfxCatalog.Fallback(school, delivery)); // built once
            }
        }
    }

    [Fact]
    public void AnUnknownSpellGetsItsSchoolsFallback()
    {
        Assert.False(SpellVfxCatalog.Has("spell.not_a_spell"));
        Assert.Same(
            SpellVfxCatalog.Fallback(DamageType.Frost, SpellDelivery.Area),
            SpellVfxCatalog.For("spell.not_a_spell", DamageType.Frost, SpellDelivery.Area));
    }

    [Theory]
    [InlineData(DamageType.Fire, VfxParticles.Embers, VfxMark.Scorch)]
    [InlineData(DamageType.Frost, VfxParticles.Shards, VfxMark.Frost)]
    [InlineData(DamageType.Lightning, VfxParticles.Sparks, VfxMark.Scorch)]
    [InlineData(DamageType.Arcane, VfxParticles.Motes, VfxMark.Rune)]
    [InlineData(DamageType.Nature, VfxParticles.Leaves, VfxMark.Roots)]
    [InlineData(DamageType.Necrotic, VfxParticles.Wisps, VfxMark.None)]
    public void ASchoolThrowsItsOwnParticlesAndLeavesItsOwnMark(DamageType school, VfxParticles particles, VfxMark mark)
    {
        Assert.Equal(particles, SpellVfxCatalog.SchoolParticles(school));
        Assert.Equal(mark, SpellVfxCatalog.SchoolMark(school));
        Assert.Equal(particles, SpellVfxCatalog.Fallback(school, SpellDelivery.Projectile).Impact.Particles);
        Assert.Equal(mark, SpellVfxCatalog.Fallback(school, SpellDelivery.Ground).Impact.Mark);
    }

    [Fact]
    public void AStageWithNothingInItIsEmpty()
    {
        Assert.True(VfxStage.None.IsEmpty);
        Assert.True(new VfxStage().IsEmpty);
        Assert.Equal(1f, VfxStage.None.Scale);
        Assert.False(new VfxStage { Flare = true }.IsEmpty);
        Assert.False(new VfxStage { Particles = VfxParticles.Smoke }.IsEmpty);
        Assert.False(new VfxStage { Secondary = VfxParticles.Smoke }.IsEmpty);
        Assert.False(new VfxStage { Inward = true }.IsEmpty);
        Assert.False(new VfxStage { Shell = true }.IsEmpty);
        Assert.False(new VfxStage { Sigil = true }.IsEmpty);
        Assert.False(new VfxStage { Tether = true }.IsEmpty);
    }
}
