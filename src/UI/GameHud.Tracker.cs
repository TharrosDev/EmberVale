using Embervale.Core;
using Embervale.Core.Events;
using Embervale.Corruption;
using Embervale.Enemies;
using Embervale.Entities;
using Embervale.Localization;
using Embervale.Magic;
using Embervale.Player;
using Embervale.Progression;
using Embervale.Quests;
using Embervale.Stats;
using Embervale.World;
using Godot;

namespace Embervale.UI;

/// <summary>The quest tracker of <see cref="GameHud"/>: the tracked quest's heading, objective rows,
/// destination readout, deadline and lingering hint.</summary>
public partial class GameHud
{
    private PanelContainer _questPanel = null!;
    private VBoxContainer _questList = null!;
    private Label _questWhere = null!;
    private Label _questClock = null!;
    private Label _questHeader = null!;
    private Label? _questHint;
    private int _questCurrentObjective = -1;
    private string _questObjectiveKey = string.Empty;
    private readonly ObjectiveDwell _questDwell = new();
    private readonly TrackerTitleFlash _questFlash = new();

    private bool _questShapeKnown;
    private string _questShapeId = string.Empty;
    private int _questShapeCount;
    private int[] _questShapeCounts = System.Array.Empty<int>();
    private byte[] _questShapeFlags = System.Array.Empty<byte>();
    private float _questFlagTimer;

    /// <summary>How often the tracker re-asks each objective whether it is in the player's branch and
    /// active. Those two answers move on a story flag, which raises no event the tracker can hear, so
    /// they are polled; each ask rebuilds the quest's objective list, so they are polled four times
    /// a second rather than sixty. The counts beside them are compared every frame.</summary>
    private const float QuestFlagInterval = 0.25f;
    private int _questHeaderShown = -1;
    private int _questWhereMode = -1; // 0 hidden, 1 distance readout, 2 place name
    private float _questWhereDistance = float.NaN;
    private string? _questWhereCardinal;
    private string? _questWhereRealm;
    private bool _questPlaceKnown;
    private string? _questPlace;
    private int _questClockShown = int.MinValue;
    private int _questClockHot = -1;

    private void InvalidateTrackerShown()
    {
        _questShapeKnown = false;
        _questHeaderShown = -1;
        _questWhereMode = -1;
        _questPlaceKnown = false;
        _questClockShown = int.MinValue;
        _questClockHot = -1;
    }

    // A story flag is what moves a quest between branches: re-ask the objectives on the next tick
    // instead of waiting out the poll. The poll stays, for a flag that changes with no event.
    private void OnStoryFlagChanged(Embervale.Dialogue.StoryFlagChangedEvent e) => _questFlagTimer = 0f;

    /// <summary>Whether the current objective's hint is showing under it. Read by the screenshot harness.</summary>
    public bool TrackerHintVisible => _questHint is { Visible: true };

    /// <summary>Adds time to the tracker's same-objective clock, so the harness can photograph the hint without
    /// waiting out <see cref="TrackerRules.HintDelaySeconds"/>. The clock is the real one; this only nudges it.</summary>
    public void AdvanceTrackerDwell(float seconds) => _questDwell.Tick(_questObjectiveKey, seconds);

    private void BuildQuestTracker()
    {
        // The spine carries the tracked quest's priority, matching the journal.
        _questPanel = Ignore(UiTheme.Band(UiTheme.QuestMain));
        _questPanel.Visible = false;
        _questPanel.CustomMinimumSize = new Vector2(280, 0);
        _layout.TopRight.AddChild(_questPanel);

        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", UiTheme.SpaceSm);
        _questHeader = UiTheme.Header(Loc.T("hud.quest"));
        col.AddChild(_questHeader);
        _questList = new VBoxContainer();
        _questList.AddThemeConstantOverride("separation", UiTheme.SpaceSm);
        col.AddChild(_questList);

        // Distance + bearing to the tracked objective. Its own label under the objective rows, so
        // the rows can stay on their rebuild-on-change signature while this updates as you walk.
        _questWhere = UiTheme.Caption("", UiTheme.Accent);
        _questWhere.Visible = false;
        col.AddChild(_questWhere);

        // The deadline on a timed quest (41C). Its own label for the same reason as the one above:
        // it changes every frame, and the objective rows must not be rebuilt at that rate (§50).
        _questClock = UiTheme.Caption("", UiTheme.Dim);
        _questClock.Visible = false;
        col.AddChild(_questClock);

        WrapPadded(_questPanel, col);
    }

