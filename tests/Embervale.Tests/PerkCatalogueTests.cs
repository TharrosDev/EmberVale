using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Embervale.Combat;
using Embervale.Economy;
using Embervale.Factions;
using Embervale.Progression;
using Embervale.Stats;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// The shipped perk catalogue (<c>tools/gen_perks.py</c>), read straight from the authored <c>.tres</c> files so the
/// tree rules and the balance caps fail the suite instead of a play session: tier gates, capstones, branch totals
/// against the 54-point supply, every effect kind used, locale text present, and "no stack of perks can breach the
/// combat or shop margins". <c>--validate</c> re-checks the structural half inside the engine.
/// </summary>
public class PerkCatalogueTests
{
    private sealed record Effect(PerkEffectKind Kind, string Arg, StatType Stat, ModifierType Mod, float Value);

    private sealed record CatalogPerk(PerkNode Node, int Column, int Corruption, string Description, List<Effect> Effects, List<Effect> StatEffects);

    private static readonly Lazy<List<CatalogPerk>> All = new(Load);

    private static List<PerkNode> Nodes => All.Value.Select(p => p.Node).ToList();

    private static Dictionary<string, PerkNode> NodeMap => Nodes.ToDictionary(n => n.Id);

    [Fact]
    public void Catalogue_IsAboutSixtyPerksAcrossSevenBranches()
    {
        Assert.InRange(All.Value.Count, 60, 80);
        foreach (PerkBranch branch in Enum.GetValues<PerkBranch>().Where(b => b != PerkBranch.None))
        {
            Assert.Contains(Nodes, n => n.Branch == branch);
        }

        Assert.DoesNotContain(Nodes, n => n.Branch == PerkBranch.None);
    }

    [Fact]
    public void EveryEffectKind_IsUsedByAPerk()
    {
        HashSet<PerkEffectKind> used = All.Value.SelectMany(p => p.Effects).Select(e => e.Kind).ToHashSet();
        foreach (PerkEffectKind kind in Enum.GetValues<PerkEffectKind>().Where(k => k != PerkEffectKind.None))
        {
            Assert.True(used.Contains(kind), $"no perk uses {kind}");
        }
    }

    [Fact]
    public void TierGates_MatchTheTier_AndPrerequisitesPointBackwards()
    {
        Dictionary<string, PerkNode> map = NodeMap;
        foreach (PerkNode node in Nodes)
        {
            Assert.Equal(PerkCatalogue.TierGate(node.Tier), node.BranchPointsRequired);
            Assert.Equal(node.Tier == PerkRules.MaxTier, node.IsCapstone);
            foreach (string prerequisite in node.Prerequisites)
            {
                PerkNode before = map[prerequisite];
                Assert.True(before.Branch == node.Branch && before.Tier < node.Tier, $"{node.Id} -> {prerequisite}");
            }

            if (node.IsCapstone)
            {
                Assert.NotEmpty(node.Prerequisites);
            }
        }

        Assert.Null(PerkRules.FindCycle(Nodes.ToDictionary(n => n.Id, n => (IReadOnlyList<string>)n.Prerequisites)));
        Assert.Equal(new[] { 0, 2, 5, 9, 14 }, Enumerable.Range(1, 5).Select(PerkCatalogue.TierGate));
    }

    [Fact]
    public void NoTwoPerks_ShareACell()
    {
        var cells = new HashSet<(PerkBranch, int, int)>();
        foreach (CatalogPerk perk in All.Value)
        {
            Assert.True(cells.Add((perk.Node.Branch, perk.Node.Tier, perk.Column)), perk.Node.Id);
            Assert.InRange(perk.Column, 0, PerkRules.MaxColumn);
        }
    }

