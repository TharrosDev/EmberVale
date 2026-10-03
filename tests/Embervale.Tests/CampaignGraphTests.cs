using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Structural tests over <c>data/story/campaign_graph.json</c>, which <c>tools/gen_campaign.py</c>
/// computes from the final quest and dialogue <c>.tres</c> files (generated and hand-authored alike).
/// They run on whatever the specs currently produce, so they pass on the legacy-only campaign and get
/// stricter in effect as quests are added: every new quest is held to the same chain rules.
///
/// <para><b>The algorithms</b> (mirrored by <c>tools/campaign/graph.py</c>; keep the two in step):</para>
/// <list type="bullet">
/// <item><b>Reachable.</b> Fixpoint over F = <c>codeFlags</c>. A quest is reachable when its auto-start
/// flag is in F, or (a dialogue starts it, or it is an entry quest) and its prerequisite is empty or
/// reachable. A reachable quest adds its completion, start and objective flags to F; a dialogue adds
/// the flags it sets once it is live (no quest owns it, or an owning quest is reachable). A quest owns
/// a dialogue when one of its Talk objectives targets it.</item>
/// <item><b>Writer before reader (tolerant).</b> A flag read by quest Q needs a writer that is a code
/// flag, Q itself, an ancestor of Q, or a dialogue that is unowned or owned by Q or an ancestor. Q's
/// parents are its prerequisite plus whoever writes its auto-start flag; ancestors are the closure.
/// "Tolerant" because it ignores per-choice gating inside dialogues and objective order inside a quest.</item>
/// <item><b>Dead ends.</b> A completion flag of a main, generated or patched quest must be read by something
/// (a quest, a dialogue, or any other data/scene/code file) unless it is a designated terminal.</item>
/// </list>
/// </summary>
public class CampaignGraphTests
{
    private const int ObjectiveTalk = 3;
    private static readonly string Root = FindRepositoryRoot();
    private static readonly Graph Data = LoadGraph();

    // --- the graph and the files agree --------------------------------------------------------

    [Fact]
    public void GraphIsPresentAndCoversEveryQuestAndDialogueFile()
    {
        Assert.Equal(1, Data.Schema);
        Assert.NotEmpty(Data.Quests);

        // A stale graph (a spec edited, gen_campaign.py not re-run) must fail here rather than quietly
        // validate yesterday's campaign.
        var questFiles = Directory.EnumerateFiles(Path.Combine(Root, "data/quests"), "*.tres")
            .Select(p => ResourceId(File.ReadAllText(p))).OrderBy(s => s, StringComparer.Ordinal).ToList();
        var dialogueFiles = Directory.EnumerateFiles(Path.Combine(Root, "data/dialogue"), "*.tres")
            .Select(p => ResourceId(File.ReadAllText(p))).OrderBy(s => s, StringComparer.Ordinal).ToList();

        Assert.Equal(questFiles, Data.Quests.Select(q => q.Id).OrderBy(s => s, StringComparer.Ordinal).ToList());
        Assert.Equal(dialogueFiles, Data.Dialogues.Select(d => d.Id).OrderBy(s => s, StringComparer.Ordinal).ToList());
    }

    [Fact]
    public void GraphMatchesTheQuestFilesOnDisk()
    {
        foreach (GQuest quest in Data.Quests)
        {
            string text = File.ReadAllText(Path.Combine(Root, quest.File));
            string resource = text[text.IndexOf("[resource]", StringComparison.Ordinal)..];
            Assert.True(Value(resource, "AutoStartFlagId") == quest.AutoStart, $"{quest.Id}: graph auto-start is stale; run tools/gen_campaign.py");
            Assert.True(Value(resource, "CompletionFlagId") == quest.CompletionFlag, $"{quest.Id}: graph completion flag is stale");
            Assert.True(Regex.Matches(Regex.Match(resource, @"^Objectives = .*$", RegexOptions.Multiline).Value, "SubResource").Count
                        == quest.Objectives.Count, $"{quest.Id}: graph objective count is stale");
        }
    }

    // --- chain rules ----------------------------------------------------------------------------

    [Fact]
    public void EveryMainQuestIsReachableFromNewGame()
    {
        HashSet<string> reachable = ReachableQuests();
        foreach (GQuest quest in Data.Quests.Where(q => q.IsMain))
        {
            Assert.True(reachable.Contains(quest.Id), $"{quest.Id} is a main quest but nothing leads to it from New Game");
        }
    }

