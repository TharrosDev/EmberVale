using System;
using System.Collections.Generic;
using System.Linq;
using Embervale.Core;
using Embervale.Corruption;
using Embervale.Items;
using Embervale.Localization;
using Embervale.Progression;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The Perks tab body: one branch at a time as a tier-by-column grid with prerequisite connectors, a branch
/// strip across the top, a detail pane for the focused perk and the Respec control. Every button press only
/// calls into <see cref="PerksComponent"/> or flips <see cref="ViewState"/> and asks the host to rebuild, so the
/// panel owns no perk state and closing it loses nothing. The host rebuilds it on every perk or skill-point
/// change (<see cref="InventoryPanel"/> already does) and focus returns to the same node by child index.
/// Pressing a node learns its next rank; focusing or hovering one fills the detail pane.
/// </summary>
public sealed partial class PerkTreePanel : VBoxContainer
{
    /// <summary>What must survive the host's rebuild: the open branch, the perk the pane shows and a pending respec.</summary>
    public sealed class ViewState
    {
        public PerkBranch? Branch { get; set; }

        public string? FocusedId { get; set; }

        public bool ConfirmingRespec { get; set; }

        /// <summary>The perk a rank was just bought in. The next build draws its heading rule as an ember wipe
        /// and clears this, so the flourish plays once per purchase and never on a plain rebuild.</summary>
        public string? LearnedId { get; set; }

        /// <summary>The perk whose last press was refused. Its reason is shown as a warning until focus moves on.</summary>
        public string? DeniedId { get; set; }

        /// <summary>How many of the four node visuals the last build drew (read by the screenshot harness).</summary>
        public int VisualsShown { get; set; }
    }

    private readonly PerksComponent _perks;
    private readonly ProgressionComponent? _progression;
    private readonly InventoryComponent? _wallet;
    private readonly ViewState _view;
    private readonly Action _changed;
    private readonly float _usableWidth;

    private VBoxContainer _detail = null!;
    private PerkTreeCanvas? _canvas;
    private HashSet<string> _branchIds = new();
    private readonly HashSet<PerkNodeVisual> _visuals = new();

    /// <summary>Set while building the grid: nodes are too narrow for body-size status text beside five pips.</summary>
    private bool _compact;

    /// <summary>Each node's perk, grid cell and focusable button, in the order they were added; linked in <see cref="_Ready"/>.</summary>
    private readonly List<(PerkResource Perk, (int Tier, int Column) Cell, Button Input)> _focusNodes = new();

    public PerkTreePanel(
        PerksComponent perks,
        ProgressionComponent? progression,
        InventoryComponent? wallet,
        ViewState view,
        Action changed,
        float usableWidth)
    {
        _perks = perks;
        _progression = progression;
        _wallet = wallet;
        _view = view;
        _changed = changed;
        _usableWidth = usableWidth;

        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        AddThemeConstantOverride("separation", UiTheme.SpaceSm);
        Build();
    }

    private void Build()
    {
        List<PerkBranch> branches = PerkTreeRules.BranchesWithPerks(PerkDatabase.All.Select(p => p.Branch));
        if (PerkTreeRules.PickBranch(branches, _view.Branch, b => _perks.BranchPoints(b)) is not { } branch)
        {
            return;
        }

        if (_view.Branch != branch)
        {
            _view.Branch = branch;
            _view.FocusedId = null;
        }

        List<PerkResource> inBranch = PerkDatabase.All
            .Where(p => p.Branch == branch)
            .OrderBy(p => p.Tier).ThenBy(p => p.Column).ThenBy(p => p.Id, StringComparer.Ordinal)
            .ToList();

        _branchIds = new HashSet<string>(inBranch.Select(p => p.Id));

        AddChild(BuildHeader());
        AddChild(BuildHeadingRule());
        AddChild(BuildBranchStrip(branches));

        var body = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", UiTheme.SpaceLg);
        body.AddChild(BuildCanvas(inBranch));
        body.AddChild(BuildDetailColumn(inBranch));
        AddChild(body);
        _view.VisualsShown = _visuals.Count;
    }