    private void UpdateQuest(float delta)
    {
        // 39.5B: one authority for "which quest am I on" — see QuestLogComponent.Tracked.
        QuestProgress? active = _player?.GetComponent<QuestLogComponent>()?.Tracked;

        // A ledger quest is an umbrella record: it is never tracked by the log's own rules, but a
        // dialogue's TrackQuest can name one, and the tracker must not become a second place that
        // decides otherwise.
        if (active == null || active.Quest.IsLedger)
        {
            _questPanel.Visible = false;
            _questShapeKnown = false;
            _questFlash.Observe(null, delta);
            _questDwell.Tick(null, delta);
            return;
        }

        // Rebuild the tracker rows only when the tracked quest's shape/progress changes.
        //
        // ⚠️ THE BRANCH STATE IS PART OF THE SHAPE (41D), AND LEAVING IT OUT IS 41B's DEFECT AGAIN.
        // A story flag changing swaps which objectives are drawn WITHOUT moving a single count — so
        // a signature of counts alone is stale exactly when the player has just made the choice the
        // whole quest is about, and the tracker would keep listing the path they turned down. The
        // journal's version of this is a missing event subscription; this one is a missing term in a
        // cache key, and neither is visible to a build, a test or a validator.
        if (QuestShapeChanged(active, delta))
        {
            _questCurrentObjective = ObjectiveFocusRules.Current(QuestProgressViews.States(active));
            _questObjectiveKey = _questCurrentObjective >= 0 ? $"{active.Quest.Id}:{_questCurrentObjective}" : string.Empty;
            _questPlaceKnown = false; // the current objective, and so its place, may have moved
            RebuildQuestRows(active);
        }

        // "Now tracking" holds the header for a few seconds after the tracked quest changes, and the
        // current objective's hint appears once the player has sat on the same step for a while.
        _questFlash.Observe(active.Quest.Id, delta);
        int header = _questFlash.Active ? 1 : 0;
        if (header != _questHeaderShown)
        {
            _questHeaderShown = header;
            _questHeader.Text = Loc.T(header == 1 ? "questui.now_tracking" : "hud.quest");
        }

        _questDwell.Tick(_questObjectiveKey, delta);
        if (_questHint != null)
        {
            _questHint.Visible = TrackerRules.ShouldShowHint(true, _questDwell.Seconds);
        }

        UpdateQuestDestination();
        UpdateQuestClock(active);
        _questPanel.Visible = true;
    }

    /// <summary>
    /// Whether the tracked quest's shape differs from the one the rows were built from, recording the
    /// new shape when it does. The shape is the quest, and per objective its count, whether it is in
    /// the player's branch and whether it is active - term for term what the old string signature
    /// joined, compared in place so an unchanged tracker costs no allocation. The quest and the counts
    /// are compared every frame; the branch and active flags on <see cref="QuestFlagInterval"/>, and
    /// at once whenever the quest or a count has moved.
    /// </summary>
    private bool QuestShapeChanged(QuestProgress active, float delta)
    {
        int[] counts = active.Counts;
        int n = counts.Length;
        bool changed = !_questShapeKnown || n != _questShapeCount || active.Quest.Id != _questShapeId;

        if (_questShapeCounts.Length < n)
        {
            _questShapeCounts = new int[n];
            _questShapeFlags = new byte[n];
            changed = true;
        }

        for (int i = 0; i < n; i++)
        {
            if (counts[i] != _questShapeCounts[i])
            {
                changed = true;
                _questShapeCounts[i] = counts[i];
            }
        }

        _questFlagTimer -= delta;
        if (changed || _questFlagTimer <= 0f)
        {
            _questFlagTimer = QuestFlagInterval;
            for (int i = 0; i < n; i++)
            {
                byte flags = (byte)((active.IsObjectiveInBranch(i) ? 1 : 0) | (active.IsObjectiveActive(i) ? 2 : 0));
                if (flags != _questShapeFlags[i])
                {
                    changed = true;
                    _questShapeFlags[i] = flags;
                }
            }
        }

        _questShapeKnown = true;
        _questShapeCount = n;
        _questShapeId = active.Quest.Id;
        return changed;
    }

