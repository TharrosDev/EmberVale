using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Embervale.Appearance;
using Embervale.Backgrounds;
using Embervale.Factions;
using Embervale.Items;
using Embervale.Localization;
using Embervale.Magic;
using Embervale.Progression;
using Embervale.Races;
using Embervale.Stats;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The new-game character creator (Phase 26D): the player picks a race (with a live trait summary),
/// a background (a soft nudge, with its kit and perk spelled out) and a name before the world is built, producing the <see cref="CharacterProfile"/> the bootstrap
/// spawns from. Opened by <see cref="MainMenu"/> after the New-Game slot is chosen; mirrors
/// <see cref="SaveSlotPanel"/> (a <see cref="CanvasLayer"/> built through <see cref="UiTheme"/>, all
/// strings via <see cref="Loc"/>).
/// </summary>
public partial class CharacterCreator : CanvasLayer
{
    private Action<CharacterProfile>? _onConfirm;
    private Action? _onBack;

    private readonly List<RaceResource> _races = new();
    private RaceResource? _selected;
    private Label _summary = null!;
    private LineEdit _name = null!;
    private readonly List<BackgroundResource> _backgrounds = new();
    private BackgroundResource? _selectedBackground;
    private Label _backgroundSummary = null!;
    private PanelContainer _panel = null!;
    private GridContainer _raceGrid = null!;
    private GridContainer _backgroundGrid = null!;
    private ScrollContainer _scroll = null!;
    private VBoxContainer _appearanceBox = null!;
    private CharacterPreview _preview = null!;
    // The chosen option id per AppearanceSlot; the slot default until the player picks.
    private string[] _appearancePicks = Enumerable.Repeat(string.Empty, AppearanceRules.SlotCount).ToArray();

    public void Configure(Action<CharacterProfile> onConfirm, Action onBack)
    {
        _onConfirm = onConfirm;
        _onBack = onBack;
    }

    public override void _Ready()
    {
        Layer = 12; // above the main menu
        Godot.Input.MouseMode = Godot.Input.MouseModeEnum.Visible;
        Build();
        UiFocus.GrabFirst(_panel); // land on the race picker (30.5J)
    }

    public override void _Process(double delta)
    {
        // Esc / gamepad B backs out (30.5J) — unless a text field has focus, where Esc means
        // "stop typing", not "leave the creator".
        if (Godot.Input.IsActionJustPressed(UiLive.UiCancel) &&
            GetViewport().GuiGetFocusOwner() is not LineEdit)
        {
            _onBack?.Invoke();
            QueueFree();
        }
    }

    private void Build()
    {
        var backdrop = UiTheme.Scrim(0.92f);
        backdrop.MouseFilter = Control.MouseFilterEnum.Stop;
        AddChild(backdrop);

        PanelContainer panel = UiTheme.Panel();
        UiTheme.ApplyWorkspace(panel, 0.72f);
        AddChild(panel);
        _panel = panel;

        MarginContainer pad = UiTheme.Padding(18);
        panel.AddChild(pad);

        var outer = new VBoxContainer();
        outer.AddThemeConstantOverride("separation", UiTheme.SpaceSm);
        pad.AddChild(outer);

        outer.AddChild(UiTheme.Title(Loc.T("create.title")));
        outer.AddChild(UiTheme.Divider());

        // Scrolled and viewport-relative (37.5H): six race cards, the lore summary and two text
        // fields do not fit the 533 px logical viewport a Steam Deck reports at UI scale 1.5.
        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(0f, Mathf.Clamp(UiTheme.UsableHeight(panel) - 150f, 200f, 420f)),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            FollowFocus = true,
        };
        // The scroll column sits beside a live preview of the body, so every appearance pick is visible
        // however far down the list the player has scrolled.
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", UiTheme.SpaceMd);
        outer.AddChild(row);
        scroll.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(scroll);
        _scroll = scroll;

