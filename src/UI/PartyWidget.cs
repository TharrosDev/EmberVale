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
///
/// Like the vitals under it, it has no plate: inked text and keylined bars on the world, as wide as
/// the vitals (<see cref="HudMetrics.VitalsWidth"/>) so the two read as one column.
/// </summary>
public partial class PartyWidget : VBoxContainer
{
    private sealed class Row
    {
        public required string CompanionId { get; init; }

        public required Label Name { get; init; }

        public required JuicedBar Health { get; init; }

        public required Label Order { get; init; }

        public required Label Loyalty { get; init; }

        // What the row last showed. The order and loyalty labels were re-translated and their
        // colours restated every frame; they move a handful of times in a session.
        public int OrderShown { get; set; } = -1;

        public int LoyaltyShown { get; set; } = -1;

        public int DownedShown { get; set; } = -1;
    }

    private readonly List<Row> _rows = new();
    private PanelContainer _frame = null!;
    private VBoxContainer _list = null!;
    private GameHud? _hud;
    private CompanionRoster? _roster;
    private bool _dirty = true;
    private bool _allowed = true;

    /// <summary>Whether the player's HUD options let the strip show (<c>GameHud</c> sets it). The
    /// strip still hides itself while the party is empty.</summary>
    public bool Allowed
    {
        get => _allowed;
        set
        {
            _allowed = value;
            Visible = value && _rows.Count > 0;
        }
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;

        // No ground, like the vitals it sits on: it was a Card, and before that a Panel, and a box
        // directly above another box read as a seam rather than as two widgets.
        _frame = UiTheme.HudBare();
        _frame.CustomMinimumSize = new Vector2(HudMetrics.VitalsMin, 0);
        AddChild(_frame);

        _list = new VBoxContainer();
        _list.AddThemeConstantOverride("separation", UiTheme.SpaceXs);
        _frame.AddChild(_list);

        EventBus bus = EventBus.Instance;
        bus?.Subscribe<CompanionRecruitedEvent>(OnPartyChanged);
        bus?.Subscribe<CompanionDismissedEvent>(OnPartyChanged);
        bus?.Subscribe<CompanionStanceChangedEvent>(OnStanceChanged);
        bus?.Subscribe<CompanionLoyaltyTierChangedEvent>(OnLoyaltyTierChanged);
        bus?.Subscribe<GameLoadedEvent>(OnGameLoaded);
        bus?.Subscribe<InputDeviceChangedEvent>(OnDeviceChanged);
        bus?.Subscribe<InputBindingsChangedEvent>(OnBindingsChanged);
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
        bus.Unsubscribe<InputDeviceChangedEvent>(OnDeviceChanged);
        bus.Unsubscribe<InputBindingsChangedEvent>(OnBindingsChanged);
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

        // As wide as the vitals at this layout width, whether or not the vitals are showing.
        _hud ??= GameHud.Of(this);
        if (_hud is { LayoutWidth: > 0f } hud)
        {
            float width = HudMetrics.VitalsWidth(hud.LayoutWidth);
            if (_frame.CustomMinimumSize.X != width)
            {
                _frame.CustomMinimumSize = new Vector2(width, 0f);
            }
        }

        Visible = _allowed && _rows.Count > 0;
        foreach (Row row in _rows)
        {
            if (!roster.TryGet(row.CompanionId, out CompanionEntity companion))
            {
                continue;
            }

            StatsComponent? stats = companion.GetComponent<StatsComponent>();
            row.Health.SetTarget(stats?.GetNormalized(StatType.Health) ?? 0d);

            // A downed companion reads as downed rather than as whatever order it was under — that
            // is the state the player has to act on.
            bool downed = companion.GetComponent<CompanionAIComponent>()?.State == CompanionState.Downed;
            int order = downed ? -2 : (int)roster.StanceOf(row.CompanionId);
            if (order != row.OrderShown)
            {
                row.OrderShown = order;
                row.Order.Text = Loc.T(downed
                    ? "companion.order.downed"
                    : CompanionOrders.NameKey(roster.StanceOf(row.CompanionId)));
            }

            int downedKey = downed ? 1 : 0;
            if (downedKey != row.DownedShown)
            {
                // Going down or getting up is the news a Dynamic strip exists for; the first
                // reading of a freshly built row is not.
                if (row.DownedShown >= 0)
                {
                    _hud?.MarkChanged(HudElement.Party);
                }

                row.DownedShown = downedKey;
                UiLive.FontColor(row.Order, downed ? UiTheme.Bad : UiTheme.Dim);
                UiLive.FontColor(row.Name, downed ? UiTheme.Bad : UiTheme.Text);
            }

            int loyalty = (int)roster.TierOf(row.CompanionId);
            if (loyalty != row.LoyaltyShown)
            {
                row.LoyaltyShown = loyalty;
                row.Loyalty.Text = Loc.T(CompanionLoyalty.NameKey(roster.TierOf(row.CompanionId)));
            }
        }
    }

