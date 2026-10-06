using System;
using System.Collections.Generic;
using Embervale.Combat;
using Embervale.Core;
using Embervale.Core.Services;
using Embervale.Corruption;
using Embervale.Localization;
using Embervale.Magic;
using Embervale.Player;
using Embervale.Stats;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The spell wheel: a radial pick of the eight favourites, the six schools and a school's own spells,
/// drawn over the HUD while the spell-wheel button is held. It sits in <see cref="HudLayout.Overlay"/>
/// (built by <see cref="GameHud"/>) and is driven through <see cref="SpellWheelInput"/>; the geometry
/// is <see cref="SpellWheelRules"/> and the measurements are <see cref="SpellWheelMetrics"/>.
///
/// <para>It is not a <c>UiPanel</c> and never calls <c>UiState.Open</c>: the world keeps running, the
/// mouse stays captured and the player can still move, jump, dodge and sprint. Hidden through
/// <see cref="HudElement.SpellWheel"/>, it refuses to open and the button steps to the next spell.</para>
///
/// <para>From the centre out: the dead zone (letting go there selects nothing), the favourites, the
/// schools, and past the rim the fan of the hovered school's known spells. A spell's wedge carries
/// its glyph on a disc in its school's colour, a dark pie over the disc while it cools, its price
/// when the mana is not there and a padlock when the caster's corruption is too shallow for it. The
/// prepared spell wears a ring and the one a tap goes back to wears a bracket. Under the wheel a
/// plate names what the cursor is over, and a legend line says what the buttons do.</para>
///
/// <para>With presses in place of holds the button toggles the wheel and a click selects. There is
/// no tap then, so the centre holds the previous spell and a click there goes back to it.</para>
///
/// <para>Everything is drawn in <see cref="_Draw"/> from a snapshot taken when the wheel opens and
/// every quarter second after (<see cref="Sample"/>), so the drawing never reaches into a component.
/// It repaints when the cursor moves, at that same rate for cooldowns, and each frame of its
/// opening. It processes only while open (NOW.md invariant 45: <see cref="Begin"/> is the one place
/// that switches processing on, and every close switches it off).</para>
///
/// <para>⚠️ The router does not tick under a pausing menu, so a wheel left open when one opens is
/// closed here: <see cref="_Process"/> runs while the tree is paused and cancels through the seam
/// as soon as the seam reports the wheel closed.</para>
/// </summary>
public partial class SpellWheel : Control, ISpellWheelView
{
    /// <summary>One known spell as the wheel last saw it.</summary>
    private sealed class Cell
    {
        public Cell(SpellResource spell)
        {
            Spell = spell;
        }

        public SpellResource Spell { get; }

        public float Remaining { get; set; }

        public float Fraction { get; set; }

        public float Cost { get; set; }

        public bool Affordable { get; set; } = true;

        public bool Locked { get; set; }
    }

    private const int ArcPoints = 64;

    private readonly Dictionary<string, Cell> _cells = new(StringComparer.Ordinal);
    private readonly string[] _favourites = SpellFavouritesRules.Empty();
    private readonly List<string>[] _schoolSpells = new List<string>[SpellWheelRules.Schools.Count];
    private readonly SpellWheelLayout _layout;
    private readonly Vector2[] _triangle = new Vector2[3];
    private readonly StyleBoxFlat _readoutBox = new();
    private readonly StyleBoxFlat _legendBox = new();
    private readonly HBoxContainer _legend;
    private readonly HBoxContainer _legendRow;

    private SpellcastingComponent? _caster;
    private HudLayout? _hud;
    private bool _open;
    private bool _toggled;
    private bool _capture;
    private bool _usingStick;
    private bool _legendForPad;
    private Vector2 _cursor;
    private Vector2 _drawnCursor;
    private SpellWheelPick _pick = SpellWheelPick.None;
    private int _latched = -1;
    private string _selectedId = string.Empty;
    private string _previousId = string.Empty;
    private float _mana;
    private float _bloom = 1f;
    private ulong _openedUsec;
    private ulong _sampledUsec;

    // Built in the constructor so the anchors are set before tree entry (see HudLayout), and so the
    // legend exists before anything can ask the wheel to open.
    public SpellWheel()
    {
        Name = "SpellWheel";
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;

        // The router stops under a pausing menu; this must not (see the class note).
        ProcessMode = ProcessModeEnum.Always;

        // The overlay is drawn under the scaled HUD. The wheel is the one thing in it the player is
        // reading while it is up, so it is lifted over the vitals and the hotbar.
        ZIndex = 1;

        for (int i = 0; i < _schoolSpells.Length; i++)
        {
            _schoolSpells[i] = new List<string>();
        }

        _layout = new SpellWheelLayout(_favourites, _schoolSpells);

        // The legend: a centred row on a dark ground of its own, placed under the readout.
        _legend = new HBoxContainer
        {
            Name = "Legend",
            MouseFilter = MouseFilterEnum.Ignore,
            Alignment = BoxContainer.AlignmentMode.Center,
        };
        AddChild(_legend);

        var plate = new PanelContainer
        {
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
        };
        plate.AddThemeStyleboxOverride("panel", _legendBox);
        _legend.AddChild(plate);

        _legendRow = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        _legendRow.AddThemeConstantOverride("separation", UiTheme.SpaceMd);
        plate.AddChild(_legendRow);

        Resized += OnResized;
    }

    /// <summary>True while the wheel is up and taking input.</summary>
    public bool IsOpen => _open;

    /// <summary>What the cursor is over. <see cref="SpellWheelPick.None"/> while closed.</summary>
    public SpellWheelPick Hovered => _open ? _pick : SpellWheelPick.None;

    public override void _EnterTree()
    {
        _hud = FindHudLayout();
        SpellWheelInput.Register(this);
    }

    public override void _Ready() => SetProcess(false);

