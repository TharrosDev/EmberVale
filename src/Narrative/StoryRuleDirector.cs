using System.Collections.Generic;
using Embervale.Companions;
using Embervale.Core.Diagnostics;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Dialogue;
using Embervale.Player;
using Embervale.World;
using Godot;

namespace Embervale.Narrative;

/// <summary>
/// Session node that evaluates the story rules (<see cref="StoryRuleEngine"/>) and keeps the derived
/// party-mirror flags honest. Rules come from <c>data/story/rules/*.json</c> plus the built-in
/// <see cref="StoryRuleData.BuiltIn"/> set. It re-evaluates on every story-flag change, region change
/// and game load, and fires <c>opening_finished</c> rules on <see cref="OpeningFinishedEvent"/>.
///
/// <para>Everything it does is a derivation: it sets flags through <see cref="StoryFlagsComponent"/>
/// (idempotent) and never clears a rule-set flag, so nothing here needs saving; the flags it wrote are
/// in the save and a load re-derives the rest. A load catch-up (and anything the quest log's
/// catch-up sets during that same load) is <b>silent</b>: flags are set, beats are not announced.</para>
///
/// <para>Re-entrancy: setting a flag publishes a synchronous event that lands back here. A nested call
/// only marks the outer loop dirty; the outer loop re-runs until nothing changes (capped).</para>
/// </summary>
public partial class StoryRuleDirector : Node
{
    private const int MaxOuterPasses = 8;

    private StoryRuleEngine _engine = new(StoryRuleData.BuiltIn());
    private bool _running;
    private bool _dirty;
    private bool _pendingOpening;
    private bool _catchUpWindow;

    public StoryRuleEngine Engine => _engine;

    public override void _Ready()
    {
        var errors = new List<string>();
        var rules = new List<StoryRule>(StoryRuleData.BuiltIn());
        rules.AddRange(StoryDataFiles.LoadRules(errors));
        _engine = new StoryRuleEngine(rules);
        foreach (string error in errors)
        {
            Log.Warn($"Story rules: {error}");
        }

        Log.Info($"Story rules: {_engine.Rules.Count} rule(s) loaded.");

        EventBus.Instance?.Subscribe<StoryFlagChangedEvent>(OnFlag);
        EventBus.Instance?.Subscribe<RegionChangedEvent>(OnRegion);
        EventBus.Instance?.Subscribe<GameLoadingEvent>(OnLoading);
        EventBus.Instance?.Subscribe<GameLoadedEvent>(OnLoaded);
        EventBus.Instance?.Subscribe<OpeningFinishedEvent>(OnOpeningFinished);
        EventBus.Instance?.Subscribe<CompanionRecruitedEvent>(OnRecruited);
        EventBus.Instance?.Subscribe<CompanionDismissedEvent>(OnDismissed);
    }

    public override void _ExitTree()
    {
        EventBus.Instance?.Unsubscribe<StoryFlagChangedEvent>(OnFlag);
        EventBus.Instance?.Unsubscribe<RegionChangedEvent>(OnRegion);
        EventBus.Instance?.Unsubscribe<GameLoadingEvent>(OnLoading);
        EventBus.Instance?.Unsubscribe<GameLoadedEvent>(OnLoaded);
        EventBus.Instance?.Unsubscribe<OpeningFinishedEvent>(OnOpeningFinished);
        EventBus.Instance?.Unsubscribe<CompanionRecruitedEvent>(OnRecruited);
        EventBus.Instance?.Unsubscribe<CompanionDismissedEvent>(OnDismissed);
    }

    private void OnFlag(StoryFlagChangedEvent e) => Run(_catchUpWindow);

    private void OnRegion(RegionChangedEvent e) => Run(_catchUpWindow);

    private void OnRecruited(CompanionRecruitedEvent e) => Run(_catchUpWindow);

    private void OnDismissed(CompanionDismissedEvent e) => Run(_catchUpWindow);

    // Opens at the start of the restore so flags changed while saveables load are silent too.
    private void OnLoading(GameLoadingEvent e)
    {
        _catchUpWindow = true;
        CallDeferred(nameof(EndCatchUp));
    }

    private void OnLoaded(GameLoadedEvent e)
    {
        // The quest log's catch-up (and its auto-starts) set flags during this same event; keep the
        // whole load silent until the frame ends.
        _catchUpWindow = true;
        Run(silent: true);
        CallDeferred(nameof(EndCatchUp));
    }

    private void EndCatchUp() => _catchUpWindow = false;

    private void OnOpeningFinished(OpeningFinishedEvent e)
    {
        _pendingOpening = true;
        Run(_catchUpWindow);
    }

    private void Run(bool silent)
    {
        if (_running)
        {
            _dirty = true;
            return;
        }

        if (Flags() is not { } flags)
        {
            return;
        }

        _running = true;
        try
        {
            for (int pass = 0; pass < MaxOuterPasses; pass++)
            {
                _dirty = false;
                MirrorParty(flags);
                if (_pendingOpening)
                {
                    _pendingOpening = false;
                    _engine.Trigger(StoryRule.OpeningFinished, flags.Has, flags.Set, PublishBeat);
                }

                _engine.Evaluate(flags.Has, flags.Set, PublishBeat, silent || _catchUpWindow);
                if (!_dirty)
                {
                    break;
                }
            }
        }
        finally
        {
            _running = false;
        }
    }

    /// <summary>Makes <c>flag.party.&lt;id&gt;</c> equal "in the party now" for every authored companion.</summary>
    private static void MirrorParty(StoryFlagsComponent flags)
    {
        if (ServiceLocator.Instance is not { } sl || !sl.TryGet(out CompanionRoster roster))
        {
            return;
        }

        var all = new List<string>();
        foreach (CompanionResource companion in CompanionDatabase.All)
        {
            all.Add(companion.Id);
        }

        var party = new HashSet<string>(roster.RecruitedIds);
        (List<string> set, List<string> clear) = PartyFlagMirror.Plan(all, party, flags.Has);
        foreach (string flag in set)
        {
            flags.Set(flag);
        }

        foreach (string flag in clear)
        {
            flags.Clear(flag);
        }
    }

    private static void PublishBeat(string key) => EventBus.Instance?.Publish(new StoryBeatEvent(key));

    private static StoryFlagsComponent? Flags() =>
        ServiceLocator.Instance is { } sl && sl.TryGet(out PlayerCharacter player)
            ? player.GetComponent<StoryFlagsComponent>()
            : null;
}