    private void Rebuild(CompanionRoster roster)
    {
        foreach (Node child in _list.GetChildren())
        {
            _list.RemoveChild(child);
            child.QueueFree();
        }

        _rows.Clear();

        foreach (string id in roster.RecruitedIds)
        {
            if (!roster.TryGet(id, out CompanionEntity companion))
            {
                continue;
            }

            // One block per companion: the name line hugs its bar, and companions stand apart.
            var block = new VBoxContainer();
            block.AddThemeConstantOverride("separation", UiTheme.Space2xs);
            _list.AddChild(block);

            var line = new HBoxContainer();
            line.AddThemeConstantOverride("separation", UiTheme.SpaceSm);

            Label name = UiTheme.HudInk(UiTheme.Body(Loc.T(companion.NameKey)));
            name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            line.AddChild(name);

            Label loyalty = UiTheme.HudInk(UiTheme.Caption(string.Empty, UiTheme.Accent));
            loyalty.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            line.AddChild(loyalty);

            Label order = UiTheme.HudInk(UiTheme.Caption(string.Empty, UiTheme.Dim));
            order.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            line.AddChild(order);
            block.AddChild(line);

            // The same keylined bar as the player's stamina and mana, snapped to where the
            // companion's health is now so a rebuilt row does not replay a hit.
            JuicedBar health = JuicedBar.Create(UiTheme.Health, 0f);
            health.Keylined = true;
            health.LagChunk = true;
            health.CustomMinimumSize = new Vector2(0f, HudCoreMetrics.BarMinorHeight);
            health.Snap(companion.GetComponent<StatsComponent>()?.GetNormalized(StatType.Health) ?? 0d);
            block.AddChild(health);

            _rows.Add(new Row { CompanionId = id, Name = name, Health = health, Order = order, Loyalty = loyalty });
        }

        // The command hint only earns its space once there is someone to command.
        //
        // ⚠️ It used to render as the bare string "C · order", which reads as a fragment rather than
        // an instruction — and it was the only input hint on the HUD NOT drawn as a keycap, so the
        // one consistent visual language the interface had ("a key looks like a key") had a hole in
        // it. Now it matches the interaction prompt and the spell row (§56).
        if (_rows.Count > 0)
        {
            var hint = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            hint.AddThemeConstantOverride("separation", UiTheme.SpaceSm);

            Control cap = UiGlyph.For(GameInput.CompanionCommand);
            cap.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            hint.AddChild(cap);

            Label label = UiTheme.HudInk(UiTheme.Caption(Loc.T("hud.party_hint"), UiTheme.Dim));
            label.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            hint.AddChild(label);

            _list.AddChild(hint);
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

    /// <summary>Rebuilds the rows on the next tick (locale or colour-vision change): they hold
    /// translated text and adapted colours.</summary>
    public void MarkStale() => _dirty = true;

    private void OnPartyChanged(CompanionRecruitedEvent e) => NoteChanged();

    private void OnPartyChanged(CompanionDismissedEvent e) => NoteChanged();

    private void OnStanceChanged(CompanionStanceChangedEvent e) => NoteChanged();

    private void OnLoyaltyTierChanged(CompanionLoyaltyTierChangedEvent e) => NoteChanged();

    // A load restores a party; it does not announce one (CLAUDE.md §7), so it rebuilds without
    // bringing a Dynamic strip up. The same goes for a glyph following the device.
    private void OnGameLoaded(GameLoadedEvent e) => _dirty = true;

    private void OnDeviceChanged(InputDeviceChangedEvent e) => _dirty = true;

    private void OnBindingsChanged(InputBindingsChangedEvent e) => _dirty = true;

    /// <summary>The party itself changed: rebuild, and bring a Dynamic strip up to show it.</summary>
    private void NoteChanged()
    {
        _dirty = true;
        _hud ??= GameHud.Of(this);
        _hud?.MarkChanged(HudElement.Party);
    }
}
