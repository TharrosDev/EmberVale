using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Embervale.Companions;

/// <summary>
/// One companion reaction (<c>data/story/reactions/*.json</c>): when <see cref="Flag"/> becomes set and
/// <see cref="Companion"/> is in the party, the party member says <see cref="TextKey"/> (a toast line) and
/// their loyalty moves by <see cref="LoyaltyDelta"/>. Once per save, recorded as <see cref="BarkFlag"/>.
/// </summary>
public sealed record CompanionReaction(string Id, string Flag, string Companion, string TextKey, int LoyaltyDelta)
{
    public const string IdPrefix = "bark.";
    public const string BarkFlagPrefix = "flag.bark.";

    /// <summary>The flag recording that this reaction played: <c>bark.x</c> becomes <c>flag.bark.x</c>.</summary>
    public string BarkFlag => BarkFlagPrefix + Id[IdPrefix.Length..];
}

/// <summary>Pure parsing and selection for companion reactions.</summary>
public static class CompanionReactionData
{
    public const string Directory = "res://data/story/reactions";

    /// <summary>A bark is flavour: it may nudge loyalty a little in either direction, not decide it.</summary>
    public const int MaxLoyaltyDelta = 20;

    /// <summary>
    /// Parses one reaction file (<c>{"reactions":[...]}</c>). Problems go to <paramref name="errors"/> and
    /// the offending reaction is skipped; a malformed document yields none.
    /// </summary>
    public static List<CompanionReaction> Parse(string json, string source, List<string> errors)
    {
        var reactions = new List<CompanionReaction>();
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
        }
        catch (JsonException e)
        {
            errors.Add($"{source}: not valid JSON ({e.Message})");
            return reactions;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("reactions", out JsonElement list) ||
                list.ValueKind != JsonValueKind.Array)
            {
                errors.Add($"{source}: expected an object with a \"reactions\" array");
                return reactions;
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            int index = 0;
            foreach (JsonElement element in list.EnumerateArray())
            {
                string where = $"{source} reactions[{index++}]";
                if (element.ValueKind != JsonValueKind.Object)
                {
                    errors.Add($"{where}: not an object");
                    continue;
                }

                string id = Str(element, "id");
                if (!id.StartsWith(CompanionReaction.IdPrefix, StringComparison.Ordinal) ||
                    id.Length <= CompanionReaction.IdPrefix.Length)
                {
                    errors.Add($"{where}: id '{id}' must start with '{CompanionReaction.IdPrefix}'");
                    continue;
                }

                where = $"{source} reaction '{id}'";
                if (!ids.Add(id))
                {
                    errors.Add($"{where}: duplicate id within the file");
                    continue;
                }

                string flag = Str(element, "flag");
                string companion = Str(element, "companion");
                string textKey = Str(element, "textKey");
                bool ok = true;
                if (!flag.StartsWith("flag.", StringComparison.Ordinal) || flag.Length <= 5)
                {
                    errors.Add($"{where}: flag '{flag}' must start with 'flag.'");
                    ok = false;
                }

                if (!companion.StartsWith("companion.", StringComparison.Ordinal) || companion.Length <= 10)
                {
                    errors.Add($"{where}: companion '{companion}' must start with 'companion.'");
                    ok = false;
                }

                if (textKey.Length == 0)
                {
                    errors.Add($"{where}: textKey is required");
                    ok = false;
                }

                int delta = 0;
                if (element.TryGetProperty("loyaltyDelta", out JsonElement deltaElement))
                {
                    if (deltaElement.ValueKind != JsonValueKind.Number || !deltaElement.TryGetInt32(out delta))
                    {
                        errors.Add($"{where}: loyaltyDelta must be an integer");
                        ok = false;
                    }
                    else if (delta < -MaxLoyaltyDelta || delta > MaxLoyaltyDelta)
                    {
                        errors.Add($"{where}: loyaltyDelta {delta} is outside -{MaxLoyaltyDelta}..{MaxLoyaltyDelta}");
                        ok = false;
                    }
                }

                if (ok)
                {
                    reactions.Add(new CompanionReaction(id, flag, companion, textKey, delta));
                }
            }
        }

        return reactions;
    }

    /// <summary>
    /// The reactions that should play now: <paramref name="changedFlag"/> was just set, the reaction's
    /// companion is in the party, and it has not played yet (<paramref name="barkHeld"/> answers its
    /// <see cref="CompanionReaction.BarkFlag"/>).
    /// </summary>
    public static List<CompanionReaction> Due(
        IEnumerable<CompanionReaction> reactions, string changedFlag, Func<string, bool> inParty,
        Func<string, bool> barkHeld)
    {
        var due = new List<CompanionReaction>();
        foreach (CompanionReaction reaction in reactions)
        {
            if (reaction.Flag == changedFlag && !barkHeld(reaction.BarkFlag) && inParty(reaction.Companion))
            {
                due.Add(reaction);
            }
        }

        return due;
    }

    private static string Str(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? string.Empty
            : string.Empty;
}