    [Fact]
    public void MainBranches_TotalAboutForty_WithOneReachableCapstone()
    {
        foreach (PerkBranch branch in Enum.GetValues<PerkBranch>().Where(PerkCatalogue.IsMainBranch))
        {
            int total = PerkCatalogue.BranchTotal(Nodes, branch);
            Assert.InRange(total, PerkCatalogue.BranchTotalMin, PerkCatalogue.BranchTotalMax);
            Assert.Single(Nodes, n => n.Branch == branch && n.IsCapstone);
        }

        // A character can max one branch and a good part of another, never two: the supply makes it a choice.
        Assert.True(PerkCatalogue.BranchTotalMin * 2 > PerkCatalogue.SkillPointSupply);
        Assert.True(PerkCatalogue.BranchTotalMax <= PerkCatalogue.SkillPointSupply);
    }

    [Fact]
    public void EveryPerk_IsReachableWithinTheSupply()
    {
        Dictionary<string, PerkNode> map = NodeMap;
        foreach (PerkNode node in Nodes)
        {
            int cost = PerkCatalogue.PointsToReach(map, node.Id);
            Assert.InRange(cost, 1, PerkCatalogue.SkillPointSupply);
        }
    }

    [Fact]
    public void PointsToReach_FindsTheChain_AndRefusesAGateNothingMeets()
    {
        PerkNode Make(string id, int tier, int gate, params string[] pre) =>
            new(id, PerkBranch.Warrior, tier, 3, 1, gate, pre, false);

        var chain = new[] { Make("a", 1, 0), Make("b", 2, 2, "a"), Make("c", 3, 5, "b") }
            .ToDictionary(n => n.Id);
        // a x1 (gate 0), then filler to reach 2 branch points for b, then filler to 5 for c: 5 spent + c's own 1.
        Assert.Equal(6, PerkCatalogue.PointsToReach(chain, "c"));
        Assert.Equal(1, PerkCatalogue.PointsToReach(chain, "a"));

        var starved = new[] { Make("a", 1, 0), Make("z", 3, 99, "a") }.ToDictionary(n => n.Id);
        Assert.Equal(-1, PerkCatalogue.PointsToReach(starved, "z"));
        Assert.Equal(-1, PerkCatalogue.PointsToReach(chain, "missing"));

        var loop = new[] { Make("x", 2, 0, "y"), Make("y", 2, 0, "x") }.ToDictionary(n => n.Id);
        Assert.Equal(-1, PerkCatalogue.PointsToReach(loop, "x"));
    }

    [Fact]
    public void LegacyPerks_KeepTheirIdsAndValues()
    {
        // (id, max rank, stat, value per rank, corruption tier): these are in saves and race files.
        (string Id, int Rank, StatType Stat, float Value, int Corruption)[] legacy =
        {
            ("perk.might", 5, StatType.PhysicalPower, 2f, 0),
            ("perk.toughness", 5, StatType.Health, 15f, 0),
            ("perk.precision", 3, StatType.CritChance, 0.02f, 0),
            ("perk.endurance_training", 5, StatType.Stamina, 10f, 0),
            ("perk.warding", 5, StatType.Armor, 3f, 0),
            ("perk.ashborn_might", 3, StatType.SpellPower, 3f, 2),
        };
        foreach ((string id, int rank, StatType stat, float value, int corruption) in legacy)
        {
            CatalogPerk perk = All.Value.Single(p => p.Node.Id == id);
            Assert.Equal(rank, perk.Node.MaxRank);
            Assert.Equal(1, perk.Node.Cost);
            Assert.Equal(corruption, perk.Corruption);
            Effect own = Assert.Single(perk.StatEffects);
            Assert.Equal(stat, own.Stat);
            Assert.Equal(ModifierType.Flat, own.Mod);
            Assert.Equal(value, own.Value, 4);
        }
    }

    [Fact]
    public void OnlyAshboundPerks_AreCorruptionGated()
    {
        foreach (CatalogPerk perk in All.Value)
        {
            Assert.Equal(perk.Node.Branch == PerkBranch.Ashbound, perk.Corruption > 0);
        }
    }

