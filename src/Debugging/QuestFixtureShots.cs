using System.Collections.Generic;
using System.Linq;
using Embervale.Core.Services;
using Embervale.Dialogue;
using Embervale.Items;
using Embervale.Player;
using Embervale.Quests;
using Embervale.World;
using Godot;

namespace Embervale.Debugging;

/// <summary>
/// In-memory quests and a conversation for the quest-UI screenshots (<c>--panelshots</c>, <c>--hudshots</c>).
///
/// No authored content uses the campaign fields yet (chapter, region, level, hints, journal lines, optional
/// objectives, ledger), so a shot of the real quests would photograph none of the new UI. These build the
/// shapes a campaign quest will have, drive them through <see cref="QuestLogComponent"/>'s public API, and
/// register their text as a runtime translation, so the shipped catalogue carries no screenshot-only rows.
/// Lives in a <c>*Shots.cs</c> file because the shipping build excludes that pattern.
/// </summary>
internal static class QuestShotFixtures
{
    public const string AshWind = "quest.shot.ash_wind";
    public const string ClosedHold = "quest.shot.closed_hold";
    public const string LastHearth = "quest.shot.last_hearth";
    public const string Errand = "quest.shot.errand";
    public const string Ledger = "quest.shot.ledger";
    public const string OldErrandPrefix = "quest.shot.old_";
    public const string ElderDialogue = "dialogue.shot_elder";
    public const string BossEpithetKey = "shot.boss.epithet";
    public const string BossIntroKey = "shot.boss.intro";
    public const int OldErrands = 7;

    private static bool _textRegistered;

    /// <summary>Registers every fixture string as a second "en" translation. Idempotent.</summary>
    public static void RegisterText()
    {
        if (_textRegistered)
        {
            return;
        }

        _textRegistered = true;
        var t = new Translation { Locale = "en" };
        void Add(string key, string text) => t.AddMessage(key, text);

        Add("chapter.ch.1.title", "Embers at the Crown");
        Add("chapter.ch.1.subtitle", "Smoke over the square, and a stranger's iron in the ash.");
        Add("chapter.ch.2.frostfang.title", "The Frostfang Reach");
        Add("chapter.ch.2.frostfang.subtitle", "The hold keeps its gates shut.");
        Add("chapter.ch.2.ashen.title", "The Ashen Wilds");
        Add("chapter.ch.2.ashen.subtitle", "The herds have gone where the grass is not.");

        Add("shot.giver.elder", "Elder Maren");

        Add("shot.ash_wind.title", "The Ash on the Wind");
        Add("shot.ash_wind.summary", "Smoke over the square. Find out who lit it.");
        Add("shot.ash_wind.detail", "A raider fell in the square with a token of black iron sewn into his coat. " +
            "The Elder wants it read before the next raid, and she wants it read quietly.");
        Add("shot.ash_wind.obj_bell", "Sound the alarm bell");
        Add("shot.ash_wind.log_bell", "You rang the alarm bell and the square armed itself.");
        Add("shot.ash_wind.obj_elder", "Tell the Elder what you found");
        Add("shot.ash_wind.hint_elder", "She is by the well, watching the north road.");
        Add("shot.ash_wind.obj_goblins", "Drive off the scouts on the ridge");
        Add("shot.ash_wind.hint_goblins", "Three of them, close to the old cairn.");
        Add("shot.ash_wind.obj_watch", "Report to the Crossway Watch");
        Add("shot.ash_wind.obj_herbs", "Bring the apothecary what she asked for");

        Add("shot.closed_hold.title", "The Closed Hold");
        Add("shot.closed_hold.obj", "Reach the Clan Hold");
        Add("shot.last_hearth.title", "The Last Hearth");
        Add("shot.last_hearth.obj", "Speak with Maeve");
        Add("shot.errand.title", "A Quiet Errand");
        Add("shot.errand.summary", "Someone in the market needs an item found, and no one asked why.");
        Add("shot.errand.obj", "Find what was lost");
        Add("shot.ledger.title", "The Gathering Storm");
        Add("shot.ledger.summary", "Three powers hold the realm's ruin. The ledger tracks them.");
        Add("shot.ledger.obj", "Bring the three realms to heel");
        Add("shot.old.title", "Old Errand");
        Add("shot.old.obj", "Finish the old errand");

        Add(ElderDialogue + ".text", "The Elder studies the token. \"Black iron. Not ours. " +
            "Tell me everything, and leave out nothing you are ashamed of.\"");
        Add("shot.choice.quest", "\"I will find who made it.\"");
        Add("shot.choice.corrupt", "\"Give me the ember in the ash. I will carry it.\"");
        Add("shot.choice.story", "\"I saw a knight on the ridge.\"");
        Add("shot.choice.loyal", "\"Kael knows more than he says.\"");
        Add("shot.choice.guild", "\"Put my name to the Dawnwardens.\"");
        Add("shot.choice.rep", "\"Hand it to the Syndicate.\"");
        Add("shot.choice.leave", "\"Not now.\"");

        Add(BossEpithetKey, "Warden of the Iron Gate");
        Add(BossIntroKey, "\"Kneel, or be a wall I build on.\"");

        TranslationServer.AddTranslation(t);
    }

