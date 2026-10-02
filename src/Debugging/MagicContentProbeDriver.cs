using System.Linq;
using Embervale.Core;
using Embervale.Core.Events;
using Embervale.Dialogue;
using Embervale.Entities;
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

    /// <summary>Exercises the four acquisitions from the exact prepared scenes production streaming
    /// loads. The learner is a live actor; the off-tree cell supplies the authored tome and gate.</summary>
    public string[] PreparedTomeIssues(Node learner)
    {
        var issues = new System.Collections.Generic.List<string>();
        if (learner is not IEntity entity || entity.GetComponent<SpellcastingComponent>() is not { } casting)
        {
            return ["prepared tome probe needs a live spellcasting entity"];
        }

        var acquisitions = new[]
        {
            ("ravenspur", "spell.blizzard"),
            ("stormbound_vale", "spell.ball_lightning"),
            ("stormfall", "spell.stormbrand"),
            ("stormcrown", "spell.storm_conduit"),
        };
        foreach ((string cell, string spellId) in acquisitions)
        {
            string path = $"res://data/world_bake/cells/region_frostfang_reach/frostfang_reach_{cell}.scn";
            PackedScene? packed = ResidentResources.Load<PackedScene>(path);
            if (packed == null)
            {
                issues.Add($"missing prepared acquisition scene: {path}");
                continue;
            }

            Node instance = packed.Instantiate();
            try
            {
                SpellTomeComponent? tome = FindTome(instance, spellId);
                if (tome == null)
                {
                    issues.Add($"prepared cell '{cell}' has no tome for '{spellId}'");
                    continue;
                }

                if (tome.RequiredFlagId.Length > 0)
                {
                    if (tome.Interact(entity))
                    {
                        issues.Add($"prepared tome '{spellId}' ignored its required story flag");
                    }
                    entity.GetComponent<StoryFlagsComponent>()?.Set(tome.RequiredFlagId);
                }

                int learned = 0;
                void Count(SpellLearnedEvent e)
                {
                    if (ReferenceEquals(e.Caster, entity) && e.SpellId == spellId && e.Route == LearnRoutes.Tome)
                    {
                        learned++;
                    }
                }
                EventBus.Instance.Subscribe<SpellLearnedEvent>(Count);
                try
                {
                    bool acquired = tome.Interact(entity);
                    bool repeated = tome.Interact(entity);
                    if (!acquired || repeated || learned != 1 ||
                        SpellDatabase.Get(spellId) is not { } spell || !casting.IsKnown(spell))
                    {
                        issues.Add($"prepared tome '{spellId}' did not teach exactly once through interaction");
                    }
                }
                finally
                {
                    EventBus.Instance.Unsubscribe<SpellLearnedEvent>(Count);
                }
            }
            finally
            {
                instance.Free();
            }
        }

        return [.. issues];
    }

    private static SpellTomeComponent? FindTome(Node node, string spellId)
    {
        if (node is SpellTomeComponent tome && tome.SpellId == spellId)
        {
            return tome;
        }
        foreach (Node child in node.GetChildren())
        {
            if (FindTome(child, spellId) is { } found)
            {
                return found;
            }
        }
        return null;
    }

    public string[] NumericGuardIssues()
    {
        var failures = new System.Collections.Generic.List<string>();
        using var spell = new SpellResource { Id = "spell.numeric_fixture" };
        foreach (float value in new[] { -0.01f, 1.01f, float.NaN, float.PositiveInfinity })
        {
            spell.ImpactWeight = value;
            var issues = new System.Collections.Generic.List<string>();
            SpellRouteValidator.ValidateNumbers(spell, issues);
            if (issues.Count == 0)
            {
                failures.Add($"validator accepted invalid ImpactWeight {value}");
            }
        }
        spell.ImpactWeight = 0.5f;
        var fields = new (string Name, System.Action<float> Set)[]
        {
            (nameof(spell.WindupSeconds), v => spell.WindupSeconds = v),
            (nameof(spell.RecoverySeconds), v => spell.RecoverySeconds = v),
            (nameof(spell.PoiseDamage), v => spell.PoiseDamage = v),
            (nameof(spell.GroundDelay), v => spell.GroundDelay = v),
            (nameof(spell.PullStrength), v => spell.PullStrength = v),
            (nameof(spell.BarrierDuration), v => spell.BarrierDuration = v),
            (nameof(spell.BarrierHealth), v => spell.BarrierHealth = v),
            (nameof(spell.DashDistance), v => spell.DashDistance = v),
            (nameof(spell.HealthCost), v => spell.HealthCost = v),
            (nameof(spell.BonusPerConsumedStack), v => spell.BonusPerConsumedStack = v),
            (nameof(spell.StatusDurationChargeBonus), v => spell.StatusDurationChargeBonus = v),
        };
        foreach (var field in fields)
        {
            foreach (float value in new[] { -1f, float.NaN, float.PositiveInfinity })
            {
                field.Set(value);
                var issues = new System.Collections.Generic.List<string>();
                SpellRouteValidator.ValidateNumbers(spell, issues);
                if (issues.Count == 0)
                {
                    failures.Add($"validator accepted invalid {field.Name} {value}");
                }
            }
            field.Set(0f);
        }
        return [.. failures];
    }

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
