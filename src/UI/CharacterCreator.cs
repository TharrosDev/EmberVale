using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Embervale.Appearance;
using Embervale.Backgrounds;
using Embervale.Core;
using Embervale.Core.Events;
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
/// a look, a background (a soft nudge, with its kit and perk spelled out) and a name before the
/// world is built, producing the <see cref="CharacterProfile"/> the bootstrap spawns from. Opened by
/// <see cref="MainMenu"/> after the New-Game slot is chosen; a <see cref="CanvasLayer"/> built
/// through <see cref="UiTheme"/>, all strings via <see cref="Loc"/>.
///
/// <para>Three columns on one sheet: the four steps down a rail, the step's own choices in the
/// middle, and the figure on a turntable (<see cref="CharacterPreview"/>) that stays put whatever
/// is being chosen. A strip under the title carries the race, the background and the stat nudges
/// the two add up to, so no step has to be revisited to remember what was picked. Steps are in any
/// order and none is required: Begin works from the first frame.</para>
/// </summary>
public partial class CharacterCreator : CanvasLayer
{
    /// <summary>Columns of the race and background card grids.</summary>
    private const int CardColumns = 2;

    /// <summary>The longest name the field takes: what a save row and the HUD can show whole.</summary>
    private const int NameMaxLength = 24;

    private static readonly string[] StepKeys =
    {
        "create.race", "create.appearance", "create.step.background", "create.name",
    };

    private static readonly string[] RigKeys = { "create.rig.hearth", "create.rig.overcast", "create.rig.dusk" };

    private Action<CharacterProfile>? _onConfirm;
    private Action? _onBack;

    private readonly List<RaceResource> _races = new();
    private RaceResource? _selected;
    private readonly List<BackgroundResource> _backgrounds = new();
    private BackgroundResource? _selectedBackground;
    // The chosen option id per AppearanceSlot; the slot default until the player picks.
    private string[] _appearancePicks = Enumerable.Repeat(string.Empty, AppearanceRules.SlotCount).ToArray();
    private string _nameText = string.Empty;

    private CreatorStep _step = CreatorStep.Race;
    private AppearanceSlot? _activeSlot;
    private readonly Random _random = new();

