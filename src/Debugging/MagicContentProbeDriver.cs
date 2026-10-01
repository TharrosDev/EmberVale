using System.Linq;
using Embervale.Core;
using Embervale.Enemies;
using Embervale.Magic;
using Embervale.Races;
using Godot;

namespace Embervale.Debugging;

/// <summary>
/// The C# half of <c>tools/magic_content_probe.gd</c>. The spell, status and enemy databases are static
/// C# classes, which a GDScript probe cannot call, so this hands it the real data through Godot types and
/// runs the shared <see cref="SpellRouteValidator"/>. It contains no rules of its own.
/// </summary>
public partial class MagicContentProbeDriver : RefCounted
{
    public string[] Roster() => (string[])GameIds.Spells.Roster.Clone();

    public string[] Starting() => (string[])GameIds.Spells.Starting.Clone();

    public string[] PlayerLearnableIds()
    {
        var ids = new Godot.Collections.Array<string>();
        foreach (SpellResource spell in SpellDatabase.All)
        {
            if (spell.PlayerLearnable)
            {
                ids.Add(spell.Id);
            }
        }

        return [.. ids];
    }

    public int SpellCount() => SpellDatabase.All.Count;

    public SpellResource? GetSpell(string id) => SpellDatabase.Get(id);

    public bool HasSpellFile(string id) => SpellDatabase.All.Any(s => s.Id == id);

    public string Resolve(string id) => SpellAliases.Resolve(id);

    public bool StatusExists(string id) => StatusEffectDatabase.Get(id) != null;

    public StatusEffectResource? GetStatus(string id) => StatusEffectDatabase.Get(id);

    /// <summary>The routes that lead to a spell, from the same reading the validator enforces.</summary>
    public string[] RoutesOf(string spellId) =>
        SpellRouteValidator.Routes().TryGetValue(spellId, out System.Collections.Generic.List<string>? routes)
            ? [.. routes]
            : [];

    /// <summary>What the validator's roster and route rules report; empty when the data is sound.</summary>
    public string[] RuleIssues()
    {
        var issues = new System.Collections.Generic.List<string>();
        SpellRouteValidator.Validate(issues);
        return [.. issues];
    }

    /// <summary>Every raw spell id an enemy archetype, boss phase or race names, as "owner|spell" rows, so the
    /// probe can prove none of them is a retired id and each one resolves.</summary>
    public string[] LoadoutRows()
    {
        var rows = new System.Collections.Generic.List<string>();
        foreach (EnemyArchetypeResource archetype in EnemyArchetypeDatabase.All)
        {
            foreach (string id in archetype.KnownSpellIds)
            {
                rows.Add($"{archetype.Id}|{id}");
            }
        }

        foreach (BossResource boss in BossDatabase.All)
        {
            foreach (BossPhaseResource phase in boss.Phases)
            {
                foreach (string id in phase.GrantSpellIds)
                {
                    rows.Add($"{boss.Id}|{id}");
                }
            }

            foreach (string id in boss.EnrageSpellIds)
            {
                rows.Add($"{boss.Id}|{id}");
            }
        }

        foreach (RaceResource race in RaceDatabase.All)
        {
            foreach (string id in race.InnateSpellIds)
            {
                rows.Add($"{race.Id}|{id}");
            }
        }

        return [.. rows];
    }
}