    [Fact]
    public void EveryPerk_HasLocaleText_ThatMatchesTheFallbackAndIsFilledIn()
    {
        Dictionary<string, string> rows = StringsCsv.Rows();
        foreach (CatalogPerk perk in All.Value)
        {
            Assert.True(rows.TryGetValue(perk.Node.Id + ".name", out string? name) && name.Length > 0, perk.Node.Id + ".name");
            Assert.True(rows.TryGetValue(perk.Node.Id + ".desc", out string? desc) && desc.Length > 0, perk.Node.Id + ".desc");
            Assert.DoesNotContain("{", desc);
            Assert.Equal(Unquote(desc), perk.Description);
            Assert.True(perk.Effects.Count + perk.StatEffects.Count > 0, perk.Node.Id + " does nothing");
        }
    }

    [Fact]
    public void EveryPerk_StaysWithinItsCapAtFullRank()
    {
        foreach (CatalogPerk perk in All.Value)
        {
            foreach (Effect effect in perk.Effects)
            {
                float full = effect.Value * perk.Node.MaxRank;
                Assert.Equal(full, PerkEffectMath.Clamp(effect.Kind, full), 4);
            }
        }
    }

    [Fact]
    public void WholeCatalogue_StaysUnderEveryCap_SoACapIsABackstopNotATuningKnob()
    {
        // Sum of every perk at full rank, per kind and qualifier (an unqualified effect counts for all of them).
        PerkEffectTotals totals = new();
        foreach (CatalogPerk perk in All.Value)
        {
            foreach (Effect effect in perk.Effects)
            {
                totals.Add(effect.Kind, effect.Arg, effect.Value * perk.Node.MaxRank);
            }
        }

        string[] args = { "", "roll", "backstep", "Fire", "Frost", "Lightning", "Arcane", "Nature", "Necrotic" };
        foreach (PerkEffectKind kind in Enum.GetValues<PerkEffectKind>().Where(k => k != PerkEffectKind.None))
        {
            foreach (string arg in args)
            {
                float raw = totals.Get(kind, arg);
                Assert.True(Math.Abs(PerkEffectMath.Clamp(kind, raw) - raw) < 0.0001f,
                    $"{kind}/{arg} reaches {raw} across the catalogue, past its cap {PerkEffectMath.RangeOf(kind)}");
            }
        }
    }

    [Fact]
    public void CombatMath_CritStacking_NeverReachesTheClamp()
    {
        const float baseCrit = 0.08f; // PlayerAttributes.tres
        const float baseCritDamage = 1.5f;
        float crit = baseCrit + StatTotal(StatType.CritChance);
        float critDamage = baseCritDamage + StatTotal(StatType.CritDamage);

        // Every crit perk in every branch at once, plus the full spell crit bonus, still sits under the clamp.
        float spellCrit = All.Value.SelectMany(p => p.Effects.Select(e => (e, p)))
            .Where(x => x.e.Kind == PerkEffectKind.SpellCritBonus).Sum(x => x.e.Value * x.p.Node.MaxRank);
        Assert.True(CombatMath.ClampCritChance(crit + spellCrit) == crit + spellCrit, $"crit {crit + spellCrit}");
        Assert.True(CombatMath.ClampCritMultiplier(critDamage) == critDamage, $"crit damage {critDamage}");
    }

