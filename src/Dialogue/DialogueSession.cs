using System.Collections.Generic;
using Embervale.Companions;
using Embervale.Core.Diagnostics;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Corruption;
using Embervale.Economy;
using Embervale.Entities;
using Embervale.Factions;
using Embervale.Items;
using Embervale.Magic;
using Embervale.Narrative;
using Embervale.Quests;

namespace Embervale.Dialogue;

/// <summary>
/// The runtime walk of a single <see cref="DialogueResource"/> for one actor. It
/// tracks the current node, filters each node's choices by their
/// <see cref="DialogueCondition"/> against the player's quest log and story flags, and
/// applies a choice's <see cref="DialogueEffect"/> when picked before advancing. It is
/// a plain object (not a node) so the dialogue UI stays a thin view: it renders
/// <see cref="CurrentNode"/> + <see cref="VisibleChoices"/> and forwards clicks to
/// <see cref="Choose"/>.
/// </summary>
public sealed class DialogueSession
{
    private readonly IEntity _player;
    private readonly QuestLogComponent? _questLog;
    private readonly StoryFlagsComponent? _flags;
    private readonly CorruptionComponent? _corruption;
    private readonly SpellcastingComponent? _spellcasting;
    private readonly ReputationComponent? _reputation;
    private readonly InventoryComponent? _inventory;

    public DialogueResource Dialogue { get; }

    public DialogueNode? CurrentNode { get; private set; }

    public bool IsEnded => CurrentNode == null;

    public DialogueSession(DialogueResource dialogue, IEntity player)
    {
        Dialogue = dialogue;
        _player = player;
        _questLog = player.GetComponent<QuestLogComponent>();
        _flags = player.GetComponent<StoryFlagsComponent>();
        _corruption = player.GetComponent<CorruptionComponent>();
        _spellcasting = player.GetComponent<SpellcastingComponent>();
        _reputation = player.GetComponent<ReputationComponent>();
        _inventory = player.GetComponent<InventoryComponent>();

        // Start variants first (first matching condition wins), else the authored start node. The start
        // node's OnEnter effect fires here, once, exactly as a node reached by a choice does.
        CurrentNode = ResolveStart(dialogue);
        EnterCurrentNode();
    }

    private DialogueNode? ResolveStart(DialogueResource dialogue)
    {
        var variants = new List<(DialogueCondition, string, string)>();
        foreach (DialogueStartVariant variant in dialogue.StartVariantList())
        {
            variants.Add((variant.Condition, variant.ConditionArg, variant.NodeId));
        }

        if (variants.Count == 0)
        {
            return dialogue.StartNode();
        }

        string id = DialogueRules.SelectStartNode(
            variants, Evaluate, n => dialogue.FindNode(n) != null, string.Empty);
        return id.Length > 0 ? dialogue.FindNode(id) : dialogue.StartNode();
    }

    /// <summary>Applies the node's OnEnter effect (once per entry). A panel-opening effect is refused:
    /// it would open a shop or service window under a live conversation (the validator rejects it).</summary>
    private void EnterCurrentNode()
    {
        if (CurrentNode is not { } node || node.OnEnterEffect == DialogueEffect.None)
        {
            return;
        }

        if (DialogueRules.OpensPanel(node.OnEnterEffect))
        {
            Log.Warn($"Dialogue '{Dialogue.Id}' node '{node.Id}': an OnEnter effect may not open a shop or service.");
            return;
        }

        ApplyEffect(node.OnEnterEffect, node.OnEnterEffectArg);
    }

    /// <summary>Speaker name for the current node (node override, else the conversation's).</summary>
    public string CurrentSpeaker()
    {
        if (CurrentNode != null && !string.IsNullOrEmpty(CurrentNode.Speaker))
        {
            return CurrentNode.Speaker;
        }

        return Dialogue.SpeakerName;
    }

    /// <summary>The current node's choices whose conditions currently pass.</summary>
    public List<DialogueChoice> VisibleChoices()
    {
        var visible = new List<DialogueChoice>();
        if (CurrentNode == null)
        {
            return visible;
        }

        foreach (DialogueChoice choice in CurrentNode.ChoiceList())
        {
            if (DialogueRules.ChoiceVisible(
                    choice.Condition, choice.ConditionArg, choice.Condition2, choice.Condition2Arg, Evaluate))
            {
                visible.Add(choice);
            }
        }

        return visible;
    }

    /// <summary>Applies the choice's effect and advances to its target node.
    /// Returns true if the conversation is now ended.</summary>
    public bool Choose(DialogueChoice choice)
    {
        if (choice == null)
        {
            return IsEnded;
        }

        ApplyEffect(choice.Effect, choice.EffectArg);
        ApplyEffect(choice.Effect2, choice.Effect2Arg);
        CurrentNode = Dialogue.FindNode(choice.Goto); // empty/unknown id => null => ended
        EnterCurrentNode();
        return IsEnded;
    }