    /// <summary>
    /// How far the tracked objective is and which way (§16, §21) — "320 m · NW" under the objectives.
    ///
    /// Reads the compass strip's already-resolved target rather than locating one itself, so the
    /// number under the tracker and the marker on the strip are the same point by construction. Lives
    /// outside the signature-driven rebuild because it changes every time the player takes a step,
    /// and rebuilding the objective rows at walking pace to update one label would be the "recreating
    /// nodes every frame" §50 forbids.
    /// </summary>
    private void UpdateQuestDestination()
    {
        if (_compass.ObjectiveTarget is { } target &&
            _player?.Body is { } body && IsInstanceValid(body))
        {
            Vector3 offset = target - body.GlobalPosition;
            float metres = new Vector2(offset.X, offset.Z).Length();
            string cardinalKey = CompassMath.CardinalKey(CompassMath.BearingTo(offset.X, offset.Z));
            string? realm = _compass.PortalRegionName;

            // The readout shows whole metres (or a tenth of a kilometre) and one of eight compass
            // points, so it is rebuilt when one of those moves and not on every step in between.
            float shown = metres < 1000f
                ? System.MathF.Round(metres)
                : 100000f + System.MathF.Round(metres / 100f, System.MidpointRounding.AwayFromZero);
            if (_questWhereMode != 1 || shown != _questWhereDistance ||
                !ReferenceEquals(cardinalKey, _questWhereCardinal) || realm != _questWhereRealm)
            {
                _questWhereMode = 1;
                _questWhereDistance = shown;
                _questWhereCardinal = cardinalKey;
                _questWhereRealm = realm;

                (string value, string unitKey) = CompassMath.Distance(metres);
                string cardinal = Loc.T(cardinalKey);

                // A destination in another realm points at this realm's door toward it, and says so.
                _questWhere.Text = realm != null
                    ? Loc.TF("questui.destination_portal", realm, value, Loc.T(unitKey), cardinal)
                    : Loc.TF("hud.quest.destination", value, Loc.T(unitKey), cardinal);
                UiLive.FontColor(_questWhere, UiTheme.Accent);
                _questWhere.Visible = true;
            }

            return;
        }

        // ⚠️ NO POSITION IS NOT NO INFORMATION (39.5C).
        //
        // A destination across a region boundary has no resolvable position — its cell is not
        // resident and the player may never have stood there — so there is no distance and no
        // bearing to print. Naming the place is still worth the line: "Dragon Roost" tells the
        // player where they are going, which is most of what the readout was for, and it is the
        // difference between the tracker looking incomplete and looking broken. 39.5B shipped this
        // row and it had never once rendered, because until `LocationId` existed there was nothing
        // for it to fall back to.
        //
        // The name belongs to the current objective, so it is resolved when the quest's shape
        // changes (UpdateQuest clears _questPlaceKnown) rather than looked up every frame.
        if (!_questPlaceKnown)
        {
            _questPlaceKnown = true;
            _questPlace = TrackedDestinationName();
            _questWhereMode = -1;
        }

        int mode = _questPlace != null ? 2 : 0;
        if (mode != _questWhereMode)
        {
            _questWhereMode = mode;
            _questWhere.Visible = _questPlace != null;
            if (_questPlace != null)
            {
                _questWhere.Text = _questPlace;
                UiLive.FontColor(_questWhere, UiTheme.Dim);
            }
        }
    }