    /// <summary>The one rule under the heading. After a purchase it is drawn by a line of heat
    /// (<see cref="UiOrnament.EmberWipe"/>); otherwise it is the same rule already cool, so the tree does not
    /// shift by a pixel when a rank is bought.</summary>
    private Control BuildHeadingRule()
    {
        const float Thickness = 2f;
        bool bought = _view.LearnedId != null;
        _view.LearnedId = null;
        if (bought)
        {
            return UiOrnament.EmberWipe(Thickness);
        }

        return new ColorRect
        {
            Color = UiTheme.RuleLit,
            CustomMinimumSize = new Vector2(0f, Thickness),
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
    }

    /// <summary>Wires arrow / d-pad focus between nodes. Only now are the buttons in the tree: a NodePath does not exist before.
    /// Up and down follow the prerequisite lines where a perk has one (<see cref="PerkTreeRules.FocusTargetAlongEdges"/>).
    /// A step with no node that way points at the button itself, so focus never leaves the grid sideways or down; "up" off
    /// the top row is left to the default search, which finds the branch strip.</summary>
    public override void _Ready()
    {
        List<(int Tier, int Column)> cells = _focusNodes.Select(n => n.Cell).ToList();
        var index = new Dictionary<string, int>();
        for (int i = 0; i < _focusNodes.Count; i++)
        {
            index[_focusNodes[i].Perk.Id] = i;
        }

        var edges = new List<(int From, int To)>();
        for (int i = 0; i < _focusNodes.Count; i++)
        {
            foreach (string prerequisite in _focusNodes[i].Perk.PrerequisiteIds)
            {
                if (index.TryGetValue(prerequisite, out int from))
                {
                    edges.Add((from, i));
                }
            }
        }

        for (int i = 0; i < _focusNodes.Count; i++)
        {
            Button input = _focusNodes[i].Input;
            Link(input, Side.Left, i, PerkTreeDirection.Left, cells, edges, stayPut: true);
            Link(input, Side.Right, i, PerkTreeDirection.Right, cells, edges, stayPut: true);
            Link(input, Side.Top, i, PerkTreeDirection.Up, cells, edges, stayPut: false);
            Link(input, Side.Bottom, i, PerkTreeDirection.Down, cells, edges, stayPut: true);
        }
    }

    private void Link(
        Button input, Side side, int from, PerkTreeDirection direction,
        List<(int Tier, int Column)> cells, List<(int From, int To)> edges, bool stayPut)
    {
        int target = PerkTreeRules.FocusTargetAlongEdges(cells, edges, from, direction);
        if (target >= 0)
        {
            input.SetFocusNeighbor(side, _focusNodes[target].Input.GetPath());
        }
        else if (stayPut)
        {
            input.SetFocusNeighbor(side, input.GetPath());
        }
    }

    private float DetailWidth => Mathf.Clamp(_usableWidth * 0.28f, 200f, 320f);

    private Control BuildHeader()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", UiTheme.SpaceSm);

        // A bare heading: the rule under the row (BuildHeadingRule) is this section's one rule.
        Label title = UiTheme.Header(Loc.T("char.perks"));
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        title.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        row.AddChild(title);

        int points = _progression?.SkillPoints ?? 0;
        row.AddChild(Centred(UiTheme.Chip(Loc.TF("char.skill_points", points), points > 0 ? UiTheme.Accent : UiTheme.Dim)));
        row.AddChild(BuildRespecControls());
        return row;
    }

    /// <summary>The Respec button, or its Cancel / hold-to-confirm pair. Cancel comes first so the focus restore after the
    /// rebuild (child index 0 of this slot) lands on the safe choice. The respec itself is only ever committed by
    /// the hold ring: it cannot be undone, and its gold is spent.</summary>
    private Control BuildRespecControls()
    {
        var slot = new HBoxContainer();
        slot.AddThemeConstantOverride("separation", UiTheme.SpaceXs);
        int cost = _perks.RespecCost;

        if (_view.ConfirmingRespec)
        {
            Button cancel = UiTheme.Action(Loc.T("perktree.respec_cancel"));
            cancel.Pressed += () =>
            {
                _view.ConfirmingRespec = false;
                _changed();
            };
            slot.AddChild(cancel);

            // With holds turned into presses (the accessibility setting) the ring completes on the press, so the
            // button must not ask for a hold it will not wait for.
            Button confirm = UiTheme.Action(UiFx.HoldsToPresses
                ? Loc.TF("perktree.respec_cost", cost)
                : Loc.TF("perktree.respec_hold", cost));
            UiTheme.ApplyType(confirm, UiTheme.FontRole.Interface, UiTheme.BodyFontSize); // a sentence, not a carved label
            confirm.Disabled = _wallet == null || !_perks.CanRespec(_wallet);
            HoldRing ring = UiFx.HoldRing(() =>
            {
                _view.ConfirmingRespec = false;
                if (_wallet != null)
                {
                    _perks.Respec(_wallet);
                }

                _changed();
            });
            ring.Attach(confirm);
            slot.AddChild(confirm);
            slot.AddChild(ring);
            return slot;
        }

        bool spent = _perks.PointsSpent > 0;
        Button respec = UiTheme.Action(spent ? Loc.TF("perktree.respec_cost", cost) : Loc.T("perktree.respec"));
        respec.Disabled = !spent || _wallet == null;
        respec.TooltipText = Loc.T(spent ? "perktree.respec_tip" : "perktree.respec_nothing");
        respec.Pressed += () =>
        {
            _view.ConfirmingRespec = true;
            _changed();
        };
        slot.AddChild(respec);
        return slot;
    }