    [Fact]
    public void StaminaAndManaCosts_NeverPassTheirFloors_WithTheWholeCatalogue()
    {
        PerkEffectKind[] stamina =
        {
            PerkEffectKind.DodgeStaminaMult, PerkEffectKind.BlockStaminaMult, PerkEffectKind.ParryStaminaMult,
            PerkEffectKind.AttackStaminaMult, PerkEffectKind.BowDrawStaminaMult, PerkEffectKind.SprintStaminaMult,
        };
        foreach (PerkEffectKind kind in stamina.Append(PerkEffectKind.ManaCostMult))
        {
            float floor = kind == PerkEffectKind.ManaCostMult ? PerkEffectMath.ManaFactorFloor : PerkEffectMath.StaminaFactorFloor;
            foreach (string arg in new[] { "", "roll", "backstep" })
            {
                float total = All.Value.SelectMany(p => p.Effects.Select(e => (e, p)))
                    .Where(x => x.e.Kind == kind && (x.e.Arg.Length == 0 || x.e.Arg == arg))
                    .Sum(x => x.e.Value * x.p.Node.MaxRank);
                Assert.True(1f + total >= floor - 0.0001f, $"{kind}/{arg} factor {1f + total} is under {floor}");
                Assert.True(1f + total > 0f);
            }
        }
    }

    [Fact]
    public void ShopMargins_HoldAtTheCataloguesOwnBestPrices()
    {
        float Total(PerkEffectKind kind) => All.Value.SelectMany(p => p.Effects.Select(e => (e, p)))
            .Where(x => x.e.Kind == kind).Sum(x => x.e.Value * x.p.Node.MaxRank);

        float buy = PerkEffectMath.BuyFactor(Total(PerkEffectKind.BuyDiscount));
        float sell = PerkEffectMath.SellFactor(Total(PerkEffectKind.SellBonus));
        float service = PerkEffectMath.ServiceFactor(Total(PerkEffectKind.ServicePriceMult));

        // The validator proves its margin at the Best* constants; the catalogue must never exceed them.
        Assert.True(buy >= PerkEffectMath.BestBuyFactor - 0.0001f);
        Assert.True(sell <= PerkEffectMath.BestSellFactor + 0.0001f);
        Assert.True(service >= PerkEffectMath.BestServiceFactor - 0.0001f);
        Assert.True(PerkEffectMath.XpFactorMax >= 1f + Total(PerkEffectKind.XpGainMult) - 0.0001f);
        Assert.Equal(1, ShopPricing.ServicePrice(1, ReputationTier.Allied, service));

        // Every authored shop keeps its round-trip margin with both perk factors at the catalogue's best,
        // the same rule ContentValidator.ValidateShopTrade applies (a 1.25 margin, haggle and specialty folded in).
        string shops = Path.Combine(StringsCsv.RepositoryRoot(), "data", "shops");
        int checkedShops = 0;
        foreach (string file in Directory.GetFiles(shops, "*.tres"))
        {
            string text = File.ReadAllText(file);
            float markup = Field(text, "BuyMarkup", 1.5f);
            float fraction = Field(text, "SellFraction", 0.4f);
            bool haggles = Field(text, "HaggleChance", 0f) > 0f;
            float widestSell = ShopPricing.SellFractionFor(fraction, specialty: true, haggled: haggles, perkFactor: sell);
            float narrowestBuy = ShopPricing.MarkupFor(markup, ReputationTier.Allied, specialty: true, haggled: haggles, perkFactor: buy);
            Assert.True(widestSell * 1.25f <= narrowestBuy, $"{Path.GetFileName(file)}: sell {widestSell:0.00} vs buy {narrowestBuy:0.00}");
            checkedShops++;
        }

        Assert.True(checkedShops > 10);
    }

    // --- parsing ------------------------------------------------------------

    private static float StatTotal(StatType stat) => All.Value
        .SelectMany(p => p.StatEffects.Select(e => (e, p)))
        .Where(x => x.e.Stat == stat && x.e.Mod == ModifierType.Flat)
        .Sum(x => x.e.Value * x.p.Node.MaxRank);

    private static float Field(string text, string name, float fallback)
    {
        Match match = Regex.Match(text, @"^" + name + @" = (-?[0-9.]+)\s*$", RegexOptions.Multiline);
        return match.Success ? float.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : fallback;
    }