    private bool Evaluate(DialogueCondition condition, string arg)
    {
        switch (condition)
        {
            case DialogueCondition.Always:
                return true;
            case DialogueCondition.QuestAvailable:
                return QuestDatabase.Get(arg) is { } q && (_questLog?.CanStart(q) ?? false);
            case DialogueCondition.QuestActive:
                return _questLog?.IsActive(arg) ?? false;
            case DialogueCondition.QuestCompleted:
                return _questLog?.IsCompleted(arg) ?? false;
            case DialogueCondition.QuestNotStarted:
                return !(_questLog?.HasQuest(arg) ?? false);
            case DialogueCondition.HasFlag:
                return _flags?.Has(arg) ?? false;
            case DialogueCondition.MissingFlag:
                return !(_flags?.Has(arg) ?? false);
            case DialogueCondition.CorruptionAtLeast:
                return _corruption != null && _corruption.Value >= ParseAmount(arg);
            case DialogueCondition.CorruptionBelow:
                return _corruption != null && _corruption.Value < ParseAmount(arg);
            case DialogueCondition.CompanionRecruited:
                return Roster()?.IsRecruited(arg) ?? false;
            case DialogueCondition.CompanionNotRecruited:
                return !(Roster()?.IsRecruited(arg) ?? false);
            case DialogueCondition.CompanionLoyaltyAtLeast:
                return CompanionArg.TryParse(arg, out string loyaltyId, out int threshold) &&
                    Roster() is { } loyaltyRoster && loyaltyRoster.LoyaltyOf(loyaltyId) >= threshold;

            // ⚠️ Both spelled out rather than left to the default below. An unhandled member there
            // falls through to *shown*, which for a trade line means the shop choice appearing at
            // midnight — the precise failure 38J exists to remove.
            case DialogueCondition.ShopOpen:
                return ShopIsOpen(arg);
            case DialogueCondition.ShopClosed:
                return !ShopIsOpen(arg);

            // Also spelled out rather than defaulted, for the ShopOpen reason above: a guild line
            // shown by accident is a stranger being greeted as a sworn member.
            case DialogueCondition.GuildRankAtLeast:
                return GuildMeets(arg, member: true);
            case DialogueCondition.GuildNotMember:
                return GuildMeets(arg, member: false);
            case DialogueCondition.GuildCanJoin:
                return _flags != null && FactionDatabase.Get(arg) is { IsGuild: true } joinable &&
                    GuildRules.CanJoin(GuildRules.Resolve(_flags.Has, joinable), joinable.RejoinAllowed);
            case DialogueCondition.ReputationAtLeast:
                // Earned standing (Get), not Effective: see the enum member's note.
                return DialogueRules.TryParseIdAmount(arg, 0, out string repFaction, out int repAmount) &&
                    _reputation != null && _reputation.Get(repFaction) >= repAmount;
            case DialogueCondition.CompanionInParty:
                return Roster()?.IsRecruited(arg) ?? false;
            case DialogueCondition.HasItem:
                return DialogueRules.TryParseIdAmount(arg, 1, out string heldItem, out int heldCount) &&
                    _inventory != null && _inventory.Contains(heldItem, heldCount);
            default:
                return true;
        }
    }

    /// <summary>
    /// Resolves a guild membership condition (Phase 42B). <paramref name="member"/> picks which side
    /// of the pair is being asked — <c>true</c> for <see cref="DialogueCondition.GuildRankAtLeast"/>,
    /// <c>false</c> for <see cref="DialogueCondition.GuildNotMember"/>.
    ///
    /// ⚠️ <b>A malformed or unknown argument answers "not a member".</b> That is the safe direction
    /// for both members of the pair: a mis-authored rank gate hides guild-only content rather than
    /// offering it to a stranger, and a mis-authored recruiting gate shows a line that only ever
    /// offers to let the player in. The validator reports the typo separately.
    /// </summary>
    private bool GuildMeets(string arg, bool member)
    {
        bool isMember =
            _flags != null &&
            GuildRules.TryParseRankArg(arg, out string factionId, out int minRank) &&
            FactionDatabase.Get(factionId) is { } guild &&
            guild.IsGuild &&
            GuildRules.MeetsRank(_flags.Has, guild.Id, guild.RankNameKeys.Count, minRank);

        return isMember == member;
    }