    private Control BuildBranchStrip(List<PerkBranch> branches)
    {
        var tabs = new UiTabs();
        bool counts = _usableWidth >= 1000f;
        foreach (PerkBranch branch in branches)
        {
            string name = Loc.T(PerkTreeRules.BranchKey(branch));
            int points = _perks.BranchPoints(branch);
            tabs.Add(counts && points > 0 ? Loc.TF("perktree.branch_tab", name, points) : name);
        }

        // Select before subscribing: the first tab is already current, and this must not re-enter a rebuild.
        tabs.Select(branches.IndexOf(_view.Branch!.Value));
        tabs.TabChanged += index =>
        {
            _view.Branch = branches[index];
            _view.FocusedId = null;
            _changed();
        };
        return tabs;
    }

    private Control BuildCanvas(List<PerkResource> inBranch)
    {
        int columns = PerkTreeRules.ColumnCount(inBranch.Max(p => p.Column));
        int rows = PerkTreeRules.RowCount(inBranch.Max(p => p.Tier));

        // Estimate the node width from the room left beside the detail pane; below the threshold the gutter, pips and
        // status text all shrink a step (a Steam Deck at UI scale 1.5 lays out at 853 px).
        float room = _usableWidth - DetailWidth - UiTheme.SpaceLg - PerkTreeCanvas.GutterWidth;
        _compact = (room / columns) - PerkTreeCanvas.ColumnGap < PerkTreeRules.CompactNodeWidth;
        var canvas = new PerkTreeCanvas(columns, rows, _compact ? PerkTreeCanvas.CompactGutterWidth : PerkTreeCanvas.GutterWidth)
        {
            SizeFlagsVertical = SizeFlags.ShrinkBegin,
        };
        _canvas = canvas;

        for (int tier = 1; tier <= rows; tier++)
        {
            canvas.AddGate(BuildGateLabel(tier, inBranch), tier);
        }

        var ids = new HashSet<string>(inBranch.Select(p => p.Id));
        foreach (PerkResource perk in inBranch)
        {
            canvas.AddNode(perk.Id, BuildNode(perk), perk.Tier, perk.Column);
            foreach (string prerequisite in perk.PrerequisiteIds)
            {
                if (ids.Contains(prerequisite))
                {
                    canvas.AddEdge(prerequisite, perk.Id, _perks.RankOf(prerequisite) > 0);
                }
            }
        }

        return canvas;
    }

    private Control BuildGateLabel(int tier, List<PerkResource> inBranch)
    {
        int required = inBranch.Where(p => p.Tier == tier).Select(p => p.BranchPointsRequired).DefaultIfEmpty(0).Max();
        bool met = _perks.BranchPoints(_view.Branch!.Value) >= required;

        var label = new VBoxContainer();
        label.AddThemeConstantOverride("separation", 0);
        label.AddChild(UiTheme.Caption(Loc.TF("perktree.tier", tier)));
        label.AddChild(UiTheme.Caption(
            required > 0 ? Loc.TF("perktree.tier_gate", required) : Loc.T("perktree.tier_open"),
            met ? UiTheme.Dim : UiTheme.Disabled));
        return label;
    }