        PanelContainer previewWell = UiTheme.Well();
        _preview = new CharacterPreview();
        previewWell.AddChild(_preview);
        row.AddChild(previewWell);

        // Same scrollbar gutter as the settings screen: the bar is drawn inside the scroll's rect,
        // over whatever is beneath it.
        var gutter = new MarginContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        gutter.AddThemeConstantOverride("margin_right", UiTheme.SpaceLg);
        scroll.AddChild(gutter);

        var col = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        col.AddThemeConstantOverride("separation", UiTheme.SpaceSm);
        gutter.AddChild(col);
        col.AddChild(UiTheme.Divider());

        foreach (RaceResource race in RaceDatabase.All)
        {
            _races.Add(race);
        }

        // Race picking is a *card grid*, not a dropdown (37.5H).
        //
        // The dropdown made the six races look like a settings value: you had to open a list, pick
        // blind, then read a paragraph that reflowed underneath to find out what you had chosen —
        // and comparing two of them meant flipping back and forth from memory. This is the first
        // real decision the player makes and the only one they cannot revise later, so all six sit
        // on screen at once with their traits visible.
        col.AddChild(UiTheme.SectionRule(Loc.T("create.race")));

        _raceGrid = new GridContainer { Columns = 2 };
        _raceGrid.AddThemeConstantOverride("h_separation", UiTheme.SpaceXs);
        _raceGrid.AddThemeConstantOverride("v_separation", UiTheme.SpaceXs);
        col.AddChild(_raceGrid);

        _summary = UiTheme.Prose(string.Empty);
        _summary.CustomMinimumSize = new Vector2(0, 72);
        _summary.VerticalAlignment = VerticalAlignment.Top;
        col.AddChild(_summary);

        col.AddChild(UiTheme.Divider());

        // Looks are cosmetic and filtered by race (RaceResource.AppearanceOptionIds); the section is rebuilt on a race change.
        col.AddChild(UiTheme.SectionRule(Loc.T("create.appearance")));
        _appearanceBox = new VBoxContainer();
        _appearanceBox.AddThemeConstantOverride("separation", UiTheme.SpaceSm);
        col.AddChild(_appearanceBox);

        col.AddChild(UiTheme.Divider());

        // Backgrounds are cards too, for the same reason as races: eight soft nudges compared at a
        // glance beat a dropdown. None of them gates anything; Wayfarer (first) is the plain start.
        col.AddChild(UiTheme.SectionRule(Loc.T("create.background")));

        // The no-op default leads; the rest keep their authored order.
        foreach (BackgroundResource background in BackgroundDatabase.All)
        {
            _backgrounds.Insert(background.Id == BackgroundDatabase.DefaultId ? 0 : _backgrounds.Count, background);
        }

        _backgroundGrid = new GridContainer { Columns = 2 };
        _backgroundGrid.AddThemeConstantOverride("h_separation", UiTheme.SpaceXs);
        _backgroundGrid.AddThemeConstantOverride("v_separation", UiTheme.SpaceXs);
        col.AddChild(_backgroundGrid);

        _backgroundSummary = UiTheme.Prose(string.Empty);
        _backgroundSummary.CustomMinimumSize = new Vector2(0, 72);
        _backgroundSummary.VerticalAlignment = VerticalAlignment.Top;
        col.AddChild(_backgroundSummary);

        col.AddChild(UiTheme.Divider());

        _name = AddField(col, Loc.T("create.name"), Loc.T("create.name_hint"));