    /// <summary>
    /// Whether a shop is trading right now (Phase 38J) — the one reader of a shop's hours on the
    /// dialogue side, so the condition pair, the effect's backstop and the validator's rule all agree
    /// on what "open" means.
    ///
    /// ⚠️ An unknown shop id answers <b>open</b>. The validator rejects the id, and a half-authored
    /// world must not silently shut a merchant the player is standing in front of — the same inverted
    /// fail-safe an unresolvable standing gets in <c>VendorComponent.WillTrade</c>. A missing
    /// <c>WorldClock</c> answers open for the same reason.
    /// </summary>
    private static bool ShopIsOpen(string shopId)
    {
        if (ShopDatabase.Get(shopId) is not { } shop)
        {
            return true;
        }

        if (ServiceLocator.Instance is not { } locator || !locator.TryGet(out World.WorldClock clock))
        {
            return true; // no clock to be shut by — falling back to hour 0 would shut every morning shop
        }

        return ShopHours.IsOpenAt(clock.Hour, shop.OpenHour, shop.CloseHour);
    }

    /// <summary>The party roster, resolved live from the <see cref="ServiceLocator"/> — a
    /// conversation may outlive any particular world build, and the roster is a world service rather
    /// than something the speaking actor owns.</summary>
    private static CompanionRoster? Roster() =>
        ServiceLocator.Instance != null && ServiceLocator.Instance.TryGet(out CompanionRoster roster)
            ? roster
            : null;

    /// <summary>Parses a numeric dialogue argument (corruption threshold/amount); 0 if malformed
    /// (the content validator flags non-numeric corruption args at author time).</summary>
    private static int ParseAmount(string arg) => int.TryParse(arg, out int value) ? value : 0;