    private Control BuildNode(PerkResource perk)
    {
        int rank = _perks.RankOf(perk.Id);
        PerkNodeState state = PerkTreeRules.StateOf(_perks.WhyNot(perk));
        PerkNodeVisual visual = PerkTreeRules.VisualOf(state, rank);
        _visuals.Add(visual);
        (string caption, Color captionColor) = NodeCaption(perk, state);

        // A grid node is smaller than a list row, so it brings its own tighter frame; CardButton sizes its hover and
        // focus rings from that frame's margins.
        StyleBoxFlat box = NodeFrame(visual, state);
        box.SetContentMarginAll(UiTheme.SpaceXs);
        box.ContentMarginLeft = UiTheme.SpaceSm;
        PanelContainer card = UiTheme.CardButton(null, out Button input, out VBoxContainer content, box);

        content.AddThemeConstantOverride("separation", 1);

        Color nameColor = state == PerkNodeState.CorruptionGated
            ? UiTheme.CorruptionText
            : rank > 0 || state == PerkNodeState.Learnable ? UiTheme.Text : UiTheme.Dim;

        // The mark is the node's state as a shape, beside the name that says which perk it is.
        var head = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        head.AddThemeConstantOverride("separation", UiTheme.SpaceXs);
        head.AddChild(Centred(new PerkNodeMark(visual, perk.IsCapstone, MarkColor(visual, state), _compact ? 12f : 14f)));
        Label name = UiTheme.Body(perk.LocalizedName, nameColor);
        name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        head.AddChild(name);
        content.AddChild(head);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", UiTheme.SpaceXs);
        row.AddChild(Centred(RankPips(rank, perk.MaxRank, _compact)));
        Label status = _compact ? UiTheme.Caption(caption, captionColor) : UiTheme.Body(caption, captionColor);
        status.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        status.HorizontalAlignment = HorizontalAlignment.Right;
        status.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        row.AddChild(status);
        content.AddChild(row);

        _focusNodes.Add((perk, (perk.Tier, perk.Column), input));
        input.TooltipText = perk.LocalizedName;
        input.FocusEntered += () => ShowDetail(perk);
        input.MouseEntered += () => ShowDetail(perk);
        input.Pressed += () =>
        {
            // Learn publishes the change and the host rebuilds on its next frame. A refusal changes nothing, so
            // it says why in the pane and sounds like a refusal instead of doing nothing at all.
            if (_perks.CanLearn(perk) && _perks.Learn(perk))
            {
                _view.DeniedId = null;
                _view.LearnedId = perk.Id;
                UiAudio.Play(UiCue.Confirm);
            }
            else
            {
                _view.DeniedId = perk.Id;
                UiAudio.Play(UiCue.Denied);
                ShowDetail(perk);
            }
        };
        return card;
    }

    /// <summary>The frame a node state takes. Locked is cut into the panel; available is a plate with one lit
    /// top edge and no spine; owned carries a brass spine; maxed an ember spine and a warmed face.</summary>
    private static StyleBoxFlat NodeFrame(PerkNodeVisual visual, PerkNodeState state)
    {
        switch (visual)
        {
            case PerkNodeVisual.Maxed:
            {
                StyleBoxFlat box = UiTheme.CardStyle(UiTheme.Accent);
                box.BgColor = UiTheme.CardBg.Lerp(UiTheme.Accent, 0.08f);
                box.BorderWidthBottom = 2;
                return box;
            }

            case PerkNodeVisual.Owned:
                return UiTheme.CardStyle(UiTheme.Brass);

            case PerkNodeVisual.Available:
            {
                StyleBoxFlat box = UiTheme.CardStyle();
                box.BorderColor = state == PerkNodeState.Learnable ? UiTheme.RuleLit : UiTheme.Rule;
                box.BorderWidthTop = 2;
                box.BorderWidthBottom = 0;
                return box;
            }

            default:
            {
                StyleBoxFlat box = UiTheme.WellStyle();
                box.BorderColor = state == PerkNodeState.CorruptionGated ? UiTheme.Corruption : UiTheme.Rule;
                box.BorderWidthTop = 1;
                return box;
            }
        }
    }

    private static Color MarkColor(PerkNodeVisual visual, PerkNodeState state) => visual switch
    {
        PerkNodeVisual.Maxed => UiTheme.Accent,
        PerkNodeVisual.Owned => UiTheme.BrassLit,
        PerkNodeVisual.Available => state == PerkNodeState.Learnable ? UiTheme.Good : UiTheme.Dim,
        _ => state == PerkNodeState.CorruptionGated ? UiTheme.CorruptionText : UiTheme.Disabled,
    };