    /// <summary>
    /// The countdown on a timed quest (41C) — "0:47" under the destination, turning hot in the last
    /// ten seconds like the world-event banner's timer it is modelled on.
    ///
    /// ⚠️ <b>Outside the signature-driven rebuild, deliberately.</b> the quest shape (<c>QuestShapeChanged</c>) is built
    /// from the objective counts precisely so the rows are rebuilt only when they change; a value
    /// that changes every frame folded into it would recreate the tracker's nodes every frame, which
    /// is what §50 forbids and what the destination readout above already avoids.
    /// </summary>
    private void UpdateQuestClock(QuestProgress active)
    {
        if (!active.IsTimed)
        {
            _questClock.Visible = false;
            return;
        }

        int seconds = Mathf.Max(0, Mathf.CeilToInt(active.SecondsLeft));
        if (seconds != _questClockShown)
        {
            _questClockShown = seconds;
            _questClock.Text = Loc.TF("hud.quest.time_left", seconds / 60, (seconds % 60).ToString("00"));
        }

        int hot = seconds <= 10 ? 1 : 0;
        if (hot != _questClockHot)
        {
            _questClockHot = hot;
            UiLive.FontColor(_questClock, hot == 1 ? UiTheme.AccentHot : UiTheme.Dim);
        }

        _questClock.Visible = true;
    }

    /// <summary>The tracked quest's current objective's place name, or null when it has none. Goes through
    /// <see cref="QuestPlaces.PlaceName"/>, so a Reach or Defend objective (whose place is its
    /// <c>TargetId</c>, not a <c>LocationId</c>) names its destination like every other type.</summary>
    private string? TrackedDestinationName()
    {
        if (_player?.GetComponent<QuestLogComponent>()?.Tracked is not { } progress ||
            QuestProgressViews.CurrentObjective(progress, out _) is not { } objective)
        {
            return null;
        }

        return QuestPlaces.PlaceName(objective);
    }