    private void ApplyEffect(DialogueEffect effect, string arg)
    {
        switch (effect)
        {
            case DialogueEffect.None:
                break;
            case DialogueEffect.StartQuest:
                if (QuestDatabase.Get(arg) is { } quest)
                {
                    _questLog?.StartQuest(quest);
                }
                else
                {
                    Log.Warn($"Dialogue effect StartQuest: unknown quest '{arg}'.");
                }

                break;
            case DialogueEffect.SetFlag:
                _flags?.Set(arg);
                break;
            case DialogueEffect.ClearFlag:
                _flags?.Clear(arg);
                break;
            case DialogueEffect.AddCorruption:
                _corruption?.Add(ParseAmount(arg));
                break;
            case DialogueEffect.RecruitCompanion:
                if (Roster() is { } recruiting && !recruiting.Recruit(arg))
                {
                    Log.Warn($"Dialogue effect RecruitCompanion: '{arg}' was not recruited (unknown id, already in the party, or the party is full).");
                }

                break;
            case DialogueEffect.DismissCompanion:
                Roster()?.Dismiss(arg);
                break;
            case DialogueEffect.AddCompanionLoyalty:
                if (CompanionArg.TryParse(arg, out string loyaltyTarget, out int delta))
                {
                    Roster()?.AddLoyalty(loyaltyTarget, delta);
                }
                else
                {
                    Log.Warn($"Dialogue effect AddCompanionLoyalty: malformed argument '{arg}' (expected <companionId>:<delta>).");
                }

                break;
            case DialogueEffect.LearnSpell:
                if (SpellDatabase.Get(arg) is null)
                {
                    Log.Warn($"Dialogue effect LearnSpell: unknown spell '{arg}'.");
                }
                else
                {
                    // The one learning route: it resolves a retired id, applies the 23H corruption gate
                    // (a refusal now toasts why), and raises SpellLearnedEvent once.
                    SpellLearning.TryLearn(_player, arg, LearnRoutes.Dialogue);
                }

                break;
            case DialogueEffect.OpenShop:
                // The conversation ends on the same choice (an OpenShop choice authors no Goto), so the
                // vendor window registers with UiState before the dialogue panel deregisters — the owner
                // count never reaches zero and neither the pause nor the mouse mode flickers between them.
                if (ShopDatabase.Get(arg) is { } shop)
                {
                    // 38J's backstop. The trade choice is meant to carry a ShopOpen condition and
                    // --validate insists on one, but a choice that forgets it must not open a shut
                    // door: the window would show a merchant who is not there and sell her stock at
                    // midnight. Silent rather than toasted — the condition is what tells the player.
                    if (!ShopIsOpen(arg))
                    {
                        Log.Warn($"Dialogue effect OpenShop: '{arg}' is closed at this hour; not opening.");
                        break;
                    }

                    EventBus.Instance?.Publish(new ShopOpenedEvent(_player, shop));
                }
                else
                {
                    Log.Warn($"Dialogue effect OpenShop: unknown shop '{arg}'.");
                }

                break;
            case DialogueEffect.OpenService:
                // 38R, and the shape is OpenShop's for the same UiState reason: an OpenService choice
                // authors no Goto, so a panel-opening kind registers with UiState before the dialogue
                // panel deregisters and the owner count never reaches zero.
                //
                // vault: null is the whole reason TryUse takes the parameter — a conversation has no
                // host entity, so a Bank has nothing to open. --validate refuses that authoring, and
                // OpenVault logs rather than throws if one ever reaches here.
                if (ServiceDatabase.Get(arg) is { } service)
                {
                    // Silent when refused, exactly as the walk-up prompt is: the refusal states
                    // (hostile, already held, cannot afford) are what the conversation's own text is
                    // for, and a toast here would talk over the line the author wrote.
                    ServiceComponent.TryUse(service, _player, vault: null);
                }
                else
                {
                    Log.Warn($"Dialogue effect OpenService: unknown service '{arg}'.");
                }

                break;

            case DialogueEffect.JoinGuild:
                if (_flags != null && FactionDatabase.Get(arg) is { IsGuild: true } joiningGuild)
                {
                    GuildStanding standing = GuildRules.Resolve(_flags.Has, joiningGuild);
                    if (GuildRules.CanJoin(standing, joiningGuild.RejoinAllowed))
                    {
                        _flags.Clear(GuildRules.RefusedFlag(arg));
                        _flags.Clear(GuildRules.LeftFlag(arg));
                        _flags.Set(GuildRules.OfferedFlag(arg));
                        _flags.Set(GuildRules.JoinedFlag(arg));
                    }
                    else
                    {
                        Log.Warn($"Dialogue effect JoinGuild: '{arg}' will not take the player back (RejoinAllowed = false).");
                    }
                }
                else
                {
                    Log.Warn($"Dialogue effect JoinGuild: unknown or non-guild faction '{arg}'.");
                }

                break;
            case DialogueEffect.GuildRank:
                if (_flags != null && GuildRules.TryParseRankArg(arg, out string rankFactionId, out int rank) &&
                    FactionDatabase.Get(rankFactionId) is { IsGuild: true } rankGuild &&
                    rank >= 1 && rank <= rankGuild.RankNameKeys.Count)
                {
                    for (int i = 1; i <= GuildRules.MaxRanks; i++)
                    {
                        if (i <= rank)
                        {
                            _flags.Set(GuildRules.RankFlag(rankFactionId, i));
                        }
                        else
                        {
                            _flags.Clear(GuildRules.RankFlag(rankFactionId, i));
                        }
                    }
                }
                else
                {
                    Log.Warn($"Dialogue effect GuildRank: malformed argument '{arg}' or rank out of range.");
                }

                break;
            case DialogueEffect.AddReputation:
                if (DialogueRules.TryParseIdAmount(arg, 0, out string repTarget, out int repDelta) &&
                    FactionDatabase.Get(repTarget) != null)
                {
                    _reputation?.Add(repTarget, repDelta);
                }
                else
                {
                    Log.Warn($"Dialogue effect AddReputation: malformed argument '{arg}' (expected <factionId>:<delta>) or unknown faction.");
                }

                break;
            case DialogueEffect.GiveItem:
                if (DialogueRules.TryParseIdAmount(arg, 1, out string giftId, out int giftCount) &&
                    ItemDatabase.Get(giftId) is { } gift && giftCount > 0)
                {
                    // ItemGrant spills the overflow at the player's feet; a full pack never eats a gift.
                    ItemGrant.Give(_inventory, gift, giftCount, _player);
                }
                else
                {
                    Log.Warn($"Dialogue effect GiveItem: malformed argument '{arg}' (expected <itemId>:<count>) or unknown item.");
                }

                break;
            case DialogueEffect.TakeItem:
                if (DialogueRules.TryParseIdAmount(arg, 1, out string takeId, out int takeCount) && takeCount > 0)
                {
                    // All or nothing: the choice should be gated on HasItem, but a choice that is not
                    // must not half-take a stack or push the count negative.
                    if (_inventory == null || !_inventory.Contains(takeId, takeCount) ||
                        !_inventory.RemoveItem(takeId, takeCount))
                    {
                        Log.Warn($"Dialogue effect TakeItem: the player does not hold {takeCount}x '{takeId}'; nothing taken.");
                    }
                }
                else
                {
                    Log.Warn($"Dialogue effect TakeItem: malformed argument '{arg}' (expected <itemId>:<count>).");
                }

                break;
            case DialogueEffect.PlayCards:
                if (!string.IsNullOrEmpty(arg))
                {
                    EventBus.Instance?.Publish(new StoryCardsRequestedEvent(arg));
                }

                break;
            case DialogueEffect.TrackQuest:
                _questLog?.Track(arg);
                break;
            case DialogueEffect.Banner:
                if (!string.IsNullOrEmpty(arg))
                {
                    EventBus.Instance?.Publish(new StoryBannerRequestedEvent(arg));
                }

                break;
        }
    }
}
