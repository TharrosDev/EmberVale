using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Embervale.Core;
using Embervale.Magic;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// The 25-spell roster read straight from the authored files, so the rule is checked on a machine with no
/// Godot: every roster id has exactly one <c>.tres</c>, sits in its school, stays inside the playbook's
/// numeric bands, references only statuses that exist, and can be reached through normal play.
/// A Godot <c>Resource</c> cannot be built here, so the files are parsed as the plain text they are.
/// </summary>
public class SpellRosterTests
{
    // School ordinals are DamageType: 1 Fire, 2 Frost, 3 Lightning, 4 Arcane, 5 Nature, 6 Necrotic.
    private static readonly int[] SchoolSizes = { 4, 4, 4, 4, 5, 4 };

    private sealed record Spell(string Id, Dictionary<string, string> Fields)
    {
        public float F(string key, float fallback = 0f) =>
            Fields.TryGetValue(key, out string? v) ? float.Parse(v, System.Globalization.CultureInfo.InvariantCulture) : fallback;

        public int I(string key, int fallback = 0) => Fields.TryGetValue(key, out string? v) ? int.Parse(v) : fallback;

        public string S(string key) => Fields.TryGetValue(key, out string? v) ? v.Trim('"') : string.Empty;
    }

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
            var fields = new Dictionary<string, string>();
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
                if (inResource && eq > 0)
                {
                    fields[line[..eq]] = line[(eq + 3)..];
                }
            }

            list.Add(new Spell(fields["Id"].Trim('"'), fields));
        }

        return list;
    }

    private static HashSet<string> StatusIds()
    {
        var ids = new HashSet<string>();
        foreach (string file in Directory.GetFiles(Path.Combine(Root(), "data", "status_effects"), "*.tres"))
        {
            Match m = Regex.Match(File.ReadAllText(file), "(?m)^Id = \"([^\"]+)\"");
            ids.Add(m.Groups[1].Value);
        }

        return ids;
    }

    private static Dictionary<string, Spell> Roster()
    {
        Dictionary<string, Spell> all = LoadSpells().ToDictionary(s => s.Id);
        return GameIds.Spells.Roster.ToDictionary(id => id, id => all[id]);
    }

    [Fact]
    public void FlameLance_AuthorsPierceGrowthAndLongerBurnAtFullCharge()
    {
        Spell spell = Roster()["spell.flame_lance"];
        Assert.Equal((int)CastMode.Charged, spell.I("CastMode"));
        Assert.True(spell.I("PierceCount") > 0);
        Assert.True(spell.I("PierceChargeBonus") > 0);
        Assert.True(spell.F("StatusDurationChargeBonus") > 0f);
    }

    [Fact]
    public void Sunfall_BreaksOnlyDirectGuardsAndKnitBoneConsumesDecay()
    {
        Dictionary<string, Spell> roster = Roster();
        Assert.Equal("true", roster["spell.sunfall"].S("Blockable"));
        Assert.Equal("true", roster["spell.sunfall"].S("DirectHitGuardBreak"));
        Assert.Equal((int)SpellDelivery.Self, roster["spell.knit_bone"].I("Delivery"));
        Assert.Equal("status.decay", roster["spell.knit_bone"].S("ConsumesStatusId"));
        Assert.True(roster["spell.knit_bone"].F("BonusPerConsumedStack") > 0f);
    }

    [Fact]
    public void Roster_HasTwentyFiveUniqueSpellsAndEachHasAFile()
    {
        Assert.Equal(25, GameIds.Spells.Roster.Length);
        Assert.Equal(25, GameIds.Spells.Roster.Distinct().Count());
        Assert.Equal(25, Roster().Count);
        Assert.Equal(LoadSpells().Count, LoadSpells().Select(s => s.Id).Distinct().Count());
    }

    [Fact]
    public void RosterIsExactlyThePlayerLearnableSpells()
    {
        HashSet<string> learnable = LoadSpells().Where(s => s.S("PlayerLearnable") != "false").Select(s => s.Id).ToHashSet();
        Assert.True(learnable.SetEquals(GameIds.Spells.Roster));
    }

    [Fact]
    public void EverySpellSitsInItsSchool()
    {
        Dictionary<string, Spell> roster = Roster();
        int index = 0;
        for (int school = 0; school < SchoolSizes.Length; school++)
        {
            for (int n = 0; n < SchoolSizes[school]; n++, index++)
            {
                string id = GameIds.Spells.Roster[index];
                Assert.True(roster[id].I("School") == school + 1, $"{id} is in school {roster[id].I("School")}, expected {school + 1}");
            }
        }
    }

    [Fact]
    public void NumbersStayInsideThePlaybookBands()
    {
        foreach ((string id, Spell spell) in Roster())
        {
            bool channelled = spell.I("CastMode") == 2;
            float mana = spell.F("ManaCost", 10f);
            Assert.True(channelled || mana is >= 8f and <= 34f, $"{id} mana {mana}");
            float cooldown = spell.F("Cooldown", 1f);
            Assert.True(cooldown is >= 0.6f and <= 16f, $"{id} cooldown {cooldown}");
            float windup = spell.F("WindupSeconds");
            Assert.True(windup is >= 0.15f and <= 0.9f, $"{id} windup {windup}");
            Assert.Equal(3, spell.I("MaxRank", 3));
        }
    }

    [Fact]
    public void CheapSpellsDoNotOutScaleExpensiveOnes()
    {
        // Direct damage per cast must not fall as mana rises (with a small allowance for the utility
        // riders), so a bolt for 8 mana never out-hits a 30 mana spell. Charged spells are compared at
        // their release, not at full charge; channels, homing orbs and health-cost spells are compared
        // on their own terms. Spells under 8 base damage carry a control rider instead.
        var direct = Roster().Values
            .Where(s => s.I("Delivery") is 0 or 4 && s.F("BaseDamage") >= 8f && s.I("CastMode") != 2 && s.F("HealthCost") == 0f
                        && s.F("HomingRange") == 0f)
            .OrderBy(s => s.F("ManaCost", 10f))
            .ToList();
        for (int i = 1; i < direct.Count; i++)
        {
            Assert.True(
                direct[i].F("BaseDamage") + 6f >= direct[i - 1].F("BaseDamage"),
                $"{direct[i].Id} ({direct[i].F("ManaCost")} mana, {direct[i].F("BaseDamage")}) hits softer than {direct[i - 1].Id}");
        }
    }

    [Fact]
    public void EveryStatusAReferencedSpellNamesExists()
    {
        HashSet<string> statuses = StatusIds();
        foreach (Spell spell in LoadSpells())
        {
            foreach (string key in new[] { "StatusEffectId", "SelfStatusEffectId", "ConsumesStatusId" })
            {
                string id = spell.S(key);
                Assert.True(id.Length == 0 || statuses.Contains(id), $"{spell.Id} {key} '{id}'");
            }
        }
    }

    [Fact]
    public void EveryAliasTargetIsARosterSpell()
    {
        foreach (string target in SpellAliases.All.Values)
        {
            Assert.Contains(target, GameIds.Spells.Roster);
        }
    }

    [Fact]
    public void NoAuthoredDataStillNamesARetiredId()
    {
        string[] retired = SpellAliases.All.Keys.ToArray();
        foreach (string folder in new[] { "enemies", "bosses", "races", "dialogue", "companions", "spells" })
        {
            foreach (string file in Directory.GetFiles(Path.Combine(Root(), "data", folder), "*.tres"))
            {
                string text = File.ReadAllText(file);
                foreach (string id in retired)
                {
                    Assert.DoesNotContain($"\"{id}\"", text);
                }
            }
        }
    }

    [Fact]
    public void EveryLoadoutSpellExists()
    {
        HashSet<string> ids = LoadSpells().Select(s => s.Id).ToHashSet();
        var pattern = new Regex("(?m)^(KnownSpellIds|GrantSpellIds|EnrageSpellIds|InnateSpellIds|BreathSpellId) = .*$");
        foreach (string folder in new[] { "enemies", "bosses", "races", "companions" })
        {
            foreach (string file in Directory.GetFiles(Path.Combine(Root(), "data", folder), "*.tres"))
            {
                foreach (Match line in pattern.Matches(File.ReadAllText(file)))
                {
                    foreach (Match id in Regex.Matches(line.Value, "\"(spell\\.[a-z_]+)\""))
                    {
                        Assert.True(ids.Contains(id.Groups[1].Value), $"{Path.GetFileName(file)} names unknown spell {id.Groups[1].Value}");
                    }
                }
            }
        }
    }

    [Fact]
    public void EveryPlayerSpellHasALearnRoute()
    {
        HashSet<string> routed = GameIds.Spells.Starting.ToHashSet();

        // A race's innate spells.
        foreach (string file in Directory.GetFiles(Path.Combine(Root(), "data", "races"), "*.tres"))
        {
            foreach (Match line in Regex.Matches(File.ReadAllText(file), "(?m)^InnateSpellIds = .*$"))
            {
                foreach (Match id in Regex.Matches(line.Value, "\"(spell\\.[a-z_]+)\""))
                {
                    routed.Add(SpellAliases.Resolve(id.Groups[1].Value));
                }
            }
        }

        // A teacher: a LearnSpell (Effect = 8) choice.
        foreach (string file in Directory.GetFiles(Path.Combine(Root(), "data", "dialogue"), "*.tres"))
        {
            foreach (Match m in Regex.Matches(File.ReadAllText(file), "(?m)^Effect = 8\\r?\\nEffectArg = \"([^\"]+)\""))
            {
                routed.Add(SpellAliases.Resolve(m.Groups[1].Value));
            }
        }

        // A tome standing in a region scene.
        foreach (string file in Directory.GetFiles(Path.Combine(Root(), "scenes", "regions"), "*.tscn", SearchOption.AllDirectories))
        {
            foreach (Match m in Regex.Matches(File.ReadAllText(file), "(?m)^SpellId = \"([^\"]+)\""))
            {
                routed.Add(SpellAliases.Resolve(m.Groups[1].Value));
            }
        }

        foreach (string id in GameIds.Spells.Roster)
        {
            Assert.True(routed.Contains(id), $"{id} has no learn route (start, race, teacher or tome)");
        }
    }
}