    private (string Text, Color Color) NodeCaption(PerkResource perk, PerkNodeState state) => state switch
    {
        PerkNodeState.Maxed => (Loc.T("perktree.node_maxed"), UiTheme.Accent),
        PerkNodeState.Learnable => (Loc.TF("char.perk_learn", perk.Cost), UiTheme.Good),
        PerkNodeState.NeedsPoints => (Loc.TF("perktree.node_points", perk.Cost), UiTheme.Dim),
        PerkNodeState.CorruptionGated => (
            Loc.TF("perktree.node_corrupt", CorruptionTiers.DisplayName(perk.MinCorruptionTier)), UiTheme.CorruptionText),
        _ => (Loc.T("perktree.node_locked"), UiTheme.Disabled),
    };

    private Control BuildDetailColumn(List<PerkResource> inBranch)
    {
        var column = new VBoxContainer
        {
            CustomMinimumSize = new Vector2(DetailWidth, 0f),
            SizeFlagsVertical = SizeFlags.ShrinkBegin,
        };
        column.AddThemeConstantOverride("separation", UiTheme.SpaceSm);

        if (_view.ConfirmingRespec)
        {
            column.AddChild(BuildRespecNote());
        }

        PanelContainer card = UiTheme.Card();
        _detail = new VBoxContainer();
        _detail.AddThemeConstantOverride("separation", UiTheme.SpaceXs);
        card.AddChild(_detail);
        column.AddChild(card);

        PerkResource? shown = _view.FocusedId is { } id ? inBranch.FirstOrDefault(p => p.Id == id) : null;
        shown ??= PerkTreeRules.FirstToShow(inBranch, _perks.CanLearn);
        if (shown != null)
        {
            ShowDetail(shown);
        }

        return column;
    }

    private Control BuildRespecNote()
    {
        int gold = _wallet?.CountOf(GameIds.Currency.Gold) ?? 0;
        int cost = _perks.RespecCost;

        PanelContainer note = UiTheme.Card(UiTheme.AccentHot);
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", UiTheme.SpaceXs);
        col.AddChild(UiTheme.Prose(Loc.TF("perktree.respec_confirm", _perks.PointsSpent, cost, gold)));
        if (gold < cost)
        {
            col.AddChild(UiTheme.Prose(Loc.TF("perktree.respec_poor", cost, gold), UiTheme.Bad));
        }

        note.AddChild(col);
        return note;
    }