        outer.AddChild(UiTheme.Divider());

        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 10);
        Button back = UiTheme.Action(Loc.T("create.back"));
        back.CustomMinimumSize = new Vector2(0, 34);
        back.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        back.Pressed += () => { _onBack?.Invoke(); QueueFree(); };
        buttons.AddChild(back);

        Button confirm = UiTheme.Action(Loc.T("create.confirm"));
        confirm.CustomMinimumSize = new Vector2(0, 34);
        confirm.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        confirm.Pressed += OnConfirm;
        buttons.AddChild(confirm);
        outer.AddChild(buttons);

        if (_races.Count > 0)
        {
            OnRaceSelected(0);
        }

        // Preselect the explicit no-op so a player who never touches the picker gets today's start.
        int defaultBackground = _backgrounds.FindIndex(b => b.Id == BackgroundDatabase.DefaultId);
        if (_backgrounds.Count > 0)
        {
            OnBackgroundSelected(Math.Max(0, defaultBackground));
        }
    }

    /// <summary>
    /// Rebuilds the race cards, lighting the chosen one. Each card carries the race's name and its
    /// stat deltas as signed chips — green up, red down — so the trade a race makes is legible
    /// before it is picked rather than after.
    /// </summary>
    /// <summary>Runs a rebuild that frees the control the player just pressed, then puts focus back at the same place
    /// in the tree: without it a gamepad or keyboard pick leaves no focus owner and the next press does nothing.</summary>
    private void KeepFocus(System.Action rebuild)
    {
        int[]? path = UiFocus.PathOf(_panel);
        rebuild();
        UiFocus.Restore(_panel, path);
    }

    private void RebuildRaceCards() => KeepFocus(RebuildRaceCardsCore);

    private void RebuildRaceCardsCore()
    {
        UiTheme.ClearChildren(_raceGrid);

        for (int i = 0; i < _races.Count; i++)
        {
            RaceResource race = _races[i];
            bool active = ReferenceEquals(race, _selected);

            // CardButton, not a Button with children (37.5H). A Button never grows to fit what is
            // inside it, so the first version of these cards had zero height and every label drew
            // on top of the one below.
            PanelContainer card = UiTheme.CardButton(
                active ? UiTheme.Accent : null, out Button input, out VBoxContainer col);
            card.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

            int index = i;
            input.Pressed += () => OnRaceSelected(index);
            input.TooltipText = race.Description;

            Label name = UiTheme.Body(race.DisplayName, active ? UiTheme.Accent : UiTheme.Text);
            UiTheme.ApplyType(name, UiTheme.FontRole.Display, UiTheme.BodyFontSize);
            col.AddChild(name);

            // Deltas wrap rather than running off the card: six races times up to three stat chips
            // will not fit on one line at any sensible card width, and a HBox would simply overflow.
            var chips = new HFlowContainer();
            chips.AddThemeConstantOverride("h_separation", 2);
            chips.AddThemeConstantOverride("v_separation", 2);
            foreach (RaceStatDelta delta in race.StatDeltaList())
            {
                chips.AddChild(UiTheme.Chip(
                    $"{Signed(delta.Amount)} {StatNames.Label(delta.Stat)}",
                    delta.Amount >= 0f ? UiTheme.Good : UiTheme.Bad));
            }

            col.AddChild(chips);
            _raceGrid.AddChild(card);
        }
    }

    /// <summary>Rebuilds the background cards: the name, and a chip naming the perk branch it leans toward.
    /// The lean is a badge only; it unlocks and blocks nothing.</summary>
    private void RebuildBackgroundCards() => KeepFocus(RebuildBackgroundCardsCore);

    private void RebuildBackgroundCardsCore()
    {
        UiTheme.ClearChildren(_backgroundGrid);

        for (int i = 0; i < _backgrounds.Count; i++)
        {
            BackgroundResource background = _backgrounds[i];
            bool active = ReferenceEquals(background, _selectedBackground);

            PanelContainer card = UiTheme.CardButton(
                active ? UiTheme.Accent : null, out Button input, out VBoxContainer col);
            card.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

            int index = i;
            input.Pressed += () => OnBackgroundSelected(index);
            input.TooltipText = Loc.T(background.DescKey);

            Label name = UiTheme.Body(Loc.T(background.NameKey), active ? UiTheme.Accent : UiTheme.Text);
            UiTheme.ApplyType(name, UiTheme.FontRole.Display, UiTheme.BodyFontSize);
            col.AddChild(name);

            if (background.LeanBranch.Length > 0)
            {
                // In a flow container so the badge keeps its own width rather than stretching across the card.
                var badge = new HFlowContainer();
                badge.AddChild(UiTheme.Chip(
                    Loc.TF("create.bg_lean", Loc.T($"background.lean.{background.LeanBranch}")), UiTheme.Dim));
                col.AddChild(badge);
            }

            _backgroundGrid.AddChild(card);
        }
    }

    private void OnBackgroundSelected(long index)
    {
        if (index < 0 || index >= _backgrounds.Count)
        {
            return;
        }

        _selectedBackground = _backgrounds[(int)index];
        _backgroundSummary.Text = BuildBackgroundSummary(_selectedBackground);
        RebuildBackgroundCards();
    }

    /// <summary>Capture hook: scroll to the top of the background picker, or to the very end (its summary and the name field).</summary>
    public async void ScrollForCapture(bool toEnd)
    {
        // Two frames: the scroll range grows only after the rebuilt cards and summary have been laid out.
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        _scroll.ScrollVertical = toEnd
            ? int.MaxValue
            : Mathf.Max(0, (int)(_backgroundGrid.GlobalPosition.Y - _scroll.GlobalPosition.Y) + _scroll.ScrollVertical - 24);
    }

    /// <summary>Capture/dev hook: choose a race by id, then appearance options by id (each as a click on its swatch
    /// would; an id the race does not offer is ignored), and scroll the appearance section into view.</summary>
    public void SelectLookForCapture(string raceId, params string[] optionIds)
    {
        OnRaceSelected(_races.FindIndex(r => r.Id == raceId));
        foreach (string id in optionIds)
        {
            if (_selected != null && _selected.AppearanceOptionIds.Contains(id) && AppearanceDatabase.Get(id) is { } option)
            {
                _appearancePicks[(int)option.Slot] = id;
            }
        }

        RebuildAppearance();
        ScrollAppearanceIntoView();
    }

    private async void ScrollAppearanceIntoView()
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        _scroll.ScrollVertical = Mathf.Max(0, (int)(_appearanceBox.GlobalPosition.Y - _scroll.GlobalPosition.Y) + _scroll.ScrollVertical - 40);
    }

    /// <summary>Capture/dev hook: choose a background by id as a click on its card would.</summary>
    public void SelectBackgroundForCapture(string id) =>
        OnBackgroundSelected(_backgrounds.FindIndex(b => b.Id == id));

    /// <summary>What a background gives, stated in full before it is picked: its description, the free perk,
    /// attribute deltas, the kit (gold last) and standing tweaks. A background with none of these says so.</summary>
    private static string BuildBackgroundSummary(BackgroundResource background)
    {
        var sb = new StringBuilder();
        sb.AppendLine(Loc.T(background.DescKey));

        var lines = new List<string>();
        if (PerkDatabase.Get(background.StartingPerkId) is { } perk)
        {
            lines.Add($"{Loc.T("create.bg_perk")}: {perk.DisplayName}");
        }

        foreach (RaceStatDelta delta in background.StatDeltaList())
        {
            lines.Add($"{Loc.T("create.bg_stat")}: {Loc.TF("create.stat_line", Signed(delta.Amount), StatNames.Label(delta.Stat))}");
        }

        var kit = new List<string>();
        foreach (string entry in background.StartingItems)
        {
            if (BackgroundRules.TryParseItem(entry, out string itemId, out int count) &&
                ItemDatabase.Get(itemId) is { } item)
            {
                kit.Add(count > 1 ? Loc.TF("create.bg_item", item.DisplayName, count) : item.DisplayName);
            }
        }

        if (background.StartingGold > 0)
        {
            kit.Add(Loc.TF("create.bg_gold", background.StartingGold));
        }

        if (kit.Count > 0)
        {
            lines.Add($"{Loc.T("create.bg_kit")}: {string.Join(", ", kit)}");
        }

        foreach (RaceReputationTweak tweak in background.ReputationTweakList())
        {
            string faction = FactionDatabase.Get(tweak.FactionId)?.DisplayName ?? tweak.FactionId;
            lines.Add($"{Loc.T("create.bg_standing")}: {Loc.TF("create.rep_line", Signed(tweak.Amount), faction)}");
        }

        sb.AppendLine();
        sb.AppendLine(lines.Count > 0 ? string.Join("\n", lines) : Loc.T("create.bg_nothing"));
        return sb.ToString().TrimEnd();
    }

    private static LineEdit AddField(VBoxContainer col, string label, string placeholder)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        Label caption = UiTheme.Body(label, UiTheme.Dim);
        caption.CustomMinimumSize = new Vector2(150, 0);
        row.AddChild(caption);

        var field = new LineEdit
        {
            PlaceholderText = placeholder,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        field.AddThemeColorOverride("font_color", UiTheme.Text);
        row.AddChild(field);
        col.AddChild(row);
        return field;
    }

    private void OnRaceSelected(long index)
    {
        if (index < 0 || index >= _races.Count)
        {
            return;
        }

        _selected = _races[(int)index];
        _summary.Text = BuildSummary(_selected);
        RebuildRaceCards();

        // Keep each pick the new race also offers; anything else falls back to the slot default.
        _appearancePicks = AppearanceRules.Resolve(
            _appearancePicks,
            id => AppearanceDatabase.Get(id)?.Slot,
            id => _selected.AppearanceOptionIds.Contains(id),
            slot => AppearanceDatabase.DefaultFor(slot)?.Id);
        RebuildAppearance();
    }

    /// <summary>One row per slot: its name and the chosen option, then a swatch per option the race offers
    /// (build options are small cards). Redraws the preview with the current picks.</summary>
    private void RebuildAppearance() => KeepFocus(RebuildAppearanceCore);

    private void RebuildAppearanceCore()
    {
        UiTheme.ClearChildren(_appearanceBox);
        _appearanceBox.AddChild(UiTheme.Caption(Loc.T("create.appearance_hint")));

        for (int i = 0; i < AppearanceRules.SlotCount; i++)
        {
            var slot = (AppearanceSlot)i;
            List<AppearanceOptionResource> options = PlayerAppearance.OptionsFor(_selected, slot);
            if (options.Count < 2)
            {
                continue; // nothing to choose between
            }

            AppearanceOptionResource? chosen = AppearanceDatabase.Get(_appearancePicks[i]);
            string chosenName = chosen != null ? Loc.T(chosen.NameKey) : string.Empty;
            _appearanceBox.AddChild(UiTheme.Body(
                $"{Loc.T($"create.appearance.{slot.ToString().ToLowerInvariant()}")}: {chosenName}", UiTheme.Dim));

            var flow = new HFlowContainer();
            flow.AddThemeConstantOverride("h_separation", UiTheme.SpaceXs);
            flow.AddThemeConstantOverride("v_separation", UiTheme.SpaceXs);
            foreach (AppearanceOptionResource option in options)
            {
                bool active = option.Id == _appearancePicks[i];
                int slotIndex = i;
                string id = option.Id;
                void Pick() => OnAppearancePicked(slotIndex, id);
                flow.AddChild(slot == AppearanceSlot.Build
                    ? BuildCard(option, active, Pick)
                    : Swatch(option, active, Pick));
            }

            _appearanceBox.AddChild(flow);
        }

        _preview.SetLook(PlayerAppearance.Resolve(_selected, _appearancePicks));
    }

    private void OnAppearancePicked(int slot, string id)
    {
        _appearancePicks[slot] = id;
        RebuildAppearance();
    }

    /// <summary>A square colour button for one option; the chosen one wears a bright border.</summary>
    private static Button Swatch(AppearanceOptionResource option, bool active, System.Action onPressed)
    {
        var button = new Button
        {
            CustomMinimumSize = new Vector2(34f, 34f),
            FocusMode = Control.FocusModeEnum.All,
            TooltipText = Loc.T(option.NameKey),
        };

        // True colour, not UiTheme.Adapt: the swatch is the thing the player is choosing.
        StyleBoxFlat Style(Color border, int width)
        {
            var box = new StyleBoxFlat { BgColor = option.Tint, BorderColor = border };
            box.SetBorderWidthAll(width);
            box.SetCornerRadiusAll(UiTheme.RadiusSm);
            return box;
        }

        StyleBoxFlat normal = Style(active ? UiTheme.Accent : UiTheme.Iron, active ? 3 : 1);
        StyleBoxFlat hover = Style(UiTheme.AccentHot, 2);
        button.AddThemeStyleboxOverride("normal", normal);
        button.AddThemeStyleboxOverride("hover", hover);
        button.AddThemeStyleboxOverride("pressed", hover);
        button.AddThemeStyleboxOverride("focus", Style(Colors.White, 3));
        button.Pressed += () => onPressed();
        return button;
    }

    /// <summary>A small named card for one Build option.</summary>
    private static PanelContainer BuildCard(AppearanceOptionResource option, bool active, System.Action onPressed)
    {
        PanelContainer card = UiTheme.CardButton(active ? UiTheme.Accent : null, out Button input, out VBoxContainer col);
        input.Pressed += () => onPressed();
        col.AddChild(UiTheme.Body(Loc.T(option.NameKey), active ? UiTheme.Accent : UiTheme.Text));
        return card;
    }

    private static string BuildSummary(RaceResource race)
    {
        var sb = new StringBuilder();
        sb.AppendLine(race.Description);

        List<RaceStatDelta> deltas = race.StatDeltaList();
        if (deltas.Count > 0)
        {
            sb.AppendLine();
            foreach (RaceStatDelta delta in deltas)
            {
                sb.AppendLine(Loc.TF("create.stat_line", Signed(delta.Amount), StatNames.Label(delta.Stat)));
            }
        }

        var innate = new List<string>();
        foreach (string perkId in race.InnatePerkIds)
        {
            if (PerkDatabase.Get(perkId) is { } perk)
            {
                innate.Add(perk.DisplayName);
            }
        }

        foreach (string spellId in race.InnateSpellIds)
        {
            if (SpellDatabase.Get(spellId) is { } spell)
            {
                innate.Add(spell.DisplayName);
            }
        }

        if (innate.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"{Loc.T("create.innate")}: {string.Join(", ", innate)}");
        }

        foreach (RaceReputationTweak tweak in race.ReputationTweakList())
        {
            string faction = FactionDatabase.Get(tweak.FactionId)?.DisplayName ?? tweak.FactionId;
            sb.AppendLine(Loc.TF("create.rep_line", Signed(tweak.Amount), faction));
        }

        return sb.ToString().TrimEnd();
    }

    // Signed numeric prefix for a delta, e.g. "+5", "-0.4". Trailing zeros trimmed; not language-sensitive.
    private static string Signed(float amount)
    {
        string magnitude = amount.ToString("0.##", CultureInfo.InvariantCulture);
        return amount >= 0f ? $"+{magnitude}" : magnitude;
    }

    private static string Signed(int amount) => amount >= 0 ? $"+{amount}" : amount.ToString(CultureInfo.InvariantCulture);

    private void OnConfirm()
    {
        if (_selected == null)
        {
            return;
        }

        // A blank name keeps CharacterProfile's "Wanderer" default rather than an empty string.
        var profile = new CharacterProfile
        {
            RaceId = _selected.Id,
            Background = _selectedBackground?.Id ?? string.Empty,
            AppearanceOptionIds = AppearanceRules.ToProfileIds(_appearancePicks),
        };

        string name = _name.Text.Trim();
        if (!string.IsNullOrEmpty(name))
        {
            profile.CharacterName = name;
        }

        _onConfirm?.Invoke(profile);
        QueueFree();
    }
}