    public override void _ExitTree()
    {
        SpellWheelInput.Unregister(this);
        _open = false;
        _capture = false;
        _caster = null;
        _hud = null;
        SetProcess(false);
    }

    // --- The seam -----------------------------------------------------------------------------

    public bool OpenWheel(SpellcastingComponent caster, bool toggled)
    {
        if (!IsInsideTree() || GameHud.ElementMode(HudElement.SpellWheel) == HudElementMode.Hidden)
        {
            return false;
        }

        // A HUD mode that hides the overlay hides the wheel with it; the button then steps instead.
        if (GetParent() is CanvasItem parent && !parent.IsVisibleInTree())
        {
            return false;
        }

        if (!IsInstanceValid(caster) || caster.SpellCount == 0)
        {
            return false;
        }

        Begin(caster, toggled, capture: false);
        UiFx.FadeIn(this, UiTheme.DurationFast);
        UiAudio.Play(UiCue.Open);
        return true;
    }

    public void MoveCursor(Vector2 mouseDelta)
    {
        if (!_open)
        {
            return;
        }

        _usingStick = false;
        _cursor = SpellWheelRules.StepCursor(_cursor, mouseDelta, SpellWheelRules.MouseScale);
        Repick(audible: true);
    }

    public void SetStick(Vector2 stick)
    {
        if (!_open)
        {
            return;
        }

        if (stick.Length() >= SpellWheelRules.StickDead)
        {
            _usingStick = true;
            _cursor = SpellWheelRules.FromStick(stick);
            Repick(audible: true);
        }
        else if (_usingStick && _cursor != Vector2.Zero)
        {
            // The stick came back to rest: the centre. A resting stick never moves a cursor the
            // mouse put somewhere.
            _cursor = Vector2.Zero;
            Repick(audible: true);
        }
    }

    public void CloseWheel(bool confirm)
    {
        if (!_open)
        {
            return;
        }

        SpellcastingComponent? caster = _caster;
        SpellWheelPick pick = _pick;
        bool toggled = _toggled;
        End();

        if (confirm && caster != null && IsInstanceValid(caster))
        {
            // The centre selects nothing while the button is held (letting go there is the cancel).
            // Toggled, there is no tap, so the centre is the previous spell.
            bool previous = toggled && pick.Kind == SpellWheelPickKind.None && caster.PreviousSpellId.Length > 0;
            if (pick.Selects || previous)
            {
                bool chose = previous ? caster.SelectPrevious() : caster.Select(pick.SpellId);
                UiAudio.Play(chose ? UiCue.Confirm : UiCue.Denied);
            }
            else
            {
                UiAudio.Play(UiCue.Back);
            }
        }

        UiFx.FadeOut(this, null, UiTheme.DurationFast);
    }

    // --- Capture hooks (the shot harness) -----------------------------------------------------

    /// <summary>
    /// Shows the wheel for the player with no input behind it, for a screenshot: no gate is taken, no
    /// sound plays and nothing animates. <paramref name="cursorNormalised"/> is the cursor in wheel
    /// units (the rim is 1, the fan's edge <see cref="SpellWheelRules.FanEdge"/>; +X right, +Y
    /// down). False when there is no player caster or it knows no spell.
    /// </summary>
    public bool OpenForCapture(Vector2 cursorNormalised, bool toggled = false)
    {
        if (!IsInsideTree() ||
            ServiceLocator.Instance is not { } locator ||
            !locator.TryGet(out PlayerCharacter player) ||
            !player.TryGetComponent(out SpellcastingComponent caster) ||
            caster.SpellCount == 0)
        {
            return false;
        }

        Begin(caster, toggled, capture: true);
        _openedUsec = 0;
        _bloom = 1f;
        UiFx.FadeIn(this, 0f);
        HoverForCapture(cursorNormalised);
        return true;
    }

    /// <summary>Puts the cursor at <paramref name="cursorNormalised"/> (wheel units). A point past
    /// the rim opens the fan of the school at that angle first, as a real cursor crossing the ring
    /// would have.</summary>
    public void HoverForCapture(Vector2 cursorNormalised)
    {
        if (!_open)
        {
            return;
        }

        _usingStick = false;
        _cursor = SpellWheelRules.StepCursor(Vector2.Zero, cursorNormalised, 1f);
        _latched = -1;
        if (_cursor.Length() > SpellWheelRules.OuterEdge)
        {
            Vector2 inRing = _cursor.Normalized() * SpellWheelMetrics.SchoolRadius;
            _latched = SpellWheelRules.Latch(SpellWheelRules.Pick(inRing, _layout, -1));
        }

        Sample();
        Repick(audible: false);
        QueueRedraw();
    }

    /// <summary>Hides a wheel opened by <see cref="OpenForCapture"/> at once, selecting nothing.</summary>
    public void CloseForCapture()
    {
        if (_open && _capture)
        {
            End();
        }

        UiFx.FadeOut(this, null, 0f);
    }

    // --- Open and close -----------------------------------------------------------------------

    private void Begin(SpellcastingComponent caster, bool toggled, bool capture)
    {
        _caster = caster;
        _toggled = toggled;
        _capture = capture;
        _usingStick = false;
        _cursor = Vector2.Zero;
        _drawnCursor = Vector2.Zero;
        _pick = SpellWheelPick.None;
        _latched = -1;
        _open = true;
        _openedUsec = Time.GetTicksUsec();
        _bloom = SpellWheelMetrics.Bloom(UiMotion.Progress(0f, UiTheme.Duration(UiTheme.DurationBase)));

        ReadSpells(caster);
        Sample();
        BuildLegend();
        PlaceLegend();
        SetProcess(true);
        QueueRedraw();
    }

    /// <summary>Stops the wheel taking input and ticking. What it last showed stays drawable, so a
    /// fade out has something to fade.</summary>
    private void End()
    {
        _open = false;
        _capture = false;
        _caster = null;
        SetProcess(false);
    }

