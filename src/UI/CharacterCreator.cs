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
    }

    private void Build()
    {
        var backdrop = new ColorRect { Color = new Color(0.02f, 0.02f, 0.04f, 0.92f) };
        backdrop.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        backdrop.MouseFilter = Control.MouseFilterEnum.Stop;
        AddChild(backdrop);

        PanelContainer panel = UiTheme.Panel();
        panel.SetAnchorsPreset(Control.LayoutPreset.Center);
        panel.GrowHorizontal = Control.GrowDirection.Both;
        panel.GrowVertical = Control.GrowDirection.Both;
        panel.CustomMinimumSize = new Vector2(560, 0);
        AddChild(panel);

        MarginContainer pad = UiTheme.Padding(18);
        panel.AddChild(pad);

        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 10);
        pad.AddChild(col);

        col.AddChild(UiTheme.Header(Loc.T("create.title")));
        col.AddChild(new HSeparator());

        // Race picker.
        foreach (RaceResource race in RaceDatabase.All)
        {
            _races.Add(race);
        }

        var raceNames = new string[_races.Count];
        for (int i = 0; i < _races.Count; i++)
        {
            raceNames[i] = _races[i].DisplayName;
        }

        var raceRow = new HBoxContainer();
        raceRow.AddThemeConstantOverride("separation", 10);
        Label raceLabel = UiTheme.Body(Loc.T("create.race"), UiTheme.Dim);
        raceLabel.CustomMinimumSize = new Vector2(150, 0);
        raceRow.AddChild(raceLabel);
        OptionButton raceDropdown = UiTheme.Dropdown(raceNames, _races.Count > 0 ? 0 : -1);
        raceDropdown.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        raceDropdown.ItemSelected += OnRaceSelected;
        raceRow.AddChild(raceDropdown);
        col.AddChild(raceRow);

        // Trait summary, rebuilt on each race change.
        _summary = UiTheme.Body(string.Empty);
        _summary.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _summary.CustomMinimumSize = new Vector2(0, 150);
        _summary.VerticalAlignment = VerticalAlignment.Top;
        col.AddChild(_summary);

        col.AddChild(new HSeparator());

        _name = AddField(col, Loc.T("create.name"), Loc.T("create.name_hint"));
        _background = AddField(col, Loc.T("create.background"), Loc.T("create.background_hint"));

        col.AddChild(new HSeparator());

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
        col.AddChild(buttons);

        if (_races.Count > 0)
        {
            OnRaceSelected(0);
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