    private Control _root = null!;
    private readonly Button[] _rail = new Button[CreatorRules.StepCount];
    private ScrollContainer _scroll = null!;
    private VBoxContainer _options = null!;
    private HFlowContainer _summary = null!;
    private CharacterPreview _preview = null!;
    private Label _turnHint = null!;
    private Button _rigButton = null!;
    private Button _confirm = null!;
    private UiLegend _legend = null!;
    // The card buttons of the step on screen, in grid order; empty for a step that is not a grid.
    private readonly List<Button> _cards = new();

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
        UiFocus.GrabFirst(_options); // land on the race picker (30.5J)
    }

    public override void _EnterTree() => EventBus.Instance?.Subscribe<InputDeviceChangedEvent>(OnDeviceChanged);

    public override void _ExitTree() => EventBus.Instance?.Unsubscribe<InputDeviceChangedEvent>(OnDeviceChanged);

    // The turntable is a drag on one device and a stick on the other; the hint and legend follow.
    private void OnDeviceChanged(InputDeviceChangedEvent e) => UpdateLegend();

    public override void _Process(double delta)
    {
        // The right stick turns the figure, whatever has focus.
        float turn = Godot.Input.GetAxis(UiLive.LookLeft, UiLive.LookRight);
        if (!Mathf.IsZeroApprox(turn))
        {
            _preview.Turn(turn * CreatorRules.TurnDegreesPerSecond * (float)delta);
        }

        // A text field owns the keyboard: Esc there means "stop typing", not "leave the creator",
        // and Q / E are letters of a name.
        if (GetViewport().GuiGetFocusOwner() is LineEdit)
        {
            return;
        }

        // Esc / gamepad B backs out (30.5J).
        if (Godot.Input.IsActionJustPressed(UiLive.UiCancel))
        {
            UiAudio.Play(UiCue.Back);
            Back();
            return;
        }

        int step = (Godot.Input.IsActionJustPressed(UiLive.MenuTabNext) ? 1 : 0)
                   - (Godot.Input.IsActionJustPressed(UiLive.MenuTabPrev) ? 1 : 0);
        if (step != 0)
        {
            UiAudio.Play(UiCue.Tab);
            ShowStep(CreatorRules.Step(_step, step));
        }
    }

    private void Back()
    {
        _onBack?.Invoke();
        QueueFree();
    }

    private void Build()
    {
        Vector2 view = GetViewport().GetVisibleRect().Size;
        bool narrow = view.X < UiTheme.CreatorNarrowWidth;
        float width = Mathf.Min(view.X - (UiChromeRules.Gutter(view.X) * 2f), UiTheme.SessionSheetMaxWidth);

        // A sheet, not a framed panel. It fills the height it is given so the three columns do.
        (Control root, VBoxContainer col) = UiTheme.Sheet(width, 0.92f, centred: true);
        col.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _root = root;
        AddChild(root);

        col.AddChild(UiTheme.Title(Loc.T("create.title")));
        col.AddChild(UiOrnament.EmberWipe());

        _summary = UiTheme.FlowRow();
        col.AddChild(_summary);

        var body = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", UiTheme.SpaceMd);
        col.AddChild(body);

        // The rail: the four steps, the one on screen lit.
        var rail = new VBoxContainer
        {
            CustomMinimumSize = new Vector2(narrow ? UiTheme.CreatorRailNarrowWidth : UiTheme.CreatorRailWidth, 0f),
        };
        rail.AddThemeConstantOverride("separation", 0);
        for (int i = 0; i < _rail.Length; i++)
        {
            var step = (CreatorStep)i;
            _rail[i] = UiTheme.SessionAction(Loc.T(StepKeys[i]), UiCue.Tab);
            _rail[i].Pressed += () => ShowStep(step);
            rail.AddChild(_rail[i]);
        }

        body.AddChild(rail);
        body.AddChild(new ColorRect
        {
            Color = UiTheme.Rule,
            CustomMinimumSize = new Vector2(1f, 1f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });

        // The step's choices. They scroll inside the sheet: on the 533 px logical viewport a
        // Steam Deck reports at UI scale 1.5 no step fits whole (37.5H).
        (_scroll, _options) = UiTheme.ScrollList();
        _scroll.CustomMinimumSize = new Vector2(0f, UiTheme.CreatorBodyMinHeight);
        _options.AddThemeConstantOverride("separation", UiTheme.SpaceSm);
        body.AddChild(_scroll);

        // The figure, its light and how to turn it. The well takes whatever height the sheet has,
        // so the preview is tall on a desktop and shrinks to its floor on a handheld.
        var side = new VBoxContainer
        {
            CustomMinimumSize = new Vector2(narrow ? UiTheme.CreatorPreviewNarrowWidth : UiTheme.CreatorPreviewWidth, 0f),
        };
        side.AddThemeConstantOverride("separation", UiTheme.SpaceXs);
        body.AddChild(side);

        PanelContainer well = UiTheme.Well();
        well.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _preview = new CharacterPreview();
        well.AddChild(_preview);
        side.AddChild(well);

        _turnHint = UiTheme.Caption(Loc.T("create.turn_hint"));
        _turnHint.HorizontalAlignment = HorizontalAlignment.Center;
        side.AddChild(_turnHint);

        _rigButton = UiTheme.Action(string.Empty);
        _rigButton.Alignment = HorizontalAlignment.Center;
        _rigButton.Pressed += () => SetRig(CreatorRules.NextRig(_preview.RigIndex));
        side.AddChild(_rigButton);

        col.AddChild(UiTheme.SessionRule());

        // A footer row never pins its width: the spacer is the one child that grows.
        var footer = new HBoxContainer();
        footer.AddThemeConstantOverride("separation", UiTheme.SpaceSm);
        Button back = UiTheme.Action(Loc.T("create.back"), UiCue.Back);
        back.Pressed += Back;
        footer.AddChild(back);
        footer.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });

        Button randomise = UiTheme.Action(Loc.T("create.randomise"));
        randomise.TooltipText = Loc.T("create.randomise_hint");
        randomise.Pressed += () => Randomise(_random);
        footer.AddChild(randomise);

        _confirm = UiTheme.Action(Loc.T("create.confirm"), UiCue.Confirm);
        _confirm.Pressed += OnConfirm;
        footer.AddChild(_confirm);
        col.AddChild(footer);

        _legend = new UiLegend();
        AddChild(_legend);

        foreach (RaceResource race in RaceDatabase.All)
        {
            _races.Add(race);
        }

        // The no-op default leads; the rest keep their authored order.
        foreach (BackgroundResource background in BackgroundDatabase.All)
        {
            _backgrounds.Insert(background.Id == BackgroundDatabase.DefaultId ? 0 : _backgrounds.Count, background);
        }

        // Preselect the first race and the explicit no-op background, so a player who never
        // touches a picker gets today's start.
        if (_races.Count > 0)
        {
            SelectRace(_races[0]);
        }

        if (_backgrounds.Count > 0)
        {
            _selectedBackground = _backgrounds[Math.Max(0, _backgrounds.FindIndex(b => b.Id == BackgroundDatabase.DefaultId))];
        }

        SetRig(0);
        RefreshSummary();
        ShowStep(_step);
    }

    // --- Steps ----------------------------------------------------------------

    /// <summary>Puts <paramref name="step"/>'s choices in the middle column and lights it on the rail.</summary>
    private void ShowStep(CreatorStep step)
    {
        _step = step;
        _activeSlot = null;
        for (int i = 0; i < _rail.Length; i++)
        {
            bool lit = i == (int)step;
            StyleBoxFlat rest = UiTheme.SessionActionStyle(focused: false, lit);
            _rail[i].AddThemeStyleboxOverride("normal", rest);
            _rail[i].AddThemeStyleboxOverride("disabled", rest);
            _rail[i].AddThemeStyleboxOverride("focus", UiTheme.SessionActionStyle(focused: true, lit));
            _rail[i].AddThemeColorOverride("font_color", lit ? UiTheme.Accent : UiTheme.Text);
        }

        // Stepping with the shoulder buttons from inside one step lands inside the next; stepping
        // from the rail leaves focus on the rail, which is not rebuilt.
        bool inOptions = GetViewport()?.GuiGetFocusOwner() is { } focus && _options.IsAncestorOf(focus);
        RebuildOptions(keepFocus: false);
        if (inOptions)
        {
            UiFocus.GrabFirst(_options);
        }

        _scroll.ScrollVertical = 0;
        _preview.SetFraming(CreatorRules.FramingFor(_step, _activeSlot));
        UpdateLegend();
    }

    /// <summary>
    /// Rebuilds the step on screen. A rebuild frees the control the player just pressed, so with
    /// <paramref name="keepFocus"/> focus is put back at the same place in the tree: without that a
    /// gamepad or keyboard pick leaves no focus owner and the next press does nothing.
    /// </summary>
    private void RebuildOptions(bool keepFocus = true)
    {
        int[]? path = keepFocus ? UiFocus.PathOf(_root) : null;
        UiTheme.ClearChildren(_options);
        _cards.Clear();

        switch (_step)
        {
            case CreatorStep.Race: BuildRaceStep(); break;
            case CreatorStep.Appearance: BuildAppearanceStep(); break;
            case CreatorStep.Background: BuildBackgroundStep(); break;
            default: BuildNameStep(); break;
        }

        WireFocus();
        UiFocus.Restore(_root, path);
    }

    /// <summary>
    /// The focus wiring a card grid needs: left, right, up and down are the cards beside, above and
    /// below, and the grid's left edge is the rail. Without it a d-pad walks the tab order, not the
    /// grid. The rail's right is the first choice of the step. Paths, so only once the step is in
    /// the tree; and re-made on every rebuild, because a path to a freed card is an error, not a miss.
    /// </summary>
    private void WireFocus()
    {
        Button rail = _rail[(int)_step];
        for (int i = 0; i < _cards.Count; i++)
        {
            Button card = _cards[i];
            int left = CreatorRules.GridNeighbour(i, _cards.Count, CardColumns, -1, 0);
            int right = CreatorRules.GridNeighbour(i, _cards.Count, CardColumns, 1, 0);
            int up = CreatorRules.GridNeighbour(i, _cards.Count, CardColumns, 0, -1);
            int down = CreatorRules.GridNeighbour(i, _cards.Count, CardColumns, 0, 1);
            card.FocusNeighborLeft = card.GetPathTo(left >= 0 ? _cards[left] : rail);
            card.FocusNeighborRight = card.GetPathTo(right >= 0 ? _cards[right] : _rigButton);
            if (up >= 0)
            {
                card.FocusNeighborTop = card.GetPathTo(_cards[up]);
            }

            card.FocusNeighborBottom = card.GetPathTo(down >= 0 ? _cards[down] : _confirm);
        }

        // The rail never keeps a path into a step that has been rebuilt away.
        Control? first = _cards.Count > 0 ? _cards[0] : FirstFocusableOption();
        foreach (Button step in _rail)
        {
            step.FocusNeighborRight = first != null ? step.GetPathTo(first) : new NodePath();
        }
    }

    private Control? FirstFocusableOption()
    {
        var pending = new Stack<Node>();
        pending.Push(_options);
        while (pending.Count > 0)
        {
            Node node = pending.Pop();
            if (node is Control { FocusMode: Control.FocusModeEnum.All } control && control != _options)
            {
                return control;
            }

            for (int i = node.GetChildCount() - 1; i >= 0; i--)
            {
                pending.Push(node.GetChild(i));
            }
        }

        return null;
    }

    // --- Race -------------------------------------------------------------------

    /// <summary>
    /// Race picking is a *card grid*, not a dropdown (37.5H): this is the first real decision the
    /// player makes and the only one they cannot revise later, so every race sits on screen at once.
    /// Each card carries the race's name and its stat deltas as signed chips, so the trade a race
    /// makes is legible before it is picked rather than after.
    /// </summary>
    private void BuildRaceStep()
    {
        var grid = new GridContainer { Columns = CardColumns };
        grid.AddThemeConstantOverride("h_separation", UiTheme.GridGap);
        grid.AddThemeConstantOverride("v_separation", UiTheme.GridGap);
        _options.AddChild(grid);

        foreach (RaceResource race in _races)
        {
            bool active = ReferenceEquals(race, _selected);

            // CardButton, not a Button with children (37.5H). A Button never grows to fit what is
            // inside it, so the first version of these cards had zero height.
            PanelContainer card = UiTheme.CardButton(
                active ? UiTheme.Accent : null, out Button input, out VBoxContainer col);
            card.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

            RaceResource picked = race;
            input.Pressed += () =>
            {
                SelectRace(picked);
                RefreshSummary();
                RebuildOptions();
            };
            input.TooltipText = race.Description;

            Label name = UiTheme.Body(race.DisplayName, active ? UiTheme.Accent : UiTheme.Text);
            UiTheme.ApplyType(name, UiTheme.FontRole.Display, UiTheme.BodyFontSize);
            col.AddChild(name);

            // Deltas wrap rather than running off the card.
            HFlowContainer chips = UiTheme.FlowRow();
            foreach (RaceStatDelta delta in race.StatDeltaList())
            {
                chips.AddChild(DeltaChip(delta.Stat, delta.Amount));
            }

            col.AddChild(chips);
            grid.AddChild(card);
            _cards.Add(input);
        }

        if (_selected != null)
        {
            _options.AddChild(UiTheme.Prose(BuildSummary(_selected)));
        }
    }

    /// <summary>Makes <paramref name="race"/> the chosen one and brings the look along: each pick
    /// the new race also offers is kept; anything else falls back to the slot default.</summary>
    private void SelectRace(RaceResource race)
    {
        _selected = race;
        _appearancePicks = AppearanceRules.Resolve(
            _appearancePicks,
            id => AppearanceDatabase.Get(id)?.Slot,
            id => race.AppearanceOptionIds.Contains(id),
            slot => AppearanceDatabase.DefaultFor(slot)?.Id);
        ApplyLook();
    }

    // --- Appearance -------------------------------------------------------------

    /// <summary>One row per slot: its name and the chosen option, then a swatch per option the race
    /// offers (build options are small cards). Looks are cosmetic and filtered by race
    /// (<see cref="RaceResource.AppearanceOptionIds"/>).</summary>
    private void BuildAppearanceStep()
    {
        _options.AddChild(UiTheme.Caption(Loc.T("create.appearance_hint")));

        for (int i = 0; i < AppearanceRules.SlotCount; i++)
        {
            var slot = (AppearanceSlot)i;
            List<AppearanceOptionResource> options = PlayerAppearance.OptionsFor(_selected, slot);
            if (options.Count < 2)
            {
                continue; // nothing to choose between
            }

            var block = new VBoxContainer();
            block.AddThemeConstantOverride("separation", UiTheme.SpaceXs);
            _options.AddChild(block);

            AppearanceOptionResource? chosen = AppearanceDatabase.Get(_appearancePicks[i]);
            string chosenName = chosen != null ? Loc.T(chosen.NameKey) : string.Empty;
            block.AddChild(UiTheme.Body(
                $"{Loc.T($"create.appearance.{slot.ToString().ToLowerInvariant()}")}: {chosenName}", UiTheme.Dim));

            HFlowContainer flow = UiTheme.FlowRow();
            flow.AddThemeConstantOverride("h_separation", UiTheme.SpaceXs);
            block.AddChild(flow);
            foreach (AppearanceOptionResource option in options)
            {
                bool active = option.Id == _appearancePicks[i];
                int slotIndex = i;
                string id = option.Id;
                void Pick() => OnAppearancePicked(slotIndex, id);
                Button input;
                if (slot == AppearanceSlot.Build)
                {
                    flow.AddChild(BuildCard(option, active, Pick, out input));
                }
                else
                {
                    input = Swatch(option, active, Pick);
                    flow.AddChild(input);
                }

                // The camera goes to what is being chosen: the face for hair and eyes.
                input.FocusEntered += () => FocusSlot(slot);
            }
        }
    }

    private void OnAppearancePicked(int slot, string id)
    {
        _appearancePicks[slot] = id;
        ApplyLook();
        RebuildOptions();
    }

    private void FocusSlot(AppearanceSlot slot)
    {
        _activeSlot = slot;
        _preview.SetFraming(CreatorRules.FramingFor(_step, _activeSlot));
    }

    private void ApplyLook() => _preview.SetLook(PlayerAppearance.Resolve(_selected, _appearancePicks));

    /// <summary>A square colour button for one option, a full control across so a thumb can hit
    /// it; the chosen one wears a heavy ember border, the focused one the focus ring.</summary>
    private static Button Swatch(AppearanceOptionResource option, bool active, Action onPressed)
    {
        var button = new Button
        {
            CustomMinimumSize = new Vector2(UiTheme.ControlHeight, UiTheme.ControlHeight),
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

        StyleBoxFlat normal = Style(active ? UiTheme.Accent : UiTheme.Iron, active ? 4 : 1);
        StyleBoxFlat hover = Style(UiTheme.AccentHot, 2);
        button.AddThemeStyleboxOverride("normal", normal);
        button.AddThemeStyleboxOverride("hover", hover);
        button.AddThemeStyleboxOverride("pressed", hover);
        button.AddThemeStyleboxOverride("focus", Style(UiTheme.FocusRing, 3));
        button.Pressed += () =>
        {
            UiAudio.Play(UiCue.Click);
            onPressed();
        };
        return button;
    }

    /// <summary>A small named card for one Build option.</summary>
    private static PanelContainer BuildCard(AppearanceOptionResource option, bool active, Action onPressed, out Button input)
    {
        PanelContainer card = UiTheme.CardButton(active ? UiTheme.Accent : null, out input, out VBoxContainer col);
        input.Pressed += () => onPressed();
        col.AddChild(UiTheme.Body(Loc.T(option.NameKey), active ? UiTheme.Accent : UiTheme.Text));
        return card;
    }

    /// <summary>A random look from what the chosen race offers. Race, background and name are
    /// left alone: there is no authored list of names to draw one from, and a random race would
    /// throw away a decision the player cannot take back in play.</summary>
    private void Randomise(Random random)
    {
        var offered = new List<AppearanceOptionResource>[AppearanceRules.SlotCount];
        var counts = new int[offered.Length];
        for (int i = 0; i < offered.Length; i++)
        {
            offered[i] = PlayerAppearance.OptionsFor(_selected, (AppearanceSlot)i);
            counts[i] = offered[i].Count;
        }

        int[] picks = CreatorRules.RandomPicks(counts, random);
        for (int i = 0; i < picks.Length; i++)
        {
            if (picks[i] >= 0)
            {
                _appearancePicks[i] = offered[i][picks[i]].Id;
            }
        }

        ApplyLook();
        if (_step == CreatorStep.Appearance)
        {
            RebuildOptions();
        }
    }

    // --- Background -------------------------------------------------------------

    /// <summary>Backgrounds are cards too, for the same reason as races: eight soft nudges compared
    /// at a glance beat a dropdown. Each carries a chip naming the perk branch it leans toward; the
    /// lean is a badge only and unlocks and blocks nothing. Wayfarer (first) is the plain start.</summary>
    private void BuildBackgroundStep()
    {
        var grid = new GridContainer { Columns = CardColumns };
        grid.AddThemeConstantOverride("h_separation", UiTheme.GridGap);
        grid.AddThemeConstantOverride("v_separation", UiTheme.GridGap);
        _options.AddChild(grid);

        foreach (BackgroundResource background in _backgrounds)
        {
            bool active = ReferenceEquals(background, _selectedBackground);

            PanelContainer card = UiTheme.CardButton(
                active ? UiTheme.Accent : null, out Button input, out VBoxContainer col);
            card.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

            BackgroundResource picked = background;
            input.Pressed += () =>
            {
                _selectedBackground = picked;
                RefreshSummary();
                RebuildOptions();
            };
            input.TooltipText = Loc.T(background.DescKey);

            Label name = UiTheme.Body(Loc.T(background.NameKey), active ? UiTheme.Accent : UiTheme.Text);
            UiTheme.ApplyType(name, UiTheme.FontRole.Display, UiTheme.BodyFontSize);
            col.AddChild(name);

            if (background.LeanBranch.Length > 0)
            {
                // In a flow container so the badge keeps its own width rather than stretching across the card.
                HFlowContainer badge = UiTheme.FlowRow();
                badge.AddChild(UiTheme.Chip(
                    Loc.TF("create.bg_lean", Loc.T($"background.lean.{background.LeanBranch}")), UiTheme.Dim));
                col.AddChild(badge);
            }

            grid.AddChild(card);
            _cards.Add(input);
        }

        if (_selectedBackground != null)
        {
            _options.AddChild(UiTheme.Prose(BuildBackgroundSummary(_selectedBackground)));
        }
    }

    // --- Name -------------------------------------------------------------------

    private void BuildNameStep()
    {
        _options.AddChild(UiTheme.Body(Loc.T("create.name"), UiTheme.Dim));

        LineEdit field = UiSkin.Apply(new LineEdit
        {
            Text = _nameText,
            PlaceholderText = Loc.T("create.name_hint"),
            MaxLength = NameMaxLength,
            CustomMinimumSize = new Vector2(0f, UiTheme.ControlHeight),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        });
        field.AddThemeColorOverride("font_color", UiTheme.Text);
        field.TextChanged += text => _nameText = text;

        // Enter is "done typing": on to Begin, which is where a finished name leads.
        field.TextSubmitted += _ => _confirm.GrabFocus();
        _options.AddChild(field);

        // A blank name keeps CharacterProfile's default rather than an empty string; say which.
        Label blank = UiTheme.Caption(Loc.TF("create.name_blank", new CharacterProfile().CharacterName));
        blank.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _options.AddChild(blank);
    }

    // --- Summary strip, light, legend ---------------------------------------------

    /// <summary>The strip under the title: the race, the background, and what the two together do
    /// to the character's stats. A sign leads every nudge, so it does not rest on green and red.</summary>
    private void RefreshSummary()
    {
        UiTheme.ClearChildren(_summary);
        if (_selected != null)
        {
            _summary.AddChild(UiTheme.Chip(_selected.DisplayName, UiTheme.Accent));
        }

        if (_selectedBackground != null)
        {
            _summary.AddChild(UiTheme.Chip(Loc.T(_selectedBackground.NameKey), UiTheme.Text));
        }

        var deltas = new List<(StatType Stat, float Amount)>();
        foreach (RaceStatDelta delta in _selected?.StatDeltaList() ?? new List<RaceStatDelta>())
        {
            deltas.Add((delta.Stat, delta.Amount));
        }

        foreach (RaceStatDelta delta in _selectedBackground?.StatDeltaList() ?? new List<RaceStatDelta>())
        {
            deltas.Add((delta.Stat, delta.Amount));
        }

        foreach ((StatType stat, float amount) in CreatorRules.CombineDeltas(deltas))
        {
            _summary.AddChild(DeltaChip(stat, amount));
        }
    }

    private static PanelContainer DeltaChip(StatType stat, float amount) => UiTheme.Chip(
        Loc.TF("create.stat_line", Signed(amount), StatNames.Label(stat)),
        amount >= 0f ? UiTheme.Good : UiTheme.Bad);

    private void SetRig(int rig)
    {
        _preview.SetRig(rig);
        _rigButton.Text = Loc.TF("create.rig", Loc.T(RigKeys[_preview.RigIndex]));
    }

    private void UpdateLegend()
    {
        bool pad = InputDevice.GamepadActive;
        _turnHint.Visible = !pad; // a pad's way to turn is in the legend, with its glyph
        var entries = new List<LegendEntry>
        {
            new("ui_accept", Loc.T("session.legend.select")),
            new(GameInput.MenuTabPrev, Loc.T("create.legend.step"), GameInput.MenuTabNext),
        };
        if (pad)
        {
            entries.Add(new LegendEntry(GameInput.LookLeft, Loc.T("create.legend.turn")));
        }

        entries.Add(new LegendEntry("ui_cancel", Loc.T("session.legend.back")));
        _legend.Set(entries);
    }

    // --- Capture hooks ----------------------------------------------------------

    /// <summary>Capture hook: scroll the step on screen to its top, or to its very end.</summary>
    public async void ScrollForCapture(bool toEnd)
    {
        // Two frames: the scroll range grows only after the rebuilt cards and summary have been laid out.
        SceneTree tree = GetTree();
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        if (IsInstanceValid(this) && IsInstanceValid(_scroll))
        {
            _scroll.ScrollVertical = toEnd ? int.MaxValue : 0;
        }
    }

    /// <summary>Capture/dev hook: choose a race by id, then appearance options by id (each as a click on its swatch
    /// would; an id the race does not offer is ignored), and show the appearance step.</summary>
    public void SelectLookForCapture(string raceId, params string[] optionIds)
    {
        if (_races.Find(r => r.Id == raceId) is { } race)
        {
            SelectRace(race);
        }

        foreach (string id in optionIds)
        {
            if (_selected != null && _selected.AppearanceOptionIds.Contains(id) && AppearanceDatabase.Get(id) is { } option)
            {
                _appearancePicks[(int)option.Slot] = id;
            }
        }

        ApplyLook();
        RefreshSummary();
        ShowStepForCapture(CreatorStep.Appearance);
    }

    /// <summary>Capture/dev hook: choose a background by id as a click on its card would, and show that step.</summary>
    public void SelectBackgroundForCapture(string id)
    {
        if (_backgrounds.Find(b => b.Id == id) is { } background)
        {
            _selectedBackground = background;
        }

        RefreshSummary();
        ShowStepForCapture(CreatorStep.Background);
    }

    /// <summary>Capture hook: show one of the four steps.</summary>
    public void ShowStepForCapture(CreatorStep step)
    {
        // No focus owner, so the frame does not depend on what the last shot left focused.
        GetViewport()?.GuiReleaseFocus();
        ShowStep(step);
    }

    /// <summary>Capture hook: frame the preview as focusing one of <paramref name="slot"/>'s swatches would.</summary>
    public void FocusSlotForCapture(AppearanceSlot slot)
    {
        ShowStepForCapture(CreatorStep.Appearance);
        FocusSlot(slot);
    }

    /// <summary>Capture hook: switch the preview's light rig, as its button would.</summary>
    public void SetRigForCapture(int rig) => SetRig(rig);

    /// <summary>Capture hook: stand the figure at a yaw, in degrees.</summary>
    public void SetYawForCapture(float degrees) => _preview.SetYaw(degrees);

    /// <summary>The step on screen. Read by the screenshot harness.</summary>
    public CreatorStep StepForCapture => _step;

    /// <summary>The preview, for the screenshot harness to read its yaw, rig and framing back.</summary>
    public CharacterPreview PreviewForCapture => _preview;

    // --- Text -------------------------------------------------------------------

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

        string name = _nameText.Trim();
        if (!string.IsNullOrEmpty(name))
        {
            profile.CharacterName = name;
        }

        _onConfirm?.Invoke(profile);
        QueueFree();
    }
}