    /// <summary>Structured tracker rows (30.5D): the chapter, an accent title, then one line per objective —
    /// complete objectives tick over to dead-green so progress reads at a glance. Optional objectives carry
    /// an "Optional" tag; the current objective carries its hint, hidden until the player has lingered.</summary>
    private void RebuildQuestRows(QuestProgress progress)
    {
        foreach (Node child in _questList.GetChildren())
        {
            _questList.RemoveChild(child);
            child.QueueFree();
        }

        _questHint = null;

        // The spine carries the quest's priority, matching the journal: ember for the main thread, the
        // errand colour for a side quest. The band's stylebox is its own instance, so recolouring it
        // touches nothing else.
        Color tint = progress.Quest.IsMainQuest ? UiTheme.QuestMain : UiTheme.QuestSide;
        if (_questPanel.GetThemeStylebox("panel") is StyleBoxFlat spine)
        {
            spine.BorderColor = tint;
        }

        // The chapter and the title are one heading, so they sit close; the objectives below are SpaceSm away.
        var heading = new VBoxContainer();
        heading.AddThemeConstantOverride("separation", UiTheme.LineGap);
        _questList.AddChild(heading);

        // Which chapter this is, above the title. Absent text means no label rather than a raw key.
        if (progress.Quest.ChapterKey.Length > 0 &&
            JournalIndexRules.FirstResolving(
                JournalIndexRules.ChapterTitleKeys(progress.Quest.ChapterKey), Loc.Has) is { } chapterKey)
        {
            Label chapter = UiTheme.Caption(Loc.T(chapterKey), UiTheme.Dim);
            chapter.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            heading.AddChild(chapter);
        }

        // The title is the thing you glance at, so it is Display-faced and wraps rather than
        // clipping — a truncated quest name is a quest you cannot identify.
        Label title = UiTheme.Body(Loc.T(progress.Quest.Title), tint);
        UiTheme.ApplyType(title, UiTheme.FontRole.Display, UiTheme.BodyFontSize);
        title.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        heading.AddChild(title);

        var objectives = progress.Quest.ObjectiveList();
        for (int i = 0; i < objectives.Count; i++)
        {
            // ⚠️ 41D, and the two states are not the same state. Out of branch = the player is on
            // the other path, so the row is not drawn at all. In branch but not active = locked
            // behind an earlier step on a SequentialObjectives quest, which IS drawn, dimmed and
            // padlocked — an errand that silently grows rows as you finish them reads as a bug.
            if (!progress.IsObjectiveInBranch(i))
            {
                continue;
            }

            bool locked = !progress.IsObjectiveActive(i);
            int required = Mathf.Max(1, objectives[i].RequiredCount);
            int have = progress.Counts[i];
            bool done = have >= objectives[i].RequiredCount;

            // ⚠️ The objective used to be a single Caption with the count glued on the end and two
            // leading spaces for indent — so a long objective wrapped its own progress count onto the
            // next line, and the count was the same weight as the words. It is a row now: the text
            // wraps, the count holds the right edge.
            // One block per objective (row, optional tag, bar, hint) so its parts hug each other and the
            // objectives stand apart from one another.
            var block = new VBoxContainer();
            block.AddThemeConstantOverride("separation", UiTheme.SpaceXs);
            _questList.AddChild(block);

            var line = new HBoxContainer();
            line.AddThemeConstantOverride("separation", UiTheme.SpaceSm);

            TextureRect bullet = UiIcon.Create(
                locked ? UiIcon.Kind.Lock : done ? UiIcon.Kind.Quest : UiIcon.Kind.Waypoint,
                12f,
                done ? UiTheme.QuestComplete : locked ? UiTheme.Dim : UiTheme.Text);
            line.AddChild(bullet);

            Label text = UiTheme.Caption(
                Loc.T(objectives[i].ShortLabel()),
                done ? UiTheme.QuestComplete : locked ? UiTheme.Dim : UiTheme.Text);
            text.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            text.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            // Without a floor the chip and count win the row and the objective wraps a word per line.
            text.CustomMinimumSize = new Vector2(110f, 0f);
            line.AddChild(text);

            // A 1-of-1 objective's "0/1" is noise — the bullet already says done or not.
            if (objectives[i].RequiredCount > 1)
            {
                line.AddChild(UiTheme.Caption(
                    $"{have}/{objectives[i].RequiredCount}",
                    done ? UiTheme.QuestComplete : UiTheme.Dim));
            }

            block.AddChild(line);

            // Optional steps are told apart by a word, not by being dimmer. The tag sits on its own line,
            // under the text it describes, so it no longer competes with the wrapped objective and its count.
            if (objectives[i].IsOptional)
            {
                var tag = new MarginContainer { SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin };
                tag.AddThemeConstantOverride("margin_left", 12 + UiTheme.SpaceSm);
                tag.AddChild(UiTheme.Chip(Loc.T("questui.chip.optional_short"), UiTheme.Dim));
                block.AddChild(tag);
            }

            // A bar under any objective that counts to more than one (37.5B). "3/10 pelts" is a
            // number you have to read; a bar is a glance. Pointless for a 1-of-1 objective, so it
            // is not drawn there.
            if (objectives[i].RequiredCount > 1)
            {
                ProgressBar track = UiTheme.Bar(done ? UiTheme.QuestComplete : UiTheme.Accent, 0f);
                track.CustomMinimumSize = new Vector2(0f, 4f);
                track.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                track.Value = Mathf.Clamp(have / (double)required, 0d, 1d);
                block.AddChild(track);
            }

            // The hint for the CURRENT objective sits under it, hidden until the player has stayed on this
            // step long enough to look stuck (TrackerRules.HintDelaySeconds). UpdateQuest toggles it.
            if (i == _questCurrentObjective && objectives[i].HintKey.Length > 0 && Loc.Has(objectives[i].HintKey))
            {
                Label hint = UiTheme.Flavour(Loc.T(objectives[i].HintKey), UiTheme.Dim);
                UiTheme.ApplyType(hint, UiTheme.FontRole.SerifItalic, UiTheme.CaptionFontSize);
                hint.CustomMinimumSize = new Vector2(186f, 0f);
                hint.Visible = false;
                block.AddChild(hint);
                _questHint = hint;
            }
        }
    }
}