    [Fact]
    public void ChainsAreContinuous_NoDeadEndCompletionFlags()
    {
        var terminal = new HashSet<string>(Data.TerminalFlags, StringComparer.Ordinal);
        foreach (GQuest quest in Data.Quests.Where(InScope))
        {
            string flag = quest.CompletionFlag;
            if (flag.Length == 0 || terminal.Contains(flag))
            {
                continue;
            }

            Data.Flags.TryGetValue(flag, out FlagNode? node);
            bool consumed = node != null
                && (node.Readers.Any(r => !(r.Kind == "quest" && r.Id == quest.Id)) || node.ExternalRefs.Count > 0);
            Assert.True(consumed, $"{quest.Id} sets {flag} but nothing reads it: a chain gap (add the next link or list it as a terminal)");
        }
    }

    [Fact]
    public void TerminalFlagsAreKnownFlags()
    {
        // A typo in the terminal list would silently exempt nothing; an unknown name is always a mistake.
        var known = new HashSet<string>(Data.Quests.Select(q => q.CompletionFlag), StringComparer.Ordinal);
        known.UnionWith(Data.CodeFlags);
        foreach (string flag in Data.TerminalFlags)
        {
            Assert.True(known.Contains(flag), $"terminal flag '{flag}' is not a quest completion flag or a code flag");
        }
    }

    [Fact]
    public void EveryFlagAQuestReadsHasAWriterThatCanRunFirst()
    {
        Dictionary<string, HashSet<string>> owners = DialogueOwners();
        Dictionary<string, HashSet<string>> ancestors = Ancestors(owners);
        var code = new HashSet<string>(Data.CodeFlags, StringComparer.Ordinal);

        foreach (GQuest quest in Data.Quests.Where(InScope))
        {
            HashSet<string> allowed = new(ancestors[quest.Id]) { quest.Id };
            foreach (Site read in quest.Reads)
            {
                if (code.Contains(read.Flag))
                {
                    continue;
                }

                Data.Flags.TryGetValue(read.Flag, out FlagNode? node);
                bool ok = node != null && node.Writers.Any(w => w.Kind == "quest"
                    ? allowed.Contains(w.Id)
                    : !owners.TryGetValue(w.Id, out HashSet<string>? own) || own.Count == 0 || own.Overlaps(allowed));
                Assert.True(ok, $"{quest.Id} reads {read.Flag} ({read.Via}) but no writer can run before it");
            }
        }
    }

    [Fact]
    public void EveryFlagAGeneratedDialogueReadsHasAWriter()
    {
        var code = new HashSet<string>(Data.CodeFlags, StringComparer.Ordinal);
        foreach (GDialogue dialogue in Data.Dialogues.Where(d => d.Source == "generated"))
        {
            foreach (string flag in dialogue.FlagsRead)
            {
                Assert.True(code.Contains(flag) || (Data.Flags.TryGetValue(flag, out FlagNode? node) && node.Writers.Count > 0),
                    $"{dialogue.Id} reads {flag} but nothing writes it");
            }
        }
    }

    [Fact]
    public void EveryForkFlagIsSetAndHasAConsequence()
    {
        foreach (ForkNode fork in Data.Forks)
        {
            Assert.True(fork.Writers.Count > 0, $"{fork.Flag} is never set");
            Assert.True(fork.Readers.Count > 0 || fork.ExternalRefs.Count > 0, $"{fork.Flag} is set but nothing reacts to it");
        }
    }

    [Fact]
    public void TheLedgerQuestIsMarkedLedger()
    {
        GQuest? ledger = Data.Quests.FirstOrDefault(q => q.Id == "quest.main.gathering");
        Assert.NotNull(ledger);
        Assert.True(ledger!.IsLedger, "quest.main.gathering is the hidden Act II umbrella and must carry IsLedger");
        Assert.True(ledger.IsMain);
    }

    // --- locale ---------------------------------------------------------------------------------

