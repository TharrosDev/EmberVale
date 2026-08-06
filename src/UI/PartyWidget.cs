using System.Collections.Generic;
using Embervale.Companions;
using Embervale.Core;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Localization;
using Embervale.Stats;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The party strip (Phase 32B): one compact row per recruited companion — name, health, and the
/// order it is under — docked above the player's vitals. It is the feedback surface the quick
/// command needs: an order the player can't see issued is an order they can't trust, so the row's
/// order label is what turns <c>C</c> from a keypress into a command.
///
/// It hides itself entirely while the party is empty, so a solo player's HUD is unchanged. Rows are
/// rebuilt from a dirty flag on roster events (never inside a signal); the bars tick every frame.
/// </summary>
public partial class PartyWidget : VBoxContainer
{
    private sealed class Row
    {
        public required string CompanionId { get; init; }

        public required Label Name { get; init; }

        public required ProgressBar Health { get; init; }

        public required Label Order { get; init; }

        public required Label Loyalty { get; init; }
    }

    private readonly List<Row> _rows = new();
    private VBoxContainer _list = null!;
    private CompanionRoster? _roster;
    private bool _dirty = true;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;

        // The shared frame, so the strip reads as part of the same HUD as the vitals panel below it.
        // A Card, not a Panel (37.5H) - a small self-hiding HUD strip does not earn a framed
        // screen's chrome, and it sits directly above the vitals panel where two brass rules
        // stacked read as a seam rather than as two widgets.
        PanelContainer frame = UiTheme.Card(UiTheme.Friendly);
        frame.MouseFilter = MouseFilterEnum.Ignore;
        frame.CustomMinimumSize = new Vector2(250, 0);
        AddChild(frame);

        MarginContainer padding = UiTheme.Padding(UiTheme.SpaceSm);
        frame.AddChild(padding);

        _list = new VBoxContainer();
        _list.AddThemeConstantOverride("separation", UiTheme.SpaceXs);
        padding.AddChild(_list);

        EventBus bus = EventBus.Instance;
        bus?.Subscribe<CompanionRecruitedEvent>(OnPartyChanged);
        bus?.Subscribe<CompanionDismissedEvent>(OnPartyChanged);
        bus?.Subscribe<CompanionStanceChangedEvent>(OnStanceChanged);
        bus?.Subscribe<CompanionLoyaltyTierChangedEvent>(OnLoyaltyTierChanged);
        bus?.Subscribe<GameLoadedEvent>(OnGameLoaded);
    }

    public override void _ExitTree()
    {
        EventBus? bus = EventBus.Instance;
        if (bus == null)
        {
            return;
        }

        bus.Unsubscribe<CompanionRecruitedEvent>(OnPartyChanged);
        bus.Unsubscribe<CompanionDismissedEvent>(OnPartyChanged);
        bus.Unsubscribe<CompanionStanceChangedEvent>(OnStanceChanged);
        bus.Unsubscribe<CompanionLoyaltyTierChangedEvent>(OnLoyaltyTierChanged);
        bus.Unsubscribe<GameLoadedEvent>(OnGameLoaded);
    }

    public override void _Process(double delta)
    {
        CompanionRoster? roster = Roster();
        if (roster == null)
        {
            Visible = false;
            return;
        }

        if (_dirty)
        {
            _dirty = false;
            Rebuild(roster);
        }

        Visible = _rows.Count > 0;
        foreach (Row row in _rows)
        {
            if (!roster.TryGet(row.CompanionId, out CompanionEntity companion))
            {
                continue;
            }

            StatsComponent? stats = companion.GetComponent<StatsComponent>();
            row.Health.Value = stats?.GetNormalized(StatType.Health) ?? 0d;

            // A downed companion reads as downed rather than as whatever order it was under — that
            // is the state the player has to act on.
            bool downed = companion.GetComponent<CompanionAIComponent>()?.State == CompanionState.Downed;
            row.Order.Text = Loc.T(downed
                ? "companion.order.downed"
                : CompanionOrders.NameKey(roster.StanceOf(row.CompanionId)));
            row.Order.AddThemeColorOverride("font_color", downed ? UiTheme.Bad : UiTheme.Dim);
            row.Loyalty.Text = Loc.T(CompanionLoyalty.NameKey(roster.TierOf(row.CompanionId)));
            row.Name.AddThemeColorOverride("font_color", downed ? UiTheme.Bad : UiTheme.Text);
        }
    }

    private void Rebuild(CompanionRoster roster)
    {
        foreach (Node child in _list.GetChildren())
        {
            child.QueueFree();
        }

        _rows.Clear();

        foreach (string id in roster.RecruitedIds)
        {
            if (!roster.TryGet(id, out CompanionEntity companion))
            {
                continue;
            }

            var line = new HBoxContainer();
            line.AddThemeConstantOverride("separation", UiTheme.SpaceSm);

            Label name = UiTheme.Body(Loc.T(companion.NameKey));
            name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            line.AddChild(name);

            Label loyalty = UiTheme.Caption(string.Empty, UiTheme.Accent);
            loyalty.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            line.AddChild(loyalty);

            Label order = UiTheme.Caption(string.Empty, UiTheme.Dim);
            order.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            line.AddChild(order);
            _list.AddChild(line);

            ProgressBar health = UiTheme.Bar(UiTheme.Health);
            health.CustomMinimumSize = new Vector2(168f, 6f);
            _list.AddChild(health);

            _rows.Add(new Row { CompanionId = id, Name = name, Health = health, Order = order, Loyalty = loyalty });
        }

        // The command hint only earns its space once there is someone to command.
        if (_rows.Count > 0)
        {
            _list.AddChild(UiTheme.Caption(
                Loc.TF("hud.party_hint", GameInput.PromptLabel(GameInput.CompanionCommand)), UiTheme.Dim));
        }
    }

    private CompanionRoster? Roster()
    {
        if (_roster != null && IsInstanceValid(_roster))
        {
            return _roster;
        }

        _roster = null;
        if (ServiceLocator.Instance != null && ServiceLocator.Instance.TryGet(out CompanionRoster found))
        {
            _roster = found;
        }

        return _roster;
    }

    private void OnPartyChanged(CompanionRecruitedEvent e) => _dirty = true;

    private void OnPartyChanged(CompanionDismissedEvent e) => _dirty = true;

    private void OnStanceChanged(CompanionStanceChangedEvent e) => _dirty = true;

    private void OnLoyaltyTierChanged(CompanionLoyaltyTierChangedEvent e) => _dirty = true;

    private void OnGameLoaded(GameLoadedEvent e) => _dirty = true;
}
