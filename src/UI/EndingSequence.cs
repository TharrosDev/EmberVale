using System.Collections.Generic;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Dialogue;
using Embervale.Player;

namespace Embervale.UI;

/// <summary>
/// The game's ending (finish run): the throne choice in <c>dialogue.ash_throne</c> sets
/// <see cref="DawnfireFlag"/> or <see cref="EmbersFlag"/>, and this plays that ending's cards, an
/// epilogue card chosen by how many Flamebearer embers the player took, one card for each fork the
/// player decided (<see cref="ForkEpilogues"/>, Act I to Act IV), and the credits. When they
/// lift the player is still standing in the world — the save carries <see cref="CompleteFlag"/> and
/// free roam continues. <see cref="CompleteFlag"/> is also what stops a reload replaying it.
/// </summary>
public partial class EndingSequence : NarrationSequence
{
    public const string DawnfireFlag = "flag.ending_dawnfire";
    public const string EmbersFlag = "flag.ending_embers";
    public const string CompleteFlag = "flag.game_complete";

    /// <summary>Absorb flags of every Flamebearer whose ember can be taken.</summary>
    public static readonly string[] AbsorbFlags =
    {
        "flag.iron_king_absorbed", "flag.storm_tyrant_absorbed", "flag.beast_lord_absorbed",
        "flag.crimson_prophet_absorbed", "flag.hollow_queen_absorbed", "flag.ashen_knight_absorbed",
    };

    /// <summary>
    /// The campaign's decided forks in story order. Each row is a fork slug and the flags that select
    /// its epilogue card: the first flag plays <c>ending.epilogue.&lt;slug&gt;.a</c>, the second
    /// <c>.b</c>, the third <c>.c</c> (rows authored in spec2_act4.py). A fork with none of its flags
    /// held contributes no card; the Knight's vigil has a third answer, "neither", which is no card.
    /// </summary>
    public static readonly (string Slug, string[] Flags)[] ForkEpilogues =
    {
        ("dray", new[] { "flag.fork.dray_spared", "flag.fork.dray_pressed" }),
        ("succession", new[] { "flag.fork.succession_hjalvar", "flag.fork.succession_halvar" }),
        ("herd", new[] { "flag.fork.herd_slain", "flag.fork.herd_calmed" }),
        ("flock", new[] { "flag.fork.flock_exposed", "flag.fork.flock_turned", "flag.fork.flock_kin" }),
        ("queen", new[] { "flag.fork.queen_released", "flag.fork.queen_kept" }),
        ("vigil", new[] { "flag.rival.gate_kneel", "flag.rival.gate_draw" }),
    };

    private static readonly string[] DawnfireCards =
    {
        "ending.dawnfire.1", "ending.dawnfire.2", "ending.dawnfire.3", "ending.dawnfire.4",
    };

    private static readonly string[] EmbersCards =
    {
        "ending.embers.1", "ending.embers.2", "ending.embers.3", "ending.embers.4",
    };

    private static readonly string[] CreditCards =
    {
        "ending.credits.1", "ending.credits.2", "ending.credits.3",
    };

    protected override void OnReady()
    {
        EventBus.Instance?.Subscribe<StoryFlagChangedEvent>(OnFlagChanged);
    }

    public override void _ExitTree()
    {
        EventBus.Instance?.Unsubscribe<StoryFlagChangedEvent>(OnFlagChanged);
    }

    private void OnFlagChanged(StoryFlagChangedEvent e)
    {
        if (!e.Value || (e.Flag != DawnfireFlag && e.Flag != EmbersFlag) ||
            Flags() is not { } flags || flags.Has(CompleteFlag))
        {
            return;
        }

        PlayCards(Script(e.Flag == DawnfireFlag, CountAbsorbed(flags.Has), flags.Has), string.Empty);
    }

    /// <summary>The card list for an ending: the ending, the ember epilogue, one card per decided fork
    /// (when <paramref name="has"/> is given), the credits. Pure, so the branch table is unit-testable.</summary>
    public static string[] Script(bool dawnfire, int absorbed, System.Func<string, bool>? has = null)
    {
        var cards = new List<string>(dawnfire ? DawnfireCards : EmbersCards);
        cards.Add(EpilogueKey(dawnfire, absorbed));
        if (has != null)
        {
            cards.AddRange(ForkCards(has));
        }

        cards.AddRange(CreditCards);
        return cards.ToArray();
    }

    /// <summary>One epilogue key per fork whose flag is held, Act I to Act IV. A fork with several
    /// flags held (a debug state) plays its first answer only.</summary>
    public static string[] ForkCards(System.Func<string, bool> has)
    {
        var cards = new List<string>();
        foreach ((string slug, string[] flags) in ForkEpilogues)
        {
            for (int i = 0; i < flags.Length; i++)
            {
                if (has(flags[i]))
                {
                    cards.Add($"ending.epilogue.{slug}.{(char)('a' + i)}");
                    break;
                }
            }
        }

        return cards.ToArray();
    }

    /// <summary>The epilogue line keyed by how much of the fallen the player carried in.</summary>
    public static string EpilogueKey(bool dawnfire, int absorbed)
    {
        string band = absorbed == 0 ? "none" : absorbed <= 3 ? "some" : "all";
        return $"ending.{(dawnfire ? "dawnfire" : "embers")}.epilogue_{band}";
    }

    public static int CountAbsorbed(System.Func<string, bool> has)
    {
        int n = 0;
        foreach (string flag in AbsorbFlags)
        {
            if (has(flag))
            {
                n++;
            }
        }

        return n;
    }

    protected override void OnSequenceFinished()
    {
        Flags()?.Set(CompleteFlag);
    }

    private static StoryFlagsComponent? Flags() =>
        ServiceLocator.Instance is { } sl && sl.TryGet(out PlayerCharacter player)
            ? player.GetComponent<StoryFlagsComponent>()
            : null;
}