    // --- builders --------------------------------------------------------------------------

    private static ObjectiveResource Obj(
        ObjectiveType type, string target, string text, int count = 1, string location = "", bool optional = false,
        string hint = "", string log = "") => new()
    {
        Type = type,
        TargetId = target,
        RequiredCount = count,
        Description = text,
        LocationId = location,
        IsOptional = optional,
        HintKey = hint,
        JournalEntryKey = log,
    };

    private static QuestResource Quest(
        string id, string title, string summary, bool main, string chapter, int order, string region, int level,
        params ObjectiveResource[] objectives)
    {
        var quest = new QuestResource
        {
            Id = id,
            Title = title,
            Summary = summary,
            IsMainQuest = main,
            ChapterKey = chapter,
            OrderInAct = order,
            RegionId = region,
            RecommendedLevel = level,
        };
        foreach (ObjectiveResource objective in objectives)
        {
            quest.Objectives.Add(objective);
        }

        return quest;
    }

    /// <summary>A real, placed map location id for the fixtures' "where" lines, so the journal can name it.</summary>
    private static string Place(params string[] preferred)
    {
        foreach (string id in preferred)
        {
            if (MapLocationDatabase.Get(id) != null)
            {
                return id;
            }
        }

        return MapLocationDatabase.All.FirstOrDefault(l => l.CellId.Length > 0)?.Id ?? string.Empty;
    }

    private static string AnyItemId() => ItemDatabase.All.Keys.FirstOrDefault() ?? string.Empty;

    private static QuestResource BuildAshWind()
    {
        string where = Place("location.crossway.watch", "location.embermarket.jeweller");
        QuestResource quest = Quest(
            AshWind, "shot.ash_wind.title", "shot.ash_wind.summary", main: true, "ch.1", 1, "region.ember_crown", 4,
            Obj(ObjectiveType.Interact, "interactable.shot_bell", "shot.ash_wind.obj_bell", log: "shot.ash_wind.log_bell"),
            Obj(ObjectiveType.Talk, ElderDialogue, "shot.ash_wind.obj_elder", location: where, hint: "shot.ash_wind.hint_elder"),
            Obj(ObjectiveType.Kill, "enemy.goblin", "shot.ash_wind.obj_goblins", count: 3, optional: true, hint: "shot.ash_wind.hint_goblins"),
            Obj(ObjectiveType.Reach, where, "shot.ash_wind.obj_watch"),
            Obj(ObjectiveType.Collect, AnyItemId(), "shot.ash_wind.obj_herbs", count: 2));
        quest.SequentialObjectives = true;
        quest.DetailKey = "shot.ash_wind.detail";
        quest.GiverNameKey = "shot.giver.elder";
        quest.XpReward = 120;
        quest.GoldReward = 45;
        quest.FactionRewardId = "faction.dawnwardens";
        quest.FactionRewardAmount = 8;
        if (AnyItemId().Length > 0)
        {
            quest.RewardItems.Add(new QuestItemReward { ItemId = AnyItemId(), Quantity = 2 });
        }

        return quest;
    }

    /// <summary>Starts the campaign-shaped set: three main quests in three chapters, an errand, a ledger umbrella
    /// and a run of finished errands. The first quest is tracked and its first step completed.</summary>
    public static void StartCampaignSet(QuestLogComponent log)
    {
        RegisterText();
        HoldChapterBanners(true);

        log.StartQuest(BuildAshWind());
        log.Track(AshWind);
        log.DebugAdvance(AshWind, 0);

        string frost = Place("location.crossway.watch");
        log.StartQuest(Quest(
            ClosedHold, "shot.closed_hold.title", string.Empty, main: true, "ch.2.frostfang", 10, "region.frostfang_reach", 7,
            Obj(ObjectiveType.Reach, frost, "shot.closed_hold.obj")));

        log.StartQuest(Quest(
            LastHearth, "shot.last_hearth.title", string.Empty, main: true, "ch.2.ashen", 13, "region.ashen_wilds", 8,
            Obj(ObjectiveType.Talk, "dialogue.shot_maeve", "shot.last_hearth.obj")));

        log.StartQuest(Quest(
            Errand, "shot.errand.title", "shot.errand.summary", main: false, string.Empty, 0, string.Empty, 3,
            Obj(ObjectiveType.Collect, AnyItemId(), "shot.errand.obj", count: 3)));

        QuestResource ledger = Quest(
            Ledger, "shot.ledger.title", "shot.ledger.summary", main: true, string.Empty, 0, string.Empty, 0,
            Obj(ObjectiveType.Milestone, "flag.shot.never_set", "shot.ledger.obj"));
        ledger.IsLedger = true;
        log.StartQuest(ledger);

        for (int i = 1; i <= OldErrands; i++)
        {
            QuestResource old = Quest(
                OldErrandPrefix + i, "shot.old.title", string.Empty, main: false, string.Empty, 0, string.Empty, 1,
                Obj(ObjectiveType.Interact, $"interactable.shot_old_{i}", "shot.old.obj"));
            if (log.StartQuest(old))
            {
                log.DebugComplete(old.Id);
            }
        }
    }