    private static string Unquote(string raw) =>
        raw.Length >= 2 && raw[0] == '"' && raw[^1] == '"' ? raw[1..^1].Replace("\"\"", "\"") : raw;

    private static List<CatalogPerk> Load()
    {
        string folder = Path.Combine(StringsCsv.RepositoryRoot(), "data", "perks");
        var perks = new List<CatalogPerk>();
        foreach (string file in Directory.GetFiles(folder, "*.tres").OrderBy(f => f, StringComparer.Ordinal))
        {
            Dictionary<string, Dictionary<string, string>> sections = Sections(File.ReadAllText(file));
            Dictionary<string, string> main = sections["resource"];

            var effects = new List<Effect>();
            var statEffects = new List<Effect>();
            if (main.TryGetValue("Effects", out string? list))
            {
                foreach (Match reference in Regex.Matches(list, @"SubResource\(""([^""]+)""\)"))
                {
                    Dictionary<string, string> sub = sections[reference.Groups[1].Value];
                    var effect = new Effect(
                        (PerkEffectKind)Int(sub, "Kind", 0), Text(sub, "Arg"), (StatType)Int(sub, "Stat", 0),
                        (ModifierType)Int(sub, "ModifierType", 0), Float(sub, "ValuePerRank"));
                    (effect.Kind == PerkEffectKind.None ? statEffects : effects).Add(effect);
                }
            }

            // The pre-tree perks keep their stat bonus in the resource's own Stat fields.
            if (Float(main, "ValuePerRank") != 0f)
            {
                statEffects.Add(new Effect(PerkEffectKind.None, "", (StatType)Int(main, "Stat", 0),
                    (ModifierType)Int(main, "ModifierType", 0), Float(main, "ValuePerRank")));
            }

            var prerequisites = Regex.Matches(main.GetValueOrDefault("PrerequisiteIds", "[]"), "\"([^\"]+)\"")
                .Select(m => m.Groups[1].Value).ToList();
            var node = new PerkNode(
                Text(main, "Id"), (PerkBranch)Int(main, "Branch", 0), Int(main, "Tier", 1), Int(main, "MaxRank", 1),
                Int(main, "Cost", 1), Int(main, "BranchPointsRequired", 0), prerequisites,
                main.GetValueOrDefault("IsCapstone", "false") == "true");
            perks.Add(new CatalogPerk(node, Int(main, "Column", 0), Int(main, "MinCorruptionTier", 0),
                Text(main, "Description"), effects, statEffects));
        }

        return perks;
    }

    private static Dictionary<string, Dictionary<string, string>> Sections(string text)
    {
        var sections = new Dictionary<string, Dictionary<string, string>>();
        Dictionary<string, string>? current = null;
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            Match header = Regex.Match(line, @"^\[(sub_resource|resource)(?: [^\]]*?id=""([^""]+)"")?");
            if (line.StartsWith('[') && line.Contains("ext_resource") == false && line.StartsWith("[gd_resource") == false)
            {
                current = new Dictionary<string, string>();
                sections[header.Groups[1].Value == "resource" ? "resource" : header.Groups[2].Value] = current;
            }
            else if (current != null && line.Contains(" = "))
            {
                int split = line.IndexOf(" = ", StringComparison.Ordinal);
                current[line[..split]] = line[(split + 3)..];
            }
        }

        return sections;
    }

    private static int Int(Dictionary<string, string> section, string key, int fallback) =>
        section.TryGetValue(key, out string? value) ? int.Parse(value, CultureInfo.InvariantCulture) : fallback;

    private static float Float(Dictionary<string, string> section, string key) =>
        section.TryGetValue(key, out string? value) ? float.Parse(value, CultureInfo.InvariantCulture) : 0f;

    private static string Text(Dictionary<string, string> section, string key) =>
        section.TryGetValue(key, out string? value) ? Regex.Unescape(value.Trim('"')) : string.Empty;
}
