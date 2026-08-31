using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Embervale.Factions;
using Embervale.Localization;
using Embervale.Magic;
using Embervale.Progression;
using Embervale.Races;
using Embervale.Stats;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The new-game character creator (Phase 26D): the player picks a race (with a live trait summary)
/// and a name before the world is built, producing the <see cref="CharacterProfile"/> the bootstrap
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
    private LineEdit _background = null!;
    private PanelContainer _panel = null!;
    private GridContainer _raceGrid = null!;

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
        if (Godot.Input.IsActionJustPressed("ui_cancel") &&
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
        outer.AddChild(scroll);

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

        _name = AddField(col, Loc.T("create.name"), Loc.T("create.name_hint"));
        _background = AddField(col, Loc.T("create.background"), Loc.T("create.background_hint"));

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
    }

    /// <summary>
    /// Rebuilds the race cards, lighting the chosen one. Each card carries the race's name and its
    /// stat deltas as signed chips — green up, red down — so the trade a race makes is legible
    /// before it is picked rather than after.
    /// </summary>
    private void RebuildRaceCards()
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
            Background = _background.Text.Trim(),
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
