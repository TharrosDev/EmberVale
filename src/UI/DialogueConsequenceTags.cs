using System.Collections.Generic;
using Embervale.Companions;
using Embervale.Dialogue;

namespace Embervale.UI;

/// <summary>What kind of consequence a dialogue choice carries.</summary>
public enum ConsequenceKind
{
    Quest,
    Corruption,
    Reputation,
    Loyalty,
    Companion,
    Guild,
    Item,
    Story,
}

/// <summary>One consequence chip. <paramref name="Arg"/> is the faction/companion/item/guild id the
/// chip names; <paramref name="Amount"/> is the signed change (the rank for a guild promotion, +1/-1 for a
/// companion joining/leaving).</summary>
public readonly record struct ConsequenceTag(ConsequenceKind Kind, string Arg, int Amount);

/// <summary>
/// Derives the consequence chips on a dialogue choice from its effect enums, so a player can see "this
/// costs corruption" or "this starts a quest" before choosing. Pure: it reads only the effect ordinal and
/// its argument. The chips are text, never colour alone.
///
/// </summary>
public static class DialogueConsequenceTags
{
    public const int AddReputation = (int)DialogueEffect.AddReputation;
    public const int GiveItem = (int)DialogueEffect.GiveItem;
    public const int TakeItem = (int)DialogueEffect.TakeItem;
    public const int PlayCards = (int)DialogueEffect.PlayCards;
    public const int TrackQuest = (int)DialogueEffect.TrackQuest;
    public const int Banner = (int)DialogueEffect.Banner;

    /// <summary>The chips for one choice: its two effects, then the target node's on-enter effect (taking the
    /// choice is what triggers it). Identical chips collapse to one.</summary>
    public static List<ConsequenceTag> ForChoice(
        int effect, string effectArg, int effect2, string effect2Arg, int onEnter, string onEnterArg)
    {
        var tags = new List<ConsequenceTag>();
        Add(tags, effect, effectArg);
        Add(tags, effect2, effect2Arg);
        Add(tags, onEnter, onEnterArg);
        return tags;
    }

    public static List<ConsequenceTag> ForEffect(int effect, string arg)
    {
        var tags = new List<ConsequenceTag>();
        Add(tags, effect, arg);
        return tags;
    }

    private static void Add(List<ConsequenceTag> into, int effect, string arg)
    {
        ConsequenceTag? tag = Of(effect, arg);
        if (tag is { } t && !into.Contains(t))
        {
            into.Add(t);
        }
    }

    /// <summary>The chip for one effect, or null when the effect is not a visible consequence.</summary>
    public static ConsequenceTag? Of(int effect, string arg)
    {
        switch (effect)
        {
            case (int)DialogueEffect.StartQuest:
                return new ConsequenceTag(ConsequenceKind.Quest, arg, 0);

            case (int)DialogueEffect.SetFlag:
            case PlayCards:
                return new ConsequenceTag(ConsequenceKind.Story, string.Empty, 0);

            case (int)DialogueEffect.AddCorruption:
                return int.TryParse(arg, out int corruption) && corruption != 0
                    ? new ConsequenceTag(ConsequenceKind.Corruption, string.Empty, corruption)
                    : null;

            case (int)DialogueEffect.RecruitCompanion:
                return new ConsequenceTag(ConsequenceKind.Companion, arg, 1);

            case (int)DialogueEffect.DismissCompanion:
                return new ConsequenceTag(ConsequenceKind.Companion, arg, -1);

            case (int)DialogueEffect.AddCompanionLoyalty:
                return CompanionArg.TryParse(arg, out string companion, out int loyalty) && loyalty != 0
                    ? new ConsequenceTag(ConsequenceKind.Loyalty, companion, loyalty)
                    : null;

            case (int)DialogueEffect.JoinGuild:
                return new ConsequenceTag(ConsequenceKind.Guild, arg, 0);

            case (int)DialogueEffect.GuildRank:
                return CompanionArg.TryParse(arg, out string guild, out int rank) && rank > 0
                    ? new ConsequenceTag(ConsequenceKind.Guild, guild, rank)
                    : null;

            case AddReputation:
                return CompanionArg.TryParse(arg, out string faction, out int delta) && delta != 0
                    ? new ConsequenceTag(ConsequenceKind.Reputation, faction, delta)
                    : null;

            case GiveItem:
            case TakeItem:
                return CompanionArg.TryParse(arg, out string item, out int count)
                    ? new ConsequenceTag(ConsequenceKind.Item, item, (count == 0 ? 1 : count) * (effect == TakeItem ? -1 : 1))
                    : null;

            default:
                return null;
        }
    }
}