    [Fact]
    public void EveryLocaleKeyTheCampaignReferencesExists()
    {
        HashSet<string> catalogue = CatalogueKeys();
        foreach (GQuest quest in Data.Quests.Where(InScope))
        {
            foreach (string key in quest.LocKeys)
            {
                Assert.True(catalogue.Contains(key), $"{quest.Id}: locale key '{key}' is not in strings.csv");
            }
        }

        foreach (GDialogue dialogue in Data.Dialogues.Where(d => d.Source != "hand"))
        {
            foreach (string key in dialogue.LocKeys)
            {
                Assert.True(catalogue.Contains(key), $"{dialogue.Id}: locale key '{key}' is not in strings.csv");
            }
        }
    }

    [Fact]
    public void ThePaleConcordIsNamedOnlyUnderPaleKeys()
    {
        // docs/NOW.md invariant 34: the name must not reach a player before the reveal. The atlas gate
        // keys on the pale. prefix, so any other key that carries the phrase is a spoiler.
        foreach ((string key, string value) in CatalogueRows())
        {
            if (!key.StartsWith("pale.", StringComparison.Ordinal))
            {
                Assert.False(value.Contains("pale concord", StringComparison.OrdinalIgnoreCase),
                    $"'{key}' names the Pale Concord outside a pale.* key");
            }
        }
    }