    /// <summary>The tracker/toast fixture: the first quest of the set only, tracked, with its first step met.</summary>
    public static void StartTrackedMainQuest(QuestLogComponent log)
    {
        RegisterText();
        HoldChapterBanners(true);
        if (log.StartQuest(BuildAshWind()))
        {
            log.Track(AshWind);
            log.DebugAdvance(AshWind, 0);
        }
        else
        {
            log.Track(AshWind);
        }
    }

    /// <summary>A conversation whose choices carry every kind of consequence chip, and whose speaker is the
    /// Talk objective of <see cref="AshWind"/>, so the quest line under the speaker shows too.</summary>
    public static DialogueResource BuildElderDialogue()
    {
        RegisterText();
        var root = new DialogueNode { Id = "root", Text = ElderDialogue + ".text" };

        DialogueChoice Choice(string text, DialogueEffect effect, string arg)
        {
            var choice = new DialogueChoice { Text = text, Goto = string.Empty };
            choice.Effect = effect;
            choice.EffectArg = arg;
            return choice;
        }

        string item = AnyItemId();
        root.Choices.Add(Choice("shot.choice.quest", DialogueEffect.StartQuest, AshWind));
        root.Choices.Add(Choice("shot.choice.corrupt", DialogueEffect.AddCorruption, "10"));
        root.Choices.Add(Choice("shot.choice.story", DialogueEffect.SetFlag, "flag.shot.knight_seen"));
        root.Choices.Add(Choice("shot.choice.loyal", DialogueEffect.AddCompanionLoyalty, "companion.kael:1"));
        root.Choices.Add(Choice("shot.choice.guild", DialogueEffect.JoinGuild, "faction.dawnwardens"));
        root.Choices.Add(Choice("shot.choice.rep", DialogueEffect.AddReputation, "faction.iron_syndicate:-4"));
        root.Choices.Add(Choice("shot.choice.leave", DialogueEffect.None, string.Empty));

        // A second effect on the corruption choice: an item hand-over.
        if (root.Choices[1].As<DialogueChoice>() is { } corrupt)
        {
            corrupt.Effect2 = DialogueEffect.GiveItem;
            corrupt.Effect2Arg = $"{item}:1";
        }

        var dialogue = new DialogueResource { Id = ElderDialogue, SpeakerName = "shot.giver.elder", StartNodeId = "root" };
        dialogue.Nodes.Add(root);
        return dialogue;
    }

    /// <summary>
    /// Marks the fixture chapters' banners as already shown (or clears that), through the same story flags the
    /// banner itself writes. Starting a quest in a chapter requests its banner; without this the banners would
    /// land in whichever later frame had no menu open, and the harness photographs the banner on purpose, once.
    /// </summary>
    public static void HoldChapterBanners(bool held)
    {
        if (ServiceLocator.Instance is not { } locator || !locator.TryGet(out PlayerCharacter player) ||
            player.GetComponent<StoryFlagsComponent>() is not { } flags)
        {
            return;
        }

        foreach (string chapter in new[] { "ch.1", "ch.2.frostfang", "ch.2.ashen" })
        {
            string flag = UI.ChapterBannerRules.FlagFor(chapter);
            if (held)
            {
                flags.Set(flag);
            }
            else
            {
                flags.Clear(flag);
            }
        }
    }

    // --- lookups ---------------------------------------------------------------------------

    public static QuestLogComponent? Log() =>
        ServiceLocator.Instance is { } locator && locator.TryGet(out PlayerCharacter player)
            ? player.GetComponent<QuestLogComponent>()
            : null;

    /// <summary>Finds the first node of type <typeparamref name="T"/> under <paramref name="root"/>.</summary>
    public static T? FindFirst<T>(Node root)
        where T : Node
    {
        foreach (Node child in root.GetChildren())
        {
            if (child is T hit)
            {
                return hit;
            }

            if (FindFirst<T>(child) is { } deeper)
            {
                return deeper;
            }
        }

        return null;
    }

    public static IEnumerable<string> FixtureIds()
    {
        yield return AshWind;
        yield return ClosedHold;
        yield return LastHearth;
        yield return Errand;
        yield return Ledger;
    }
}