    /// <summary>Rebuilds what the wheel holds from the caster: its known spells by school, and the
    /// favourites that name one of them.</summary>
    private void ReadSpells(SpellcastingComponent caster)
    {
        _cells.Clear();
        foreach (List<string> school in _schoolSpells)
        {
            school.Clear();
        }

        foreach (SpellResource spell in caster.Spells)
        {
            if (spell == null || _cells.ContainsKey(spell.Id))
            {
                continue;
            }

            _cells[spell.Id] = new Cell(spell);
            int school = SchoolIndex(spell.School);
            if (school >= 0)
            {
                _schoolSpells[school].Add(spell.Id);
            }
        }

        IReadOnlyList<string> pinned = caster.Favourites;
        for (int i = 0; i < _favourites.Length; i++)
        {
            string id = i < pinned.Count ? pinned[i] ?? string.Empty : string.Empty;
            _favourites[i] = _cells.ContainsKey(id) ? id : SpellFavouritesRules.None;
        }
    }

    /// <summary>Reads what changes while the wheel is up: cooldowns, prices, the mana to pay them,
    /// the corruption locks and which spell is prepared.</summary>
    private void Sample()
    {
        _sampledUsec = Time.GetTicksUsec();
        if (_caster is not { } caster || !IsInstanceValid(caster))
        {
            return;
        }

        _mana = caster.Entity?.GetComponent<StatsComponent>()?.GetCurrent(StatType.Mana) ?? 0f;
        SchoolMasteryComponent? mastery = caster.Entity?.GetComponent<SchoolMasteryComponent>();
        foreach (Cell cell in _cells.Values)
        {
            SpellResource spell = cell.Spell;

            // The cooldown the cast actually set, which mastery shortens (as the HUD's recovery bar).
            float total = spell.Cooldown * (mastery?.CooldownMultiplier(spell.School) ?? 1f);
            cell.Remaining = caster.CooldownOf(spell);
            cell.Fraction = SpellWheelMetrics.CooldownFraction(cell.Remaining, total);
            cell.Cost = caster.EffectiveManaCost(spell);
            cell.Affordable = _mana >= cell.Cost;
            cell.Locked = !caster.MeetsCorruption(spell);
        }

        _selectedId = caster.Selected?.Id ?? string.Empty;
        _previousId = caster.PreviousSpellId;
    }

    public override void _Process(double delta)
    {
        if (!_open)
        {
            SetProcess(false);
            return;
        }

        // The seam says closed (a menu took the gate, the session is going), or the caster is gone:
        // close through the seam so the router and the gate agree, and directly if it had already
        // forgotten this wheel. A HUD mode that hides the overlay while the wheel is up closes it
        // too: a wheel nobody can see must not keep the look and the attack gated.
        bool lost = _caster == null || !IsInstanceValid(_caster);
        if (lost || (!_capture && (!SpellWheelInput.IsOpen || !IsVisibleInTree())))
        {
            SpellWheelInput.Cancel();
            if (_open)
            {
                CloseWheel(confirm: false);
            }

            return;
        }

        // Wall clock: hit-stop slows the frame delta and must not slow the wheel.
        ulong now = Time.GetTicksUsec();
        float bloom = SpellWheelMetrics.Bloom(UiMotion.Progress(
            (now - _openedUsec) / 1_000_000f, UiTheme.Duration(UiTheme.DurationBase)));
        if (bloom != _bloom)
        {
            _bloom = bloom;
            QueueRedraw();
        }

        if ((now - _sampledUsec) / 1_000_000f >= SpellWheelMetrics.RepaintSeconds)
        {
            Sample();
            StyleLegendGround();
            QueueRedraw();
        }

        if (_legendForPad != InputDevice.GamepadActive)
        {
            BuildLegend();
        }
    }

    private void Repick(bool audible)
    {
        SpellWheelPick pick = SpellWheelRules.Pick(_cursor, _layout, _latched);
        int latched = SpellWheelRules.Latch(pick);

        // Every move repaints (the pointer tick follows the cursor inside a wedge too), but a stick
        // held still calls this every physics tick and must not redraw the wheel each time.
        bool changed = pick != _pick || latched != _latched || _cursor != _drawnCursor;
        _latched = latched;
        if (pick != _pick)
        {
            _pick = pick;
            if (audible)
            {
                UiAudio.Play(UiCue.Focus);
            }
        }

        if (changed)
        {
            _drawnCursor = _cursor;
            QueueRedraw();
        }
    }

    // --- Legend -------------------------------------------------------------------------------

    private void BuildLegend()
    {
        _legendForPad = InputDevice.GamepadActive;
        StyleLegendGround();
        _legendBox.SetCornerRadiusAll(UiTheme.RadiusSm);
        _legendBox.SetContentMarginAll(UiTheme.Space2xs);
        _legendBox.ContentMarginLeft = UiTheme.SpaceSm;
        _legendBox.ContentMarginRight = UiTheme.SpaceSm;

        UiTheme.ClearChildren(_legendRow);
        if (_toggled)
        {
            AddLegend(UiGlyph.For(GameInput.Attack), Loc.T("wheel.legend.select"));
        }
        else
        {
            AddLegend(UiGlyph.For(GameInput.CycleSpell), Loc.T("wheel.legend.release"));
        }

        AddLegend(UiGlyph.For(GameInput.Block), Loc.T("wheel.legend.cancel"));
        AddLegend(CentreMark(), Loc.T(_toggled ? "wheel.legend.centre_previous" : "wheel.legend.centre_cancel"));
    }

    /// <summary>The legend's ground. Re-read while the wheel is up, so a high-contrast change in the
    /// middle of a toggled wheel takes without waiting for the next open.</summary>
    private void StyleLegendGround()
    {
        Color ground = UiTheme.ScrimBg with { A = UiTheme.HighContrast ? 1f : 0.80f };
        if (_legendBox.BgColor != ground)
        {
            _legendBox.BgColor = ground;
        }
    }