    /// <summary>Fills the detail pane in place (it holds nothing focusable, so no rebuild is needed).</summary>
    private void ShowDetail(PerkResource perk)
    {
        _view.FocusedId = perk.Id;
        if (_view.DeniedId != perk.Id)
        {
            _view.DeniedId = null;
        }

        _canvas?.SetPath(PerkTreeRules.PathTo(
            perk.Id,
            id => PerkDatabase.Get(id)?.PrerequisiteIds.Where(_branchIds.Contains) ?? Enumerable.Empty<string>(),
            id => _perks.RankOf(id) > 0));
        UiTheme.ClearChildren(_detail);

        int rank = _perks.RankOf(perk.Id);
        PerkBlock block = _perks.WhyNot(perk);

        Label title = UiTheme.Header(perk.LocalizedName);
        title.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _detail.AddChild(title);

        var chips = new HFlowContainer();
        chips.AddThemeConstantOverride("h_separation", UiTheme.SpaceXs);
        chips.AddThemeConstantOverride("v_separation", UiTheme.SpaceXs);
        chips.AddChild(UiTheme.Chip(Loc.T(PerkTreeRules.BranchKey(perk.Branch)), UiTheme.Dim));
        chips.AddChild(UiTheme.Chip(Loc.TF("perktree.tier", perk.Tier), UiTheme.Dim));
        if (perk.IsCapstone)
        {
            chips.AddChild(UiTheme.Chip(Loc.T("perktree.capstone"), UiTheme.Accent));
        }

        if (perk.MinCorruptionTier > CorruptionTier.Untainted)
        {
            chips.AddChild(UiTheme.Chip(
                Loc.TF("perktree.node_corrupt", CorruptionTiers.DisplayName(perk.MinCorruptionTier)), UiTheme.CorruptionText));
        }

        _detail.AddChild(chips);

        var rankRow = new HBoxContainer();
        rankRow.AddThemeConstantOverride("separation", UiTheme.SpaceSm);
        rankRow.AddChild(Centred(RankPips(rank, perk.MaxRank, false)));
        rankRow.AddChild(UiTheme.Body(Loc.TF("perktree.detail_rank", rank, perk.MaxRank)));
        _detail.AddChild(rankRow);

        if (!string.IsNullOrWhiteSpace(perk.LocalizedDescription))
        {
            _detail.AddChild(UiTheme.Prose(perk.LocalizedDescription));
        }

        _detail.AddChild(UiTheme.Caption(Loc.TF("perktree.detail_cost", perk.Cost)));
        if (perk.PrerequisiteIds.Count > 0)
        {
            _detail.AddChild(UiTheme.Caption(Loc.TF("perktree.detail_requires", PrerequisiteNames(perk))));
        }

        (string reason, Color reasonColor) = ReasonLine(perk, block);
        if (_view.DeniedId != perk.Id)
        {
            _detail.AddChild(UiTheme.Prose(reason, reasonColor));
            return;
        }

        // A refused press: the same sentence, now with the warning mark beside it so the refusal is a shape
        // as well as a colour.
        var refused = new HBoxContainer();
        refused.AddThemeConstantOverride("separation", UiTheme.SpaceXs);
        TextureRect warning = UiIcon.Create(UiIcon.Kind.Warning, 16f, UiTheme.Bad);
        warning.SizeFlagsVertical = SizeFlags.ShrinkBegin;
        refused.AddChild(warning);
        Label why = UiTheme.Prose(reason, UiTheme.Bad);
        why.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        refused.AddChild(why);
        _detail.AddChild(refused);
    }

    private string PrerequisiteNames(PerkResource perk) => string.Join(
        ", ",
        perk.PrerequisiteIds.Select(id => PerkDatabase.Get(id)?.LocalizedName ?? id));

    /// <summary>The full sentence behind a node's one-word caption: the same <see cref="PerkBlock"/>, spelled out.</summary>
    private (string Text, Color Color) ReasonLine(PerkResource perk, PerkBlock block)
    {
        switch (block)
        {
            case PerkBlock.None:
                return (Loc.TF("perktree.why_learn", perk.Cost), UiTheme.Good);
            case PerkBlock.Maxed:
                return (Loc.T("perktree.why_maxed"), UiTheme.Accent);
            case PerkBlock.Corruption:
                return (Loc.TF("perktree.why_corruption", CorruptionTiers.DisplayName(perk.MinCorruptionTier)), UiTheme.CorruptionText);
            case PerkBlock.Prerequisite:
            {
                IEnumerable<string> missing = perk.PrerequisiteIds
                    .Where(id => _perks.RankOf(id) <= 0)
                    .Select(id => PerkDatabase.Get(id)?.LocalizedName ?? id);
                return (Loc.TF("perktree.why_prerequisite", string.Join(", ", missing)), UiTheme.Dim);
            }

            case PerkBlock.BranchPoints:
                return (Loc.TF(
                    "perktree.why_branch",
                    perk.BranchPointsRequired,
                    Loc.T(PerkTreeRules.BranchKey(perk.Branch)),
                    _perks.BranchPoints(perk.Branch)), UiTheme.Dim);
            default:
                return (Loc.TF("perktree.why_points", perk.Cost, _progression?.SkillPoints ?? 0), UiTheme.Dim);
        }
    }

    /// <summary>A perk's rank as filled pips. Paired with the "2 of 5" text rather than replacing it: pips are
    /// read at a glance, the numbers exactly.</summary>
    private static Control RankPips(int rank, int maxRank, bool compact)
    {
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", compact ? 1 : 2);

        for (int i = 0; i < Mathf.Max(1, maxRank); i++)
        {
            row.AddChild(new ColorRect
            {
                Color = i < rank ? UiTheme.Accent : UiTheme.Iron,
                CustomMinimumSize = new Vector2(compact ? 8f : 11f, 8f),
                MouseFilter = MouseFilterEnum.Ignore,
            });
        }

        return row;
    }

    private static Control Centred(Control control)
    {
        control.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        return control;
    }
}