    [Fact]
    public void NoCatalogueKeyIsDuplicated()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach ((string key, _) in CatalogueRows())
        {
            Assert.True(seen.Add(key), $"duplicate locale key '{key}'");
        }
    }

    // --- algorithms -------------------------------------------------------------------------------

    private static bool InScope(GQuest quest) => quest.IsMain || quest.Source != "hand";

    private static Dictionary<string, HashSet<string>> DialogueOwners()
    {
        var owners = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (GQuest quest in Data.Quests)
        {
            foreach (GObjective objective in quest.Objectives.Where(o => o.Type == ObjectiveTalk && o.Target.Length > 0))
            {
                if (!owners.TryGetValue(objective.Target, out HashSet<string>? set))
                {
                    owners[objective.Target] = set = new HashSet<string>(StringComparer.Ordinal);
                }

                set.Add(quest.Id);
            }
        }

        return owners;
    }

    private static HashSet<string> ReachableQuests()
    {
        Dictionary<string, HashSet<string>> owners = DialogueOwners();
        var startedByDialogue = new HashSet<string>(Data.Dialogues.SelectMany(d => d.QuestsStarted), StringComparer.Ordinal);
        var entry = new HashSet<string>(Data.EntryQuests, StringComparer.Ordinal);
        var flags = new HashSet<string>(Data.CodeFlags, StringComparer.Ordinal);
        var done = new HashSet<string>(StringComparer.Ordinal);
        var live = new HashSet<string>(StringComparer.Ordinal);

        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (GQuest quest in Data.Quests.Where(q => !done.Contains(q.Id)))
            {
                bool prerequisiteOk = quest.Prerequisite.Length == 0 || done.Contains(quest.Prerequisite);
                bool autoStarts = quest.AutoStart.Length > 0 && flags.Contains(quest.AutoStart);
                if (autoStarts || ((startedByDialogue.Contains(quest.Id) || entry.Contains(quest.Id)) && prerequisiteOk))
                {
                    done.Add(quest.Id);
                    foreach (Site write in quest.Writes.Where(w => w.Via != "fail"))
                    {
                        flags.Add(write.Flag);
                    }

                    changed = true;
                }
            }

            foreach (GDialogue dialogue in Data.Dialogues.Where(d => !live.Contains(d.Id)))
            {
                if (!owners.TryGetValue(dialogue.Id, out HashSet<string>? own) || own.Count == 0 || own.Overlaps(done))
                {
                    live.Add(dialogue.Id);
                    flags.UnionWith(dialogue.FlagsSet);
                    changed = true;
                }
            }
        }

        return done;
    }

    private static Dictionary<string, HashSet<string>> Ancestors(Dictionary<string, HashSet<string>> owners)
    {
        var questIds = new HashSet<string>(Data.Quests.Select(q => q.Id), StringComparer.Ordinal);
        var writersOf = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach ((string flag, FlagNode node) in Data.Flags)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (FlagRef writer in node.Writers)
            {
                if (writer.Kind == "quest")
                {
                    set.Add(writer.Id);
                }
                else if (owners.TryGetValue(writer.Id, out HashSet<string>? own))
                {
                    set.UnionWith(own);
                }
            }

            writersOf[flag] = set;
        }

        var parents = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (GQuest quest in Data.Quests)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            if (questIds.Contains(quest.Prerequisite))
            {
                set.Add(quest.Prerequisite);
            }

            if (quest.AutoStart.Length > 0 && writersOf.TryGetValue(quest.AutoStart, out HashSet<string>? writers))
            {
                set.UnionWith(writers);
            }

            set.Remove(quest.Id);
            parents[quest.Id] = set;
        }

        var result = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (GQuest quest in Data.Quests)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var stack = new Stack<string>(parents[quest.Id]);
            while (stack.Count > 0)
            {
                string next = stack.Pop();
                if (seen.Add(next) && parents.TryGetValue(next, out HashSet<string>? up))
                {
                    foreach (string parent in up)
                    {
                        stack.Push(parent);
                    }
                }
            }

            result[quest.Id] = seen;
        }

        return result;
    }

    // --- files ------------------------------------------------------------------------------------

    private static string ResourceId(string tres)
    {
        string resource = tres[tres.IndexOf("[resource]", StringComparison.Ordinal)..];
        return Value(resource, "Id");
    }

    private static string Value(string section, string name)
    {
        Match match = Regex.Match(section, "^" + name + @" = ""([^""]*)""", RegexOptions.Multiline);
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    private static IEnumerable<(string Key, string Value)> CatalogueRows()
    {
        foreach (string line in File.ReadAllLines(Path.Combine(Root, "data/locale/strings.csv")))
        {
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            int comma = line.IndexOf(',');
            if (comma > 0 && line[..comma] != "keys")
            {
                yield return (line[..comma], line[(comma + 1)..]);
            }
        }
    }

    private static HashSet<string> CatalogueKeys() => CatalogueRows().Select(r => r.Key).ToHashSet(StringComparer.Ordinal);

    private static Graph LoadGraph()
    {
        string path = Path.Combine(Root, "data/story/campaign_graph.json");
        Assert.True(File.Exists(path), "data/story/campaign_graph.json is missing; run python tools/gen_campaign.py");
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        return JsonSerializer.Deserialize<Graph>(File.ReadAllText(path), options)!;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "project.godot")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }

    // --- JSON shape (tools/campaign/graph.py) -----------------------------------------------------

    private sealed class Graph
    {
        public int Schema { get; set; }
        public List<string> CodeFlags { get; set; } = new();
        public List<string> TerminalFlags { get; set; } = new();
        public List<string> EntryQuests { get; set; } = new();
        public List<GQuest> Quests { get; set; } = new();
        public List<GDialogue> Dialogues { get; set; } = new();
        public Dictionary<string, FlagNode> Flags { get; set; } = new();
        public List<ForkNode> Forks { get; set; } = new();
    }

    private sealed class GQuest
    {
        public string Id { get; set; } = "";
        public string File { get; set; } = "";
        public string Source { get; set; } = "";
        public bool IsMain { get; set; }
        public bool IsLedger { get; set; }
        public string AutoStart { get; set; } = "";
        public string CompletionFlag { get; set; } = "";
        public string Prerequisite { get; set; } = "";
        public List<GObjective> Objectives { get; set; } = new();
        public List<Site> Writes { get; set; } = new();
        public List<Site> Reads { get; set; } = new();
        public List<string> LocKeys { get; set; } = new();
    }

    private sealed class GObjective
    {
        public int Type { get; set; }
        public string Target { get; set; } = "";
    }

    private sealed class Site
    {
        public string Flag { get; set; } = "";
        public string Via { get; set; } = "";
    }

    private sealed class GDialogue
    {
        public string Id { get; set; } = "";
        public string Source { get; set; } = "";
        public List<string> FlagsSet { get; set; } = new();
        public List<string> FlagsRead { get; set; } = new();
        public List<string> QuestsStarted { get; set; } = new();
        public List<string> LocKeys { get; set; } = new();
    }

    private sealed class FlagRef
    {
        public string Kind { get; set; } = "";
        public string Id { get; set; } = "";
        public string Via { get; set; } = "";
    }

    private sealed class FlagNode
    {
        public List<FlagRef> Writers { get; set; } = new();
        public List<FlagRef> Readers { get; set; } = new();
        public List<string> ExternalRefs { get; set; } = new();
    }

    private sealed class ForkNode
    {
        public string Flag { get; set; } = "";
        public List<FlagRef> Writers { get; set; } = new();
        public List<FlagRef> Readers { get; set; } = new();
        public List<string> ExternalRefs { get; set; } = new();
    }
}