    private void AddLegend(Control glyph, string verb)
    {
        var pair = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        pair.AddThemeConstantOverride("separation", UiTheme.SpaceXs);
        glyph.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        pair.AddChild(glyph);

        Label label = UiTheme.Caption(verb, UiTheme.Text);
        label.MouseFilter = MouseFilterEnum.Ignore;
        label.VerticalAlignment = VerticalAlignment.Center;
        pair.AddChild(label);
        _legendRow.AddChild(pair);
    }

    /// <summary>The picture of the wheel's centre for the legend: a ring with its middle marked.</summary>
    private static Control CentreMark()
    {
        var mark = new Control
        {
            CustomMinimumSize = new Vector2(18f, 18f),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        mark.Draw += () =>
        {
            Vector2 middle = mark.Size * 0.5f;
            mark.DrawArc(middle, 7f, 0f, Mathf.Tau, 20, UiTheme.Dim, 1.5f, true);
            mark.DrawCircle(middle, 2.5f, UiTheme.Text);
        };
        return mark;
    }

    private void OnResized()
    {
        PlaceLegend();
        QueueRedraw();
    }

    private void PlaceLegend()
    {
        Measure(out Vector2 centre, out float rim, out Vector2 origin, out Vector2 view);
        _legend.Position = new Vector2(origin.X + SpellWheelMetrics.Margin, SpellWheelMetrics.LegendTop(centre, rim));
        _legend.Size = new Vector2(
            Mathf.Max(0f, view.X - (SpellWheelMetrics.Margin * 2f)), SpellWheelMetrics.LegendHeight);
    }

    // --- Measuring ----------------------------------------------------------------------------

    private HudLayout? FindHudLayout()
    {
        for (Node? node = GetParent(); node != null; node = node.GetParent())
        {
            if (node is HudLayout layout)
            {
                return layout;
            }
        }

        return null;
    }

    /// <summary>Where the wheel sits in this control: the view is the control less the HUD's safe
    /// zone, and the rim follows the HUD scale as far as the view has room.</summary>
    private void Measure(out Vector2 centre, out float rim, out Vector2 origin, out Vector2 view)
    {
        float scale = 1f;
        float safe = 0f;
        if (_hud != null && IsInstanceValid(_hud))
        {
            scale = _hud.HudScale;
            safe = _hud.SafeZone;
        }

        origin = Size * safe;
        view = Size * (1f - (2f * safe));
        rim = SpellWheelMetrics.Radius(view, scale);
        centre = origin + SpellWheelMetrics.Centre(view, rim);
    }

    private static int SchoolIndex(DamageType school)
    {
        for (int i = 0; i < SpellWheelRules.Schools.Count; i++)
        {
            if (SpellWheelRules.Schools[i] == school)
            {
                return i;
            }
        }

        return -1;
    }

    private static string SchoolNameKey(DamageType school) => school switch
    {
        DamageType.Fire => "school.fire",
        DamageType.Frost => "school.frost",
        DamageType.Lightning => "school.lightning",
        DamageType.Arcane => "school.arcane",
        DamageType.Nature => "school.nature",
        _ => "school.necrotic",
    };

    // --- Drawing ------------------------------------------------------------------------------

    public override void _Draw()
    {
        if (_cells.Count == 0)
        {
            return; // never opened
        }

        Measure(out Vector2 centre, out float rim, out _, out Vector2 view);
        float r = rim * _bloom;
        float line = UiTheme.WheelLine;
        float lit = UiTheme.WheelLitLine;

        float dead = r * SpellWheelRules.DeadZone;
        float inner = r * SpellWheelRules.InnerEdge;
        float outer = r * SpellWheelRules.OuterEdge;
        float fanFrom = r * (SpellWheelRules.OuterEdge + SpellWheelMetrics.FanGap);
        float fanTo = r * SpellWheelRules.FanEdge;

        int favourites = SpellWheelRules.FavouriteWedges;
        int schools = SpellWheelRules.Schools.Count;
        float favouriteStep = 360f / favourites;
        float schoolStep = SpellWheelRules.SchoolWedgeDegrees;

        int hoverFavourite = _pick.Kind == SpellWheelPickKind.Favourite ? _pick.Index : -1;
        int hoverSchool = _pick.Kind == SpellWheelPickKind.School ? _pick.Index : -1;
        int hoverSpell = _pick.Kind == SpellWheelPickKind.Spell ? _pick.Index : -1;
        int fanSchool = _latched >= 0 && _latched < schools && _schoolSpells[_latched].Count > 0 ? _latched : -1;
        int fanCount = fanSchool >= 0 ? _schoolSpells[fanSchool].Count : 0;
        float fanSpan = SpellWheelRules.FanSpanDegrees(fanCount);
        float fanStart = (fanSchool * schoolStep) - (fanSpan * 0.5f);
        float fanStep = fanCount > 0 ? fanSpan / fanCount : 0f;

        // 1. Grounds. A soft disc first, so the wheel has something darker than the scene to sit on.
        DrawCircle(centre, (outer + fanFrom) * 0.5f, UiTheme.WheelBackdrop, true, -1f, true);
        for (int i = 0; i < favourites; i++)
        {
            float mid = i * favouriteStep;
            DrawColoredPolygon(
                SpellWheelMetrics.Sector(centre, dead, inner, mid - (favouriteStep * 0.5f), mid + (favouriteStep * 0.5f)),
                i == hoverFavourite ? UiTheme.WheelWellHover : UiTheme.WheelWell);
        }

        for (int i = 0; i < schools; i++)
        {
            float mid = i * schoolStep;
            DrawColoredPolygon(
                SpellWheelMetrics.Sector(centre, inner, outer, mid - (schoolStep * 0.5f), mid + (schoolStep * 0.5f)),
                UiTheme.WheelSchoolGround(SpellWheelRules.Schools[i], i == hoverSchool || i == fanSchool));
        }

        for (int i = 0; i < fanCount; i++)
        {
            float from = fanStart + (fanStep * i);
            DrawColoredPolygon(
                SpellWheelMetrics.Sector(centre, fanFrom, fanTo, from, from + fanStep),
                UiTheme.WheelFanGround(SpellWheelRules.Schools[fanSchool], i == hoverSpell));
        }

        // 2. Keylines: the spokes between wedges, then the rings over their ends.
        for (int i = 0; i < favourites; i++)
        {
            Vector2 spoke = SpellWheelMetrics.Direction((i + 0.5f) * favouriteStep);
            DrawLine(centre + (spoke * dead), centre + (spoke * inner), UiTheme.Keyline, line, true);
        }

        for (int i = 0; i < schools; i++)
        {
            Vector2 spoke = SpellWheelMetrics.Direction((i + 0.5f) * schoolStep);
            DrawLine(centre + (spoke * inner), centre + (spoke * outer), UiTheme.Keyline, line, true);
        }

        DrawArc(centre, inner, 0f, Mathf.Tau, ArcPoints, UiTheme.Keyline, line, true);
        DrawArc(centre, outer, 0f, Mathf.Tau, ArcPoints, UiTheme.Keyline, line, true);
        DrawArc(centre, outer - line, 0f, Mathf.Tau, ArcPoints, UiTheme.HudInnerEdge, 1f, true);

        if (fanCount > 0)
        {
            float from = SpellWheelMetrics.ArcAngle(fanStart);
            float to = SpellWheelMetrics.ArcAngle(fanStart + fanSpan);
            int points = Mathf.Max(8, Mathf.CeilToInt(fanSpan / 4f));
            for (int i = 0; i <= fanCount; i++)
            {
                Vector2 spoke = SpellWheelMetrics.Direction(fanStart + (fanStep * i));
                DrawLine(centre + (spoke * fanFrom), centre + (spoke * fanTo), UiTheme.Keyline, line, true);
            }

            DrawArc(centre, fanFrom, from, to, points, UiTheme.Keyline, line, true);
            DrawArc(centre, fanTo, from, to, points, UiTheme.Keyline, line, true);
            DrawArc(centre, fanTo - line, from, to, points, UiTheme.HudInnerEdge, 1f, true);
        }

        // 3. The one lit edge: on the wedge under the cursor, and in the school's own colour on the
        // school whose fan is open while the cursor is out in it.
        if (hoverFavourite >= 0)
        {
            DrawLitEdge(centre, inner - (lit * 0.5f) - 1f, hoverFavourite * favouriteStep, favouriteStep, UiTheme.WheelLit, lit);
        }

        if (hoverSchool >= 0)
        {
            DrawLitEdge(centre, outer - (lit * 0.5f) - 1f, hoverSchool * schoolStep, schoolStep, UiTheme.WheelLit, lit);
        }
        else if (fanSchool >= 0)
        {
            DrawLitEdge(
                centre, outer - (lit * 0.5f) - 1f, fanSchool * schoolStep, schoolStep,
                UiTheme.SchoolColor(SpellWheelRules.Schools[fanSchool]), lit);
        }

        if (hoverSpell >= 0 && hoverSpell < fanCount)
        {
            DrawLitEdge(
                centre, fanTo - (lit * 0.5f) - 1f, fanStart + (fanStep * (hoverSpell + 0.5f)), fanStep,
                UiTheme.WheelLit, lit);
        }

        // 4. What the wedges hold.
        Font numerals = UiTheme.WheelTextFont ?? GetThemeDefaultFont();
        float favouriteSide = SpellWheelMetrics.GlyphSide(
            r * SpellWheelMetrics.FavouriteRadius, favouriteStep, inner - dead);
        for (int i = 0; i < favourites; i++)
        {
            Vector2 at = centre + (SpellWheelMetrics.Direction(i * favouriteStep) * r * SpellWheelMetrics.FavouriteRadius);
            if (_cells.TryGetValue(_favourites[i], out Cell? cell))
            {
                DrawSpell(cell, at, favouriteSide, numerals, i * favouriteStep);
            }
            else
            {
                DrawSocket(at, favouriteSide);
            }
        }

        float emblemSide = SpellWheelMetrics.GlyphSide(r * SpellWheelMetrics.SchoolRadius, schoolStep, outer - inner);
        for (int i = 0; i < schools; i++)
        {
            DrawSchool(centre, r, i, emblemSide, i == hoverSchool || i == fanSchool);
        }

        if (fanCount > 0)
        {
            float fanSide = SpellWheelMetrics.GlyphSide(r * SpellWheelMetrics.FanRadius, fanStep, fanTo - fanFrom);
            for (int i = 0; i < fanCount; i++)
            {
                if (_cells.TryGetValue(_schoolSpells[fanSchool][i], out Cell? cell))
                {
                    float angle = SpellWheelMetrics.FanCentre(fanSchool, i, fanCount);
                    DrawSpell(
                        cell, centre + (SpellWheelMetrics.Direction(angle) * r * SpellWheelMetrics.FanRadius), fanSide, numerals,
                        angle);
                }
            }
        }

        // 5. The centre, the pointer, and the plate that says what the cursor is over.
        DrawCentre(centre, dead, line, lit);
        if (_open)
        {
            DrawPointer(centre + (_cursor * r));
        }

        DrawReadout(centre, rim, view);
    }

    /// <summary>An arc along a wedge's outer edge, a little short of its spokes.</summary>
    private void DrawLitEdge(Vector2 centre, float radius, float midDegrees, float wedgeDegrees, Color color, float width)
    {
        float trim = Mathf.Min(2.5f, wedgeDegrees * 0.1f);
        float half = (wedgeDegrees * 0.5f) - trim;
        DrawArc(
            centre, radius, SpellWheelMetrics.ArcAngle(midDegrees - half), SpellWheelMetrics.ArcAngle(midDegrees + half),
            Mathf.Max(6, Mathf.CeilToInt(wedgeDegrees / 4f)), color, width, true);
    }

    /// <summary>An empty favourite slot: a faint ring with its middle marked, so eight unpinned
    /// slots are not the heaviest thing on the wheel.</summary>
    private void DrawSocket(Vector2 at, float side)
    {
        Color socket = UiTheme.WheelSocket;
        DrawArc(at, side * 0.40f, 0f, Mathf.Tau, 24, socket, 1.5f, true);
        DrawCircle(at, 1.5f, socket);
    }

    /// <summary>A school's wedge: its emblem, and a pip on the rim for each spell known in it, which
    /// is also the hint that there is a fan past the rim.</summary>
    private void DrawSchool(Vector2 centre, float r, int index, float side, bool lit)
    {
        DamageType school = SpellWheelRules.Schools[index];
        int known = _schoolSpells[index].Count;
        float mid = index * SpellWheelRules.SchoolWedgeDegrees;
        Vector2 at = centre + (SpellWheelMetrics.Direction(mid) * r * SpellWheelMetrics.SchoolRadius);

        Color ink = UiTheme.SchoolColor(school).Lightened(lit ? 0.35f : 0.12f);
        if (known == 0)
        {
            // Nothing to pick here. Under high contrast it stays readable and the missing pips say it.
            ink = ink with { A = UiTheme.HighContrast ? 0.75f : UiTheme.WheelDimmed };
        }

        var rect = new Rect2(at - new Vector2(side * 0.5f, side * 0.5f), new Vector2(side, side));
        SpellGlyphs.DrawStrokes(
            this, SpellGlyphs.Emblem(school), rect, Colors.Transparent, UiTheme.Keyline with { A = ink.A }, 2.1f);
        SpellGlyphs.DrawStrokes(this, SpellGlyphs.Emblem(school), rect, Colors.Transparent, ink);

        float pipRadius = Mathf.Max(1.6f, r * 0.014f);
        float pipAt = r * (SpellWheelRules.OuterEdge - 0.07f);
        for (int i = 0; i < known; i++)
        {
            float angle = mid + ((i - ((known - 1) * 0.5f)) * 5f);
            Vector2 pip = centre + (SpellWheelMetrics.Direction(angle) * pipAt);
            DrawCircle(pip, pipRadius + 1f, UiTheme.Keyline);
            DrawCircle(pip, pipRadius, ink);
        }
    }

    /// <summary>
    /// A spell: its glyph on a disc in its school's colour, with what stands in the way of casting it
    /// drawn as a shape (a pie while it cools, a padlock when locked, its price when the mana is
    /// short) and which spell it is to the caster (a ring on the prepared one, a bracket on the
    /// previous one). <paramref name="radialDegrees"/> is the wedge's own direction: the bracket sits
    /// along it, where a wedge has room, and not across it, where the neighbours are.
    /// </summary>
    private void DrawSpell(Cell cell, Vector2 at, float side, Font numerals, float radialDegrees)
    {
        SpellResource spell = cell.Spell;
        float half = side * 0.5f;
        bool dimmed = cell.Locked || !cell.Affordable;

        // A spell that cannot be cast now is drawn unlit: its glyph in the school's colour on a dark
        // disc, so it is still told apart from its neighbours.
        Color school = UiTheme.SchoolColor(spell.School);
        Color disc = dimmed ? UiTheme.WheelUnlitDisc(spell.School) : school;
        Color ink = dimmed ? UiTheme.WheelUnlitInk(spell.School) : UiTheme.WheelGlyphInk;

        DrawCircle(at, half + 1.5f, UiTheme.Keyline, true, -1f, true);
        SpellGlyphs.Draw(this, spell.Id, new Rect2(at - new Vector2(half, half), new Vector2(side, side)), disc, ink);

        if (cell.Fraction >= 0.01f)
        {
            DrawWipe(at, half, cell.Fraction);
        }

        if (spell.Id == _selectedId)
        {
            DrawArc(at, half + 2.5f, 0f, Mathf.Tau, 32, UiTheme.Keyline, UiTheme.WheelLitLine + 2f, true);
            DrawArc(at, half + 2.5f, 0f, Mathf.Tau, 32, UiTheme.WheelLit, UiTheme.WheelLitLine, true);
        }
        else if (spell.Id == _previousId)
        {
            DrawBracket(at, half + 3f, radialDegrees);
            DrawBracket(at, half + 3f, radialDegrees + 180f);
        }

        int size = Mathf.Clamp(Mathf.RoundToInt(side * 0.36f), 12, 18); // UI_STYLE's 12 px floor
        if (cell.Locked)
        {
            DrawPadlock(at, side * 0.42f, UiTheme.CorruptionText);
        }
        else
        {
            int numeral = SpellWheelMetrics.Numeral(cell.Remaining);
            if (numeral > 0)
            {
                DrawInked(numerals, numeral.ToString(), new Vector2(at.X, at.Y + (size * 0.36f)), size, UiTheme.Text);
            }

            if (!cell.Affordable)
            {
                // Across the foot of the disc, so it never covers a cooldown numeral.
                DrawInked(numerals, cell.Cost.ToString("0"), new Vector2(at.X, at.Y + half + (size * 0.30f)), size, UiTheme.Bad);
            }
        }
    }

    /// <summary>The cooldown still to run, as on the hotbar: a dark pie over the disc, unwinding
    /// clockwise from twelve o'clock.</summary>
    private void DrawWipe(Vector2 at, float radius, float fraction)
    {
        int steps = Mathf.Max(3, Mathf.CeilToInt(32f * fraction));
        float from = (-Mathf.Pi / 2f) + (Mathf.Tau * (1f - fraction));
        float span = Mathf.Tau * fraction;
        Color shade = UiTheme.HudWipe;

        // One reused triangle: the engine copies the points on each call.
        _triangle[0] = at;
        _triangle[1] = at + (new Vector2(Mathf.Cos(from), Mathf.Sin(from)) * radius);
        for (int s = 1; s <= steps; s++)
        {
            float angle = from + (span * s / steps);
            _triangle[2] = at + (new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
            DrawColoredPolygon(_triangle, shade);
            _triangle[1] = _triangle[2];
        }
    }

    /// <summary>One side of the previous spell's bracket: a keylined arc centred on
    /// <paramref name="midDegrees"/>.</summary>
    private void DrawBracket(Vector2 at, float radius, float midDegrees)
    {
        float from = SpellWheelMetrics.ArcAngle(midDegrees - 38f);
        float to = SpellWheelMetrics.ArcAngle(midDegrees + 38f);
        DrawArc(at, radius, from, to, 12, UiTheme.Keyline, UiTheme.WheelLitLine + 2f, true);
        DrawArc(at, radius, from, to, 12, UiTheme.WheelPrevious, UiTheme.WheelLitLine - 1f, true);
    }

    /// <summary>A keylined padlock <paramref name="size"/> px wide, centred on <paramref name="at"/>.</summary>
    private void DrawPadlock(Vector2 at, float size, Color color)
    {
        float shackle = size * 0.30f;
        float stroke = Mathf.Max(2f, size * 0.16f);
        var body = new Rect2(at.X - (size * 0.5f), at.Y - (size * 0.08f), size, size * 0.58f);
        var top = new Vector2(at.X, body.Position.Y);

        // The top half of a circle: the engine's angles run clockwise from +X on a screen.
        DrawArc(top, shackle, Mathf.Pi, Mathf.Tau, 12, UiTheme.Keyline, stroke + 3f, true);
        DrawRect(body.Grow(1.5f), UiTheme.Keyline);
        DrawArc(top, shackle, Mathf.Pi, Mathf.Tau, 12, color, stroke, true);
        DrawRect(body, color);
        DrawCircle(body.Position + (body.Size * 0.5f), Mathf.Max(1.2f, size * 0.09f), UiTheme.Keyline);
    }

    /// <summary>
    /// The dead zone. Held, letting go here selects nothing, so it carries a cancel mark. Toggled,
    /// it is the previous spell (there is no tap to reach it by), and it lights under the cursor.
    /// </summary>
    private void DrawCentre(Vector2 centre, float dead, float line, float lit)
    {
        bool hovered = _pick.Kind == SpellWheelPickKind.None;
        Cell? previous = null;
        bool offersPrevious = _toggled && _cells.TryGetValue(_previousId, out previous);

        DrawCircle(centre, dead, offersPrevious && hovered ? UiTheme.WheelWellHover : UiTheme.WheelWell, true, -1f, true);
        DrawArc(centre, dead, 0f, Mathf.Tau, ArcPoints / 2, UiTheme.Keyline, line, true);

        if (offersPrevious && previous != null)
        {
            float side = dead * 1.36f;
            float half = side * 0.5f;
            Color school = UiTheme.SchoolColor(previous.Spell.School);
            SpellGlyphs.Draw(
                this, previous.Spell.Id, new Rect2(centre - new Vector2(half, half), new Vector2(side, side)),
                school, UiTheme.WheelGlyphInk);
            if (previous.Fraction >= 0.01f)
            {
                DrawWipe(centre, half, previous.Fraction);
            }

            if (hovered)
            {
                DrawArc(centre, dead - (lit * 0.5f) - 1f, 0f, Mathf.Tau, ArcPoints / 2, UiTheme.WheelLit, lit, true);
            }

            return;
        }

        float arm = dead * 0.30f;
        Color mark = hovered ? UiTheme.Text : UiTheme.Dim;
        DrawLine(centre + new Vector2(-arm, -arm), centre + new Vector2(arm, arm), mark, line, true);
        DrawLine(centre + new Vector2(-arm, arm), centre + new Vector2(arm, -arm), mark, line, true);
    }

    /// <summary>The pointer tick: where the virtual cursor is. The mouse is captured, so this is the
    /// only cursor there is.</summary>
    private void DrawPointer(Vector2 at)
    {
        DrawArc(at, 5.5f, 0f, Mathf.Tau, 20, UiTheme.Keyline, 5f, true);
        DrawArc(at, 5.5f, 0f, Mathf.Tau, 20, UiTheme.WheelPointer, 2f, true);
        DrawCircle(at, 1.6f, UiTheme.WheelPointer);
    }

    // --- Readout ------------------------------------------------------------------------------

    /// <summary>The plate under the wheel: the name of what the cursor is over, then what it costs
    /// and whether it can be cast, each in the colour the HUD's spell row gives it.</summary>
    private void DrawReadout(Vector2 centre, float rim, Vector2 view)
    {
        string title;
        Color titleColor = UiTheme.Text;
        Color edge = UiTheme.RuleLit;
        string cost = string.Empty;
        Color costColor = UiTheme.Mana;
        string state = string.Empty;
        Color stateColor = UiTheme.Dim;
        string tag = string.Empty;

        Cell? cell = null;
        bool previousAtCentre = false;
        if (_pick.Kind is SpellWheelPickKind.Favourite or SpellWheelPickKind.Spell)
        {
            _cells.TryGetValue(_pick.SpellId, out cell);
        }
        else if (_pick.Kind == SpellWheelPickKind.None && _toggled)
        {
            previousAtCentre = _cells.TryGetValue(_previousId, out cell);
        }

        if (cell != null)
        {
            SpellResource spell = cell.Spell;
            title = spell.DisplayName;
            titleColor = UiTheme.SchoolColor(spell.School);
            edge = titleColor;
            cost = spell.HealthCost > 0f
                ? $"{Loc.TF("spellbook.mana", cell.Cost.ToString("0"))} + {Loc.TF("magic.book.hud_health_cost", spell.HealthCost.ToString("0"))}"
                : Loc.TF("spellbook.mana", cell.Cost.ToString("0"));
            costColor = cell.Affordable ? UiTheme.Mana : UiTheme.Bad;

            switch (SpellWheelMetrics.State(true, cell.Locked, cell.Remaining, _mana, cell.Cost))
            {
                case SpellWheelCellState.Locked:
                    state = Loc.TF("wheel.state.locked", CorruptionTiers.DisplayName(spell.MinCorruptionTier));
                    stateColor = UiTheme.CorruptionText;
                    break;
                case SpellWheelCellState.Cooling:
                    state = Loc.TF("spellbook.cooldown", cell.Remaining.ToString("0.0"));
                    stateColor = UiTheme.Dim;
                    break;
                case SpellWheelCellState.Unaffordable:
                    state = Loc.TF("magic.book.hud_mana_short", Mathf.Ceil(cell.Cost - _mana).ToString("0"));
                    stateColor = UiTheme.Bad;
                    break;
                default:
                    state = Loc.T("hud.ready");
                    stateColor = UiTheme.Accent;
                    break;
            }

            tag = spell.Id == _selectedId ? Loc.T("spellbook.prepared")
                : previousAtCentre || spell.Id == _previousId ? Loc.T("wheel.previous")
                : string.Empty;
        }
        else if (_pick.Kind == SpellWheelPickKind.School && _pick.Index >= 0 && _pick.Index < _schoolSpells.Length)
        {
            DamageType school = SpellWheelRules.Schools[_pick.Index];
            int known = _schoolSpells[_pick.Index].Count;
            title = Loc.T(SchoolNameKey(school));
            titleColor = UiTheme.SchoolColor(school);
            edge = titleColor;
            state = known > 0 ? Loc.TF("wheel.school.known", known) : Loc.T("wheel.school.none");
        }
        else if (_pick.Kind == SpellWheelPickKind.Favourite)
        {
            title = Loc.T("wheel.slot.empty");
            titleColor = UiTheme.Dim;
            state = Loc.T("wheel.slot.hint");
        }
        else
        {
            title = Loc.T(_toggled ? "wheel.centre.no_previous" : "wheel.centre.cancel");
            titleColor = UiTheme.Dim;
            if (_cells.TryGetValue(_selectedId, out Cell? kept))
            {
                state = Loc.TF("wheel.centre.keeps", kept.Spell.DisplayName);
            }
        }

        float width = SpellWheelMetrics.ReadoutWidth(view, rim);
        var plate = new Rect2(
            centre.X - (width * 0.5f), SpellWheelMetrics.ReadoutTop(centre, rim), width, SpellWheelMetrics.ReadoutHeight);
        UiTheme.StyleWheelReadout(_readoutBox, edge);
        DrawStyleBox(_readoutBox, plate);

        // The plate does not grow with the text-scale setting, so its two lines stop where it ends.
        Font nameFont = UiTheme.WheelNameFont ?? GetThemeDefaultFont();
        Font textFont = UiTheme.WheelTextFont ?? GetThemeDefaultFont();
        int nameSize = Mathf.Min(UiTheme.FontSize(UiTheme.HeaderFontSize), 20);
        int textSize = Mathf.Min(UiTheme.FontSize(UiTheme.CaptionFontSize) + 1, 15);
        float pad = UiTheme.SpaceSm;
        float inside = width - (pad * 2f);

        float nameBaseline = plate.Position.Y + 5f + nameFont.GetAscent(nameSize);
        DrawString(
            nameFont, new Vector2(plate.Position.X + pad, nameBaseline), title, HorizontalAlignment.Center, inside,
            nameSize, titleColor);

        // The second line is up to three runs in their own colours, centred as one.
        const float RunGap = 12f;
        float costWidth = RunWidth(textFont, cost, textSize);
        float stateWidth = RunWidth(textFont, state, textSize);
        float tagWidth = RunWidth(textFont, tag, textSize);
        int runs = (cost.Length > 0 ? 1 : 0) + (state.Length > 0 ? 1 : 0) + (tag.Length > 0 ? 1 : 0);
        if (runs == 0)
        {
            return;
        }

        float total = costWidth + stateWidth + tagWidth + (RunGap * (runs - 1));
        float x = centre.X - (Mathf.Min(total, inside) * 0.5f);
        float baseline = plate.End.Y - 7f - textFont.GetDescent(textSize);
        x = DrawRun(textFont, cost, x, baseline, textSize, costColor, costWidth, RunGap);
        x = DrawRun(textFont, state, x, baseline, textSize, stateColor, stateWidth, RunGap);
        DrawRun(textFont, tag, x, baseline, textSize, UiTheme.Text, tagWidth, RunGap);
    }

    private static float RunWidth(Font font, string text, int size) =>
        text.Length > 0 ? font.GetStringSize(text, HorizontalAlignment.Left, -1f, size).X : 0f;

    private float DrawRun(Font font, string text, float x, float baseline, int size, Color color, float width, float gap)
    {
        if (text.Length == 0)
        {
            return x;
        }

        DrawString(font, new Vector2(x, baseline), text, HorizontalAlignment.Left, -1f, size, color);
        return x + width + gap;
    }

    /// <summary>Inked text centred on <paramref name="at"/> (its baseline): the HUD's keyline, for a
    /// number that sits on a disc rather than a plate.</summary>
    private void DrawInked(Font font, string text, Vector2 at, int size, Color color)
    {
        float width = font.GetStringSize(text, HorizontalAlignment.Left, -1f, size).X;
        var position = new Vector2(at.X - (width * 0.5f), at.Y);
        DrawStringOutline(
            font, position, text, HorizontalAlignment.Left, -1f, size, UiTheme.HudInkSize + 1, UiTheme.Keyline with { A = 1f });
        DrawString(font, position, text, HorizontalAlignment.Left, -1f, size, color);
    }
}
