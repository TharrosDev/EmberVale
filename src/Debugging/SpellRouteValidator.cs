using System.Collections.Generic;
using Embervale.Core;
using Embervale.Dialogue;
using Embervale.Magic;
using Embervale.Races;
using Godot;

namespace Embervale.Debugging;

/// <summary>
/// The magic roster's content rules (2026-09 magic upgrade): the 25 player spells exist and are the only
/// player-learnable ones, every one of them can be reached through normal play, every status a spell
/// names resolves, every alias points at a real spell, and each placement kind carries the numbers its
/// mechanism needs. <see cref="Routes"/> is public so the headless probe reports the same table the
/// validator enforces, rather than a second reading of the data.
///
/// A route is any of: the starting list, a race's innate spells, a teacher's <c>LearnSpell</c> dialogue
/// effect, or a <see cref="SpellTomeComponent"/> authored in a region scene. The spellbook's skill-point
/// purchase is deliberately not one: magic is recovered, not bought, so a spell whose only path is the
/// shelf is unreachable content.
/// </summary>
public static class SpellRouteValidator
{
    /// <summary>Spell id to the routes that lead to it, as short human-readable strings.</summary>
    public static Dictionary<string, List<string>> Routes()
    {
        var routes = new Dictionary<string, List<string>>();

        void Add(string spellId, string route)
        {
            string id = SpellAliases.Resolve(spellId);
            if (!routes.TryGetValue(id, out List<string>? list))
            {
                routes[id] = list = new List<string>();
            }

            list.Add(route);
        }

        foreach (string id in GameIds.Spells.Starting)
        {
            Add(id, "start");
        }

        foreach (RaceResource race in RaceDatabase.All)
        {
            foreach (string id in race.InnateSpellIds)
            {
                Add(id, $"race {race.Id}");
            }
        }

        var sceneText = new System.Text.StringBuilder();
        foreach (string path in ScenePaths("res://scenes/regions"))
        {
            using FileAccess? file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
            if (file != null)
            {
                sceneText.AppendLine(file.GetAsText());
            }
        }

        string authoredScenes = sceneText.ToString();
        foreach (DialogueResource dialogue in DialogueDatabase.All)
        {
            if (!authoredScenes.Contains($"DialogueId = \"{dialogue.Id}\"", System.StringComparison.Ordinal))
            {
                continue;
            }

            var reachable = new HashSet<string>();
            var pending = new Stack<string>();
            if (dialogue.StartNode() is { } start)
            {
                pending.Push(start.Id);
            }

            while (pending.Count > 0)
            {
                string id = pending.Pop();
                if (!reachable.Add(id) || dialogue.FindNode(id) is not { } current)
                {
                    continue;
                }

                foreach (DialogueChoice choice in current.ChoiceList())
                {
                    if (choice.Goto.Length > 0)
                    {
                        pending.Push(choice.Goto);
                    }
                }
            }

            foreach (DialogueNode node in dialogue.NodeList())
            {
                if (!reachable.Contains(node.Id))
                {
                    continue;
                }

                foreach (DialogueChoice choice in node.ChoiceList())
                {
                    if (choice.Effect == DialogueEffect.LearnSpell && choice.EffectArg.Length > 0)
                    {
                        Add(choice.EffectArg, $"teacher {dialogue.Id}");
                    }
                }
            }
        }

        var tome = new System.Text.RegularExpressions.Regex(@"(?m)^SpellId = ""([^""]*)""");
        foreach (string path in ScenePaths("res://scenes/regions"))
        {
            using FileAccess? file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
            if (file == null)
            {
                continue;
            }

            foreach (System.Text.RegularExpressions.Match match in tome.Matches(file.GetAsText()))
            {
                Add(match.Groups[1].Value, $"tome {path.Substring("res://scenes/regions/".Length)}");
            }
        }

        return routes;
    }

    public static void Validate(List<string> issues)
    {
        var roster = new HashSet<string>(GameIds.Spells.Roster);
        if (roster.Count != GameIds.Spells.Roster.Length)
        {
            issues.Add("GameIds.Spells.Roster lists a spell twice");
        }

        foreach (string id in roster)
        {
            if (SpellDatabase.Get(id) is not { PlayerLearnable: true })
            {
                issues.Add($"roster spell '{id}' is missing or not PlayerLearnable");
            }
        }

        Dictionary<string, List<string>> routes = Routes();

        foreach (SpellResource spell in SpellDatabase.All)
        {
            ValidateNumbers(spell, issues);
            if (spell.PlayerLearnable)
            {
                if (!roster.Contains(spell.Id))
                {
                    issues.Add($"spell '{spell.Id}' is PlayerLearnable but not in GameIds.Spells.Roster");
                }

                if (!routes.ContainsKey(spell.Id))
                {
                    issues.Add($"player spell '{spell.Id}' has no learn route: no tome, teacher, race or starting list names it");
                }
            }

            foreach (string status in new[] { spell.SelfStatusEffectId, spell.ConsumesStatusId })
            {
                if (status.Length > 0 && StatusEffectDatabase.Get(status) == null)
                {
                    issues.Add($"spell '{spell.Id}' references unknown status effect '{status}'");
                }
            }

            switch (spell.Delivery)
            {
                case SpellDelivery.Ground:
                    if (spell.PlaceRange <= 0f || spell.GroundDelay <= 0f || spell.ImpactRadius <= 0f)
                    {
                        issues.Add($"ground spell '{spell.Id}' needs PlaceRange, GroundDelay and ImpactRadius above zero");
                    }

                    break;
                case SpellDelivery.Barrier:
                    if (spell.PlaceRange <= 0f || spell.BarrierDuration <= 0f || spell.BarrierWidth <= 0f)
                    {
                        issues.Add($"barrier spell '{spell.Id}' needs PlaceRange, BarrierDuration and BarrierWidth above zero");
                    }

                    break;
                case SpellDelivery.Dash:
                    if (spell.DashDistance <= 0f)
                    {
                        issues.Add($"dash spell '{spell.Id}' has no DashDistance");
                    }

                    break;
            }
        }

        foreach ((string old, string current) in SpellAliases.All)
        {
            if (SpellDatabase.Get(current) == null || SpellAliases.IsRetired(current))
            {
                issues.Add($"spell alias '{old}' points at '{current}', which is not a live spell");
            }
        }
    }

    /// <summary>Rejects nonfinite and negative authored mechanics before runtime clamps can hide them.</summary>
    public static void ValidateNumbers(SpellResource spell, List<string> issues)
    {
        void Nonnegative(string name, float value)
        {
            if (!float.IsFinite(value) || value < 0f)
            {
                issues.Add($"spell '{spell.Id}' {name} must be finite and nonnegative: {value}");
            }
        }

        Nonnegative(nameof(spell.ManaCost), spell.ManaCost);
        Nonnegative(nameof(spell.Cooldown), spell.Cooldown);
        Nonnegative(nameof(spell.BaseDamage), spell.BaseDamage);
        Nonnegative(nameof(spell.Healing), spell.Healing);
        Nonnegative(nameof(spell.WindupSeconds), spell.WindupSeconds);
        Nonnegative(nameof(spell.RecoverySeconds), spell.RecoverySeconds);
        Nonnegative(nameof(spell.PoiseDamage), spell.PoiseDamage);
        Nonnegative(nameof(spell.GroundDelay), spell.GroundDelay);
        Nonnegative(nameof(spell.PullStrength), spell.PullStrength);
        Nonnegative(nameof(spell.BarrierDuration), spell.BarrierDuration);
        Nonnegative(nameof(spell.BarrierHealth), spell.BarrierHealth);
        Nonnegative(nameof(spell.DashDistance), spell.DashDistance);
        Nonnegative(nameof(spell.HealthCost), spell.HealthCost);
        Nonnegative(nameof(spell.BonusPerConsumedStack), spell.BonusPerConsumedStack);
        Nonnegative(nameof(spell.StatusDurationChargeBonus), spell.StatusDurationChargeBonus);
        if (!float.IsFinite(spell.ImpactWeight) || spell.ImpactWeight is < 0f or > 1f)
        {
            issues.Add($"spell '{spell.Id}' ImpactWeight must be finite and within 0..1: {spell.ImpactWeight}");
        }

        if (spell.PierceCount < 0 || spell.PierceChargeBonus < 0)
        {
            issues.Add($"spell '{spell.Id}' piercing counts must be nonnegative");
        }

        if (spell.CastMode == CastMode.Charged &&
            (!float.IsFinite(spell.ChargeTime) || spell.ChargeTime <= 0f ||
             !float.IsFinite(spell.MaxChargeMultiplier) || spell.MaxChargeMultiplier < 1f))
        {
            issues.Add($"charged spell '{spell.Id}' requires finite positive ChargeTime and MaxChargeMultiplier at least one");
        }

        if (spell.CastMode == CastMode.Channeled &&
            (!float.IsFinite(spell.ChannelTickInterval) || spell.ChannelTickInterval <= 0f ||
             !float.IsFinite(spell.ChannelManaPerSecond) || spell.ChannelManaPerSecond < 0f))
        {
            issues.Add($"channelled spell '{spell.Id}' requires finite positive tick interval and nonnegative mana rate");
        }

        if (!float.IsFinite(spell.PlaceRange) || !float.IsFinite(spell.ImpactRadius) ||
            !float.IsFinite(spell.BarrierWidth))
        {
            issues.Add($"spell '{spell.Id}' placement dimensions must be finite");
        }
    }

    private static IEnumerable<string> ScenePaths(string directory)
    {
        if (!DirAccess.DirExistsAbsolute(directory))
        {
            yield break;
        }

        foreach (string file in DirAccess.GetFilesAt(directory))
        {
            if (file.EndsWith(".tscn", System.StringComparison.OrdinalIgnoreCase))
            {
                yield return $"{directory}/{file}";
            }
        }

        foreach (string sub in DirAccess.GetDirectoriesAt(directory))
        {
            foreach (string nested in ScenePaths($"{directory}/{sub}"))
            {
                yield return nested;
            }
        }
    }
}
