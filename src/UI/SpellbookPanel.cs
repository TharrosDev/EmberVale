using System.Collections.Generic;
using Embervale.Combat;
using Embervale.Core;
using Embervale.Core.Events;
using Embervale.Corruption;
using Embervale.Localization;
using Embervale.Magic;
using Embervale.Progression;
using Embervale.Stats;
using Embervale.World;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The spellbook (Phase 37.5D) — magic's own screen, lifted out of the character sheet's fourth
/// tab where it had been a flat list of text rows sharing the gear screen's chrome.
///
/// **This is the one surface in the game that runs cold.** Everything else is ash, parchment and
/// ember gold; the spellbook is ink-violet vellum behind tarnished silver, lit by glyph-blue. That
/// contrast is the whole point — arcane scholarship should not look like a bag of swords, and the
/// player should know which screen they are on before they read a word of it.
///
/// It is also where the ornament budget (see <see cref="UiOrnament"/>) is spent: a rotating rune
/// diagram behind the school ring, a drifting sigil field across the ground, and a shimmer on the
/// school heading. Nothing else in the UI gets all three, and nothing else should.
/// </summary>
public partial class SpellbookPanel : UiPanel
{
    private SpellcastingComponent? _spellcasting;
    private ProgressionComponent? _progression;
    private SchoolMasteryComponent? _mastery;

    private VBoxContainer _body = null!;

    /// <summary>The schools, in the order the book presents them. Fixed rather than derived from
    /// the spell database so the ring does not reorder itself as spells are learned.</summary>
    private static readonly DamageType[] Schools =
    {
        DamageType.Fire, DamageType.Frost, DamageType.Lightning,
        DamageType.Arcane, DamageType.Nature, DamageType.Necrotic,
    };

    private DamageType _school = DamageType.Fire;
    private SpellResource? _selected;

    // A corrupted spell is taken in two presses: the first arms it, the second confirms. Nothing else in
    // the book asks twice, which is what marks this one as a choice.
    private SpellResource? _armed;

    private float _shownPotency = -1f;

    // The favourite slot a pin goes to, chosen by pressing it in the pin row; none by default, and
    // a pin then takes the first free slot. _pinsFull is the refusal: every slot taken, none chosen.
    private int _pinSlot = SpellPinRules.NoSlot;
    private bool _pinsFull;

    private const float PinDiscSize = 22f;
    private const float CardDiscSize = 24f;

    // Below this usable width the eight slots sit in two rows of four, so a name is never cut to a stub.
    private const float PinRowWidth = 1400f;

    // For the screenshot harness: how many slots the last rebuild drew, and a card's pin button.
    private int _pinSlotsBuilt;
    private Button? _pinForCapture;
    private bool _pinForCapturePins;

    protected override string? ToggleAction => GameInput.Spellbook;

    protected override HubTab? Hub => HubTab.Spellbook;

    protected override IReadOnlyList<LegendEntry> Legend
    {
        get
        {
            var entries = new List<LegendEntry>
            {
                new(GameInput.MenuSubPrev, Loc.T("kn.legend.school"), GameInput.MenuSubNext),
            };
            if (_spellcasting is { SpellCount: > 0 })
            {
                entries.Add(new LegendEntry("ui_accept", Loc.T("spellbook.legend.pin")));
            }

            entries.AddRange(base.Legend);
            return entries;
        }
    }

    /// <summary>Schools step on the sub-tab actions (Z / C, LT / RT), in the ring's order, wrapping.</summary>
    protected override void OnSubTab(int delta)
    {
        int at = System.Array.IndexOf(Schools, _school);
        UiAudio.Play(UiCue.Tab);
        ShowSchool((int)Schools[(((at + delta) % Schools.Length) + Schools.Length) % Schools.Length]);
    }

    protected override void BuildShell(PanelContainer shell)
    {
        UiTheme.ApplyScreenInset(shell);

        // The cold ground. Overrides UiPanel's default parchment frame rather than extending it —
        // this screen is deliberately not made of the same material as the rest of the UI.
        //
        // The material is its own; the cut is the hub's (UiTheme.ApplyHubPlate): one lit edge along
        // the top, here in tarnished silver, no box round it, and the same soft shadow. A full
        // two-pixel silver frame made this the one boxed window of the five.
        var box = new StyleBoxFlat
        {
            BgColor = UiTheme.ArcaneGround,
            BorderColor = UiTheme.HighContrast ? UiTheme.ArcaneSilver : UiTheme.ArcaneSilver with { A = 0.80f },
        };
        box.SetBorderWidthAll(0);
        box.BorderWidthTop = UiTheme.HighContrast ? 3 : 1;
        box.SetCornerRadiusAll(UiTheme.RadiusSm);
        box.ShadowColor = UiTheme.Engrave;
        box.ShadowSize = UiTheme.HighContrast ? UiTheme.SpaceXs : UiTheme.SpaceSm;
        box.ShadowOffset = new Vector2(0f, UiTheme.Space2xs);
        shell.AddThemeStyleboxOverride("panel", box);

        // Vellum rather than parchment: much finer grain, tinted toward the glyph light so the
        // surface reads cold instead of merely dark.
        UiTheme.ApplyGrain(shell, grain: 0.22f, fibre: 0.10f, mottle: 0.16f, tint: UiTheme.GlyphLight);

        // Ambient sigils, behind everything. Added first so every widget draws over it.
        shell.AddChild(UiOrnament.SigilField(alphaMax: 0.07f, density: 11f));

        MarginContainer margin = UiTheme.Padding(UiTheme.PanelPad);
        shell.AddChild(margin);

        _body = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        _body.AddThemeConstantOverride("separation", UiTheme.SpaceSm);
        margin.AddChild(_body);
    }

    protected override void OnReady()
    {
        EventBus.Instance?.Subscribe<SpellsChangedEvent>(OnSpellsChanged);
        EventBus.Instance?.Subscribe<SpellSelectedEvent>(OnSpellSelected);
        EventBus.Instance?.Subscribe<XpGainedEvent>(OnDirty);
        EventBus.Instance?.Subscribe<LeveledUpEvent>(OnLevelled);
        EventBus.Instance?.Subscribe<CorruptionChangedEvent>(OnCorruption);
        EventBus.Instance?.Subscribe<SchoolRankedUpEvent>(OnRanked);
        EventBus.Instance?.Subscribe<SpellLearnedEvent>(OnLearned);
    }

    /// <summary>A chosen slot and a refusal belong to one visit to the book.</summary>
    protected override void OnOpenChanged(bool open)
    {
        base.OnOpenChanged(open);
        _pinSlot = SpellPinRules.NoSlot;
        _pinsFull = false;
    }

    public override void _Process(double delta)
    {
        base._Process(delta);

        // The Weave is a live global, set on a region change: the header line must follow it.
        if (IsOpen && !Mathf.IsEqualApprox(_shownPotency, Weave.Potency))
        {
            MarkDirty();
        }
    }

    public override void _ExitTree()
    {
        EventBus.Instance?.Unsubscribe<SpellsChangedEvent>(OnSpellsChanged);
        EventBus.Instance?.Unsubscribe<SpellSelectedEvent>(OnSpellSelected);
        EventBus.Instance?.Unsubscribe<XpGainedEvent>(OnDirty);
        EventBus.Instance?.Unsubscribe<LeveledUpEvent>(OnLevelled);
        EventBus.Instance?.Unsubscribe<CorruptionChangedEvent>(OnCorruption);
        EventBus.Instance?.Unsubscribe<SchoolRankedUpEvent>(OnRanked);
        EventBus.Instance?.Unsubscribe<SpellLearnedEvent>(OnLearned);
    }

    private void OnRanked(SchoolRankedUpEvent e) => MarkDirty();

    private void OnLearned(SpellLearnedEvent e) => MarkDirty();

    private void OnSpellsChanged(SpellsChangedEvent e) => MarkDirty();

    private void OnSpellSelected(SpellSelectedEvent e) => MarkDirty();

    private void OnDirty(XpGainedEvent e) => MarkDirty();

    private void OnLevelled(LeveledUpEvent e) => MarkDirty();

    private void OnCorruption(CorruptionChangedEvent e) => MarkDirty();

    public void SetSpellcasting(SpellcastingComponent? spellcasting)
    {
        _spellcasting = spellcasting;
        _mastery = spellcasting?.Entity?.GetComponent<SchoolMasteryComponent>();
        MarkDirty();
    }

    /// <summary>Opens the book on a school (a <see cref="DamageType"/> ordinal); the probe and the
    /// screenshot harnesses use it to reach a page without pressing buttons.</summary>
    public void ShowSchool(int school)
    {
        _school = (DamageType)school;
        _selected = null;
        _armed = null;
        MarkDirty();
    }

    public void SetProgression(ProgressionComponent? progression)
    {
        _progression = progression;
        MarkDirty();
    }

    protected override void Rebuild()
    {
        UiTheme.ClearChildren(_body);
        UiTheme.ApplyScreenInset(Shell);
        _pinSlotsBuilt = 0;
        _pinForCapture = null;
        _pinForCapturePins = false;

        float usable = UiTheme.UsableWidth(Shell);
        float ring = Mathf.Clamp(usable * 0.30f, 190f, 300f);
        float detail = Mathf.Clamp(usable * 0.28f, 190f, 280f);

        if (_spellcasting == null || SpellDatabase.All.Count == 0)
        {
            _body.AddChild(UiTheme.Body(Loc.T("spellbook.none"), UiTheme.Dim));
            return;
        }

        _shownPotency = Weave.Potency;
        _body.AddChild(BuildHeader());
        foreach (Control line in BuildWeaveLine())
        {
            _body.AddChild(line);
        }

        _body.AddChild(BuildPins(usable));

        // The hairline every hub page has under its title and tabs.
        _body.AddChild(UiTheme.RowRule());

        var row = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", UiTheme.SpaceLg);
        _body.AddChild(row);

        row.AddChild(BuildSchoolRing(ring));
        row.AddChild(BuildSpellList());
        row.AddChild(BuildDetail(detail));
    }

    /// <summary>The book's title, with the shimmer. One of only two places in the game that gets
    /// it (the other is the title screen) — see the ornament budget.</summary>
    private Control BuildHeader()
    {
        var stack = new Control { CustomMinimumSize = new Vector2(0f, 34f) };

        // The size and face of every hub title; the silver and the shimmer are this screen's own.
        Label title = UiTheme.Title(Loc.T("spellbook.title"));
        title.AddThemeColorOverride("font_color", UiTheme.ArcaneSilver);
        title.VerticalAlignment = VerticalAlignment.Center;
        title.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        stack.AddChild(title);
        stack.AddChild(UiOrnament.InkShimmer(UiTheme.GlyphLight, period: 9f, intensity: 0.35f));

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", UiTheme.SpaceMd);
        stack.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(stack);

        if (_progression is { SpellPoints: > 0 })
        {
            PanelContainer chip = UiTheme.Chip(
                Loc.TF("char.spell_points", _progression.SpellPoints), UiTheme.GlyphLight);
            chip.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            row.AddChild(chip);
        }

        if (WeaveMath.ShowsIndicator(Weave.Potency))
        {
            PanelContainer weave = UiTheme.Chip(WeaveChipText(), WeaveTint(Weave.Band));
            weave.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            row.AddChild(weave);
        }

        return row;
    }

    /// <summary>The region's Weave in one chip: the band and how it bends an ordinary cast.</summary>
    internal static string WeaveChipText() => Loc.TF(
        "magic.book.weave_chip", Loc.T(WeaveBandKey(Weave.Band)),
        Signed(WeaveMath.PowerPercent(Weave.Potency, corrupted: false)));

    internal static string WeaveBandKey(WeaveBand band) => band switch
    {
        WeaveBand.Thinning => "magic.book.weave_thinning",
        WeaveBand.Frayed => "magic.book.weave_frayed",
        WeaveBand.Failing => "magic.book.weave_failing",
        _ => "magic.book.weave_strong",
    };

    internal static Color WeaveTint(WeaveBand band) => band switch
    {
        WeaveBand.Thinning => UiTheme.Accent,
        WeaveBand.Frayed => UiTheme.AccentHot,
        WeaveBand.Failing => UiTheme.Bad,
        _ => UiTheme.Dim,
    };

    internal static string Signed(int value) => value > 0 ? $"+{value}" : value.ToString();

    /// <summary>A plain sentence under the title saying what the Weave does to casting right now,
    /// shown only while it is not strong. Both paths, so the corrupted temptation is stated, not hinted.</summary>
    private static IEnumerable<Control> BuildWeaveLine()
    {
        if (!WeaveMath.ShowsIndicator(Weave.Potency))
        {
            yield break;
        }

        float p = Weave.Potency;
        yield return Wrapped(UiTheme.Caption(Loc.TF(
            "magic.book.weave_line",
            Signed(WeaveMath.PowerPercent(p, false)),
            Signed((int)Mathf.Round((WeaveMath.CostMultiplier(p, false) - 1f) * 100f)),
            Signed(WeaveMath.PowerPercent(p, true)),
            Signed((int)Mathf.Round((WeaveMath.CostMultiplier(p, true) - 1f) * 100f)))));
    }

    /// <summary>
    /// Lets a line of text wrap. ⚠️ Every sentence on this page goes through it. A label that does
    /// not wrap reports its whole length as its minimum width, the spell list reports the widest of
    /// them, and the three columns then asked for more than the frame had: the page ran 28 px past
    /// its right edge at 1280 and a whole column off a handheld.
    /// </summary>
    private static Label Wrapped(Label label)
    {
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        return label;
    }

    /// <summary>
    /// The pin row: the eight favourite slots the spell wheel's inner ring draws, numbered as the
    /// wheel lays them out (1 at the top, then clockwise), each with its spell's glyph and name.
    ///
    /// It replaces the prepared row, which listed every known spell in cycle order. The wheel made
    /// that order mean nothing; what the player needs here is which eight spells are one flick
    /// away, and a way to change them. Pressing a slot chooses it as where the next pin goes (and
    /// opens its spell's page); a card's Pin button fills the first free slot when none is chosen.
    ///
    /// ⚠️ The line over the slots is always there, whatever it says: the panel restores focus by
    /// child index across a rebuild, and a line that came and went would shift every row under it.
    /// </summary>
    private Control BuildPins(float usable)
    {
        var block = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        block.AddThemeConstantOverride("separation", UiTheme.SpaceXs);

        IReadOnlyList<string> pins = _spellcasting!.Favourites;
        if (_pinSlot >= pins.Count)
        {
            _pinSlot = SpellPinRules.NoSlot;
        }

        block.AddChild(Wrapped(
            _pinSlot >= 0
                ? UiTheme.Caption(Loc.TF("spellbook.pins_chosen", SpellPinRules.SlotNumber(_pinSlot)), UiTheme.Accent)
                : _pinsFull
                    ? UiTheme.Caption(Loc.T("spellbook.pins_full"), UiTheme.AccentHot)
                    : UiTheme.Caption(Loc.T("spellbook.pins"))));

        var grid = new GridContainer
        {
            Columns = usable >= PinRowWidth ? pins.Count : Mathf.Max(1, pins.Count / 2),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        grid.AddThemeConstantOverride("h_separation", UiTheme.ChipGap);
        grid.AddThemeConstantOverride("v_separation", UiTheme.ChipGap);
        for (int i = 0; i < pins.Count; i++)
        {
            grid.AddChild(BuildPinSlot(i, pins[i]));
        }

        _pinSlotsBuilt = pins.Count;
        block.AddChild(grid);
        return block;
    }

    private Control BuildPinSlot(int slot, string spellId)
    {
        SpellResource? spell = spellId.Length > 0 ? SpellDatabase.Get(spellId) : null;
        bool chosen = slot == _pinSlot;
        bool prepared = spell != null && _spellcasting!.Selected?.Id == spell.Id;
        Color tint = spell != null ? SpellSchools.Color(spell.School) : UiTheme.Disabled;
        Color? edge = chosen ? UiTheme.Accent : spell != null ? tint : null;

        PanelContainer card = UiTheme.CardButton(
            edge, out Button input, out VBoxContainer col, UiTheme.Compact(UiTheme.CardStyle(edge)));
        card.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", UiTheme.SpaceSm);
        row.AddChild(Centred(UiTheme.Caption(
            SpellPinRules.SlotNumber(slot).ToString(), chosen ? UiTheme.Accent : UiTheme.Dim)));

        SpellDisc disc = SpellDisc.Create(PinDiscSize);
        disc.Display(spell);
        row.AddChild(disc);

        Label name = UiTheme.Body(
            spell != null ? SpellText.Name(spell) : Loc.T("wheel.slot.empty"),
            spell == null ? UiTheme.Disabled : prepared ? tint : UiTheme.Text);
        name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        name.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        row.AddChild(name);
        col.AddChild(row);

        input.TooltipText = spell != null ? SpellText.Name(spell) : Loc.T("wheel.slot.hint");
        input.Pressed += () =>
        {
            UiAudio.Play(UiCue.Click);
            _pinsFull = false;
            _pinSlot = _pinSlot == slot ? SpellPinRules.NoSlot : slot;
            if (_pinSlot >= 0 && spell != null)
            {
                _school = spell.School;
                _selected = spell;
                _armed = null;
            }

            MarkDirty();
        };
        return card;
    }

    /// <summary>
    /// The school ring: the rotating rune diagram with the six schools listed over it, each showing
    /// its mastery as filled segments.
    ///
    /// The diagram is a <see cref="ColorRect"/> sat behind the list rather than a panel background,
    /// because its polar maths needs UV to span its rect — see <see cref="UiOrnament"/>.
    /// </summary>
    private Control BuildSchoolRing(float width)
    {
        var frame = new Control { CustomMinimumSize = new Vector2(width, 0f) };

        ColorRect ring = UiOrnament.RuneCircle(width, UiTheme.GlyphLight, intensity: 0.5f, ticks: 30f);
        ring.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        ring.Position = new Vector2(0f, 10f);
        frame.AddChild(ring);

        // Six two-line rows plus their gaps are taller than a 720 px page, so the list scrolls over the
        // ring (Necrotic, the last school, was cut off the bottom without it).
        (ScrollContainer scroll, VBoxContainer col) = UiTheme.ScrollList();
        scroll.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        frame.AddChild(scroll);

        foreach (DamageType school in Schools)
        {
            col.AddChild(BuildSchoolRow(school));
        }

        return frame;
    }

    private Control BuildSchoolRow(DamageType school)
    {
        Color tint = SpellSchools.Color(school);
        int rank = _mastery?.RankOf(school) ?? 0;
        bool active = school == _school;

        // CardButton, not a Button with anchored children (37.5H). These rows were pinned to a
        // hand-guessed 44 px for two lines of content, which the text-scale setting overran the
        // moment it moved off 1.0 — the name and the mastery meter drew over each other.
        PanelContainer card = UiTheme.CardButton(active ? tint : null, out Button input, out VBoxContainer col);

        DamageType captured = school;
        input.Pressed += () =>
        {
            _school = captured;
            _selected = null;
            _armed = null;
            MarkDirty();
        };

        Label name = UiTheme.Body(Loc.T(SchoolKey(school)), active ? tint : UiTheme.Text);
        UiTheme.ApplyType(name, UiTheme.FontRole.Display, UiTheme.HeaderFontSize);
        col.AddChild(name);

        var meter = new HBoxContainer();
        meter.AddThemeConstantOverride("separation", UiTheme.Space2xs);
        for (int i = 0; i < SchoolMasteryMath.MaxRank; i++)
        {
            meter.AddChild(new ColorRect
            {
                Color = i < rank ? tint : UiTheme.Engrave,
                CustomMinimumSize = new Vector2(18f, 4f),
                MouseFilter = Control.MouseFilterEnum.Ignore,
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            });
        }

        meter.AddChild(UiTheme.Caption(
            $"  {Loc.TF("magic.book.rank_short", rank, SchoolMasteryMath.MaxRank)}  " +
            $"+{(int)Mathf.Round((SchoolMasteryMath.PowerMultiplier(rank) - 1f) * 100f)}%",
            rank > 0 ? tint : UiTheme.Disabled));
        col.AddChild(meter);

        return card;
    }

    /// <summary>The selected school's spells, one card each, in a scroll: a full roster does not fit a page.</summary>
    private Control BuildSpellList()
    {
        (ScrollContainer scroll, VBoxContainer col) = UiTheme.ScrollList();

        Color tint = SpellSchools.Color(_school);
        col.AddChild(UiTheme.SectionRule(Loc.T(SchoolKey(_school))));

        // The rank line, its bar and the three perk lines are one block, tighter inside than the cards below.
        var masteryBlock = new VBoxContainer();
        masteryBlock.AddThemeConstantOverride("separation", UiTheme.SpaceXs);
        foreach (Control mastery in BuildMastery(tint))
        {
            masteryBlock.AddChild(mastery);
        }

        if (masteryBlock.GetChildCount() > 0)
        {
            col.AddChild(masteryBlock);
        }

        var spells = new List<SpellResource>();
        foreach (SpellResource spell in SpellDatabase.All)
        {
            // Enemy-only loadouts (the Phase 34 caster roster) stay out of the player's book —
            // unless the player has actually recovered one (35F: an Ancient dragon teaches lost
            // spellcraft that can never be bought). A known spell must always be listed, or the
            // reward for the fight is a spell the spellbook says you do not have.
            if (spell.School == _school && (spell.PlayerLearnable || _spellcasting!.IsKnown(spell)))
            {
                spells.Add(spell);
            }
        }

        // Plain before corrupted, cheap before dear: a stable order that does not shuffle on learning.
        spells.Sort((a, b) =>
        {
            int byTier = a.MinCorruptionTier.CompareTo(b.MinCorruptionTier);
            if (byTier != 0)
            {
                return byTier;
            }

            int byCost = a.ManaCost.CompareTo(b.ManaCost);
            return byCost != 0 ? byCost : string.CompareOrdinal(a.Id, b.Id);
        });

        foreach (SpellResource spell in spells)
        {
            col.AddChild(BuildSpellCard(spell, tint));
        }

        if (spells.Count == 0)
        {
            col.AddChild(UiTheme.Body(Loc.T("spellbook.school_empty"), UiTheme.Dim));
        }

        foreach (Control synergy in BuildSynergies(tint))
        {
            col.AddChild(synergy);
        }

        return scroll;
    }

    /// <summary>The school's mastery: rank, progress to the next rank, and what each perk does. Perks the
    /// reader has earned are lit in the school colour; the rest wait in dim.</summary>
    private IEnumerable<Control> BuildMastery(Color tint)
    {
        if (_mastery == null)
        {
            yield break;
        }

        int rank = _mastery.RankOf(_school);
        string school = Loc.T(SchoolKey(_school));

        (int into, int needed) = SchoolMasteryMath.ProgressToNext(_mastery.PointsIn(_school));
        yield return Wrapped(UiTheme.Caption(
            rank >= SchoolMasteryMath.MaxRank
                ? Loc.TF("magic.book.rank_maxed", rank, SchoolMasteryMath.MaxRank)
                : Loc.TF("magic.book.rank_progress", rank, SchoolMasteryMath.MaxRank, into, needed),
            UiTheme.Text));

        if (rank < SchoolMasteryMath.MaxRank)
        {
            ProgressBar bar = UiTheme.Bar(tint);
            bar.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            bar.CustomMinimumSize = new Vector2(0f, 4f);
            bar.Value = needed > 0 ? into / (double)needed : 0d;
            yield return bar;
        }

        yield return Wrapped(UiTheme.Caption(
            Loc.TF("magic.book.perk_power", (int)Mathf.Round(SchoolMasteryMath.PowerPerRank * 100f)),
            rank >= 1 ? tint : UiTheme.Dim));
        yield return Wrapped(UiTheme.Caption(
            Loc.TF("magic.book.perk_cooldown", SchoolMasteryMath.FirstCooldownRank,
                (int)Mathf.Round(SchoolMasteryMath.CooldownTrimPerStep * 100f), SchoolMasteryMath.SecondCooldownRank),
            rank >= SchoolMasteryMath.FirstCooldownRank ? tint : UiTheme.Dim));
        yield return Wrapped(UiTheme.Caption(
            Loc.TF("magic.book.perk_attune", SchoolMasteryMath.AttunementRank,
                (int)SchoolMasteryMath.AttunementResist, school),
            rank >= SchoolMasteryMath.AttunementRank ? tint : UiTheme.Dim));
    }

    private Control BuildSpellCard(SpellResource spell, Color tint)
    {
        bool known = _spellcasting!.IsKnown(spell);
        int rank = _spellcasting.RankOf(spell);
        bool selected = ReferenceEquals(spell, _selected);
        int tierNow = (int)SpellLearning.TierOf(_spellcasting.Entity!);
        bool locked = SpellBookRules.IsTierLocked(known, tierNow, (int)spell.MinCorruptionTier);
        bool corrupted = SpellLearnRules.IsCorrupted((int)spell.MinCorruptionTier);

        PanelContainer card = UiTheme.Card(known ? tint : corrupted ? UiTheme.CorruptionText : UiTheme.Disabled);
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", UiTheme.SpaceXs);

        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", UiTheme.SpaceSm);

        SpellDisc disc = SpellDisc.Create(CardDiscSize);
        disc.Display(spell, known);
        head.AddChild(disc);

        var pick = new Button { Text = SpellText.Name(spell), Flat = true };
        pick.AddThemeColorOverride("font_color", known ? tint : UiTheme.Dim);
        pick.AddThemeColorOverride("font_hover_color", UiTheme.Text);
        pick.AddThemeColorOverride("font_focus_color", UiTheme.Accent);
        UiTheme.ApplyType(pick, UiTheme.FontRole.Display, UiTheme.BodyFontSize);
        pick.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        pick.Alignment = HorizontalAlignment.Left;
        pick.AutowrapMode = TextServer.AutowrapMode.WordSmart; // a long name wraps; it never sets the card's width
        SpellResource captured = spell;
        pick.Pressed += () =>
        {
            _selected = captured;
            MarkDirty();
        };
        head.AddChild(pick);

        if (known)
        {
            head.AddChild(Centred(RankPips(rank, spell.MaxRank, tint)));
        }

        head.AddChild(Centred(ActionFor(spell, known, rank, locked)));
        col.AddChild(head);

        // The at-a-glance costs. A spell's mana, cooldown, wind-up and shape decide whether it is usable
        // in the fight you are in, and they were previously only in the description text.
        var chips = new HFlowContainer();
        chips.AddThemeConstantOverride("h_separation", UiTheme.ChipGap);
        chips.AddThemeConstantOverride("v_separation", UiTheme.ChipGap);
        chips.AddChild(UiTheme.Chip(Loc.TF("spellbook.mana", spell.ManaCost.ToString("0")), UiTheme.Mana));
        chips.AddChild(UiTheme.Chip(Loc.TF("spellbook.cooldown", spell.Cooldown.ToString("0.#")), UiTheme.Dim));

        float windup = EffectiveWindup(spell);
        if (windup > 0f)
        {
            chips.AddChild(UiTheme.Chip(Loc.TF("magic.book.windup_chip", windup.ToString("0.##")), UiTheme.Dim));
        }

        chips.AddChild(UiTheme.Chip(Loc.T(DeliveryKey(spell.Delivery)), UiTheme.GlyphLight));

        if (spell.CastMode is CastMode.Charged)
        {
            chips.AddChild(UiTheme.Chip(Loc.T("char.mode_charged"), UiTheme.GlyphLight));
        }
        else if (spell.CastMode is CastMode.Channeled)
        {
            chips.AddChild(UiTheme.Chip(Loc.T("char.mode_channeled"), UiTheme.GlyphLight));
        }

        if (spell.HasStatusEffect && StatusEffectDatabase.Get(spell.StatusEffectId) is { } status)
        {
            chips.AddChild(UiTheme.Chip(
                Loc.TF("magic.book.applies_chip", SpellText.Name(status)),
                status.IsBeneficial ? UiTheme.Good : SpellSchools.Color(status.School)));
        }

        col.AddChild(chips);

        if (known)
        {
            col.AddChild(BuildCardActions(spell));
        }

        if (locked)
        {
            col.AddChild(Wrapped(UiTheme.Caption(LockReason(spell, tierNow), UiTheme.CorruptionText)));
        }

        List<SpellRuleLine> rules = SpellBookRules.Rules(SpellBookRules.FactsOf(spell));
        if (rules.Count > 0)
        {
            col.AddChild(Wrapped(UiTheme.Caption(Loc.TF(rules[0].Key, rules[0].Value.ToString("0.#")), UiTheme.Text)));
        }

        if (ReferenceEquals(_armed, spell))
        {
            col.AddChild(Wrapped(UiTheme.Caption(Loc.T("magic.book.embrace_warning"), UiTheme.CorruptionText)));
        }

        if (selected && !string.IsNullOrWhiteSpace(SpellText.Description(spell)))
        {
            col.AddChild(UiTheme.Flavour(SpellText.Description(spell)));
        }

        card.AddChild(col);
        return card;
    }

    /// <summary>
    /// A known spell's two verbs: prepare it (what the cast key casts) and pin it to the wheel.
    ///
    /// ⚠️ Both are always buttons, so the row keeps its shape. The prepared spell's first button
    /// reads "Prepared" and pressing it prepares it again, which changes nothing: were it a chip,
    /// the rebuild after a press would find no button where focus had been and drop a pad's focus
    /// back to the top of the page.
    /// </summary>
    private Control BuildCardActions(SpellResource spell)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", UiTheme.SpaceSm);

        bool prepared = _spellcasting!.Selected?.Id == spell.Id;
        Button prepare = UiTheme.Action(Loc.T(prepared ? "spellbook.prepared" : "spellbook.prepare"), UiCue.Confirm);
        if (prepared)
        {
            prepare.AddThemeColorOverride("font_color", UiTheme.Accent);
        }

        string id = spell.Id;
        prepare.Pressed += () =>
        {
            // Refused only while a cast is in flight, which a menu can open over.
            if (!_spellcasting!.Select(id))
            {
                UiAudio.Play(UiCue.Denied);
            }
        };
        row.AddChild(prepare);

        SpellPinChoice choice = SpellPinRules.Decide(_spellcasting.Favourites, id, _pinSlot);
        Button pin = UiTheme.Action(
            choice.Kind == SpellPinKind.Unpin ? Loc.T("spellbook.unpin")
            : _pinSlot >= 0 ? Loc.TF("spellbook.pin_to", SpellPinRules.SlotNumber(choice.Slot))
            : Loc.T("spellbook.pin"),
            choice.Kind == SpellPinKind.Pin ? UiCue.Confirm : choice.Kind == SpellPinKind.Full ? UiCue.Denied : UiCue.Click);
        pin.Pressed += () => TogglePin(id);
        row.AddChild(pin);

        if (_pinForCapture == null || (!_pinForCapturePins && choice.Kind == SpellPinKind.Pin))
        {
            _pinForCapture = pin;
            _pinForCapturePins = choice.Kind == SpellPinKind.Pin;
        }

        return row;
    }

    /// <summary>Pins or unpins through the caster, which says so with a
    /// <see cref="SpellsChangedEvent"/>; a refusal (every slot taken, none chosen) is said in the
    /// line over the pin row.</summary>
    private void TogglePin(string spellId)
    {
        SpellPinChoice choice = SpellPinRules.Decide(_spellcasting!.Favourites, spellId, _pinSlot);
        _pinsFull = choice.Kind == SpellPinKind.Full;
        if (choice.Kind != SpellPinKind.Full)
        {
            _spellcasting.SetFavourite(
                choice.Slot, choice.Kind == SpellPinKind.Pin ? spellId : SpellFavouritesRules.None);
            _pinSlot = SpellPinRules.NoSlot;
        }

        MarkDirty();
    }

    /// <summary>Turns to the first school with a known spell that is not pinned, so a card's Pin
    /// button is on the page (the screenshot harness).</summary>
    public void ShowUnpinnedForCapture()
    {
        if (_spellcasting == null)
        {
            return;
        }

        foreach (DamageType school in Schools)
        {
            foreach (SpellResource spell in _spellcasting.Spells)
            {
                if (spell.School == school && SpellFavouritesRules.IndexOf(_spellcasting.Favourites, spell.Id) < 0)
                {
                    ShowSchool((int)school);
                    return;
                }
            }
        }
    }

    /// <summary>How many favourite slots the page drew (the screenshot harness).</summary>
    public int PinSlotsForCapture => IsOpen ? _pinSlotsBuilt : 0;

    /// <summary>Focuses a card's pin button, one that would pin if the page has one (the
    /// screenshot harness). False when the page has no known spell.</summary>
    public bool FocusPinForCapture()
    {
        if (_pinForCapture is not { } pin || !IsInstanceValid(pin))
        {
            return false;
        }

        pin.GrabFocus();
        return true;
    }

    /// <summary>Whether a card's pin button holds focus (the screenshot harness).</summary>
    public bool PinFocusedForCapture =>
        _pinForCapture is { } pin && IsInstanceValid(pin) && pin.HasFocus();

    /// <summary>Why a corrupted spell is out of reach: which tier it needs and which the reader is at.</summary>
    private static string LockReason(SpellResource spell, int tierNow) => Loc.TF(
        "magic.book.locked",
        CorruptionTiers.DisplayName(spell.MinCorruptionTier),
        CorruptionTiers.DisplayName((CorruptionTier)tierNow));

    /// <summary>The wind-up the cast will actually use: the spell's own, else the derived cast action's
    /// release point.</summary>
    private static float EffectiveWindup(SpellResource spell)
    {
        if (spell.WindupSeconds > 0f)
        {
            return spell.WindupSeconds;
        }

        return SpellActions.For(spell) is { } action ? action.FallbackDuration * action.ActiveFrom : 0f;
    }

    /// <summary>The card's one verb: buy, upgrade, or a chip saying why neither is available.
    /// Refusals name themselves rather than the button simply being absent. A corrupted spell is taken
    /// in two presses (embrace, then confirm) so it is a choice and not a click.</summary>
    private Control ActionFor(SpellResource spell, bool known, int rank, bool locked)
    {
        if (locked)
        {
            return UiTheme.Chip(
                Loc.TF("char.spell_needs", CorruptionTiers.DisplayName(spell.MinCorruptionTier)),
                UiTheme.CorruptionText);
        }

        if (!known && _spellcasting!.CanBuy(spell))
        {
            SpellResource captured = spell;
            if (SpellLearnRules.IsCorrupted((int)spell.MinCorruptionTier))
            {
                bool armed = ReferenceEquals(_armed, spell);
                Button embrace = UiTheme.Action(Loc.TF(
                    armed ? "magic.book.embrace_confirm" : "magic.book.embrace", spell.LearnCost));
                embrace.Pressed += () =>
                {
                    if (!ReferenceEquals(_armed, captured))
                    {
                        _armed = captured;
                        _selected = captured;
                        MarkDirty();
                        return;
                    }

                    _armed = null;
                    LearnFromBook(captured);
                };
                return embrace;
            }

            Button buy = UiTheme.Action(Loc.TF("char.spell_buy", spell.LearnCost));
            buy.Pressed += () => LearnFromBook(captured);
            return buy;
        }

        if (known && _spellcasting!.CanUpgrade(spell))
        {
            Button up = UiTheme.Action(Loc.TF("char.spell_upgrade", spell.UpgradeCost));
            SpellResource captured = spell;
            up.Pressed += () => _spellcasting!.Upgrade(captured);
            return up;
        }

        if (known && rank >= spell.MaxRank)
        {
            return UiTheme.Chip(Loc.T("char.spell_maxed"), UiTheme.Accent);
        }

        return UiTheme.Chip(Loc.TF("char.spell_cost", spell.LearnCost), UiTheme.Disabled);
    }

    /// <summary>Buys a spell with spell points; the casting component announces the successful purchase.</summary>
    private void LearnFromBook(SpellResource spell)
    {
        _spellcasting!.Buy(spell);
    }

    /// <summary>
    /// The school's reactive combos, read straight from <see cref="SpellCombo"/>'s rule table.
    ///
    /// **This is the only place in the game that says these interactions exist.** Shatter and
    /// Thermal Shock have been live since Phase 29.5D and were discoverable only by casting
    /// lightning into a chilled enemy and noticing the number was bigger.
    /// </summary>
    private IEnumerable<Control> BuildSynergies(Color tint)
    {
        var rules = new List<ComboRule>(SpellCombo.ForSchool(_school));
        if (rules.Count == 0)
        {
            yield break;
        }

        yield return UiTheme.SectionRule(Loc.T("spellbook.synergies"));

        foreach (ComboRule rule in rules)
        {
            StatusEffectResource? status = StatusEffectDatabase.Get(rule.RequiredStatusId);
            string statusName = status?.DisplayName ?? rule.RequiredStatusId;

            PanelContainer card = UiTheme.Card(tint);
            var col = new VBoxContainer();
            col.AddThemeConstantOverride("separation", UiTheme.LineGap);
            col.AddChild(Wrapped(UiTheme.Body(rule.Name, tint)));
            col.AddChild(Wrapped(UiTheme.Caption(Loc.TF(
                "spellbook.synergy_line",
                Loc.T(SchoolKey(_school)), statusName, rule.BonusDamage.ToString("0")))));

            card.AddChild(col);
            yield return card;
        }
    }

    /// <summary>The right-hand page: the selected spell in full, in a scroll.</summary>
    private Control BuildDetail(float width)
    {
        (ScrollContainer scroll, VBoxContainer col) = UiTheme.ScrollList();
        scroll.CustomMinimumSize = new Vector2(width, 0f);
        scroll.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        col.AddThemeConstantOverride("separation", UiTheme.SpaceSm);

        if (_selected is not { } spell)
        {
            col.AddChild(Wrapped(UiTheme.Body(Loc.T("spellbook.select_hint"), UiTheme.Dim)));
            return scroll;
        }

        Color tint = SpellSchools.Color(spell.School);
        Label name = UiTheme.Body(SpellText.Name(spell), tint);
        UiTheme.ApplyType(name, UiTheme.FontRole.Display, UiTheme.TitleFontSize);
        name.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        col.AddChild(name);

        col.AddChild(Wrapped(UiTheme.Caption(Loc.TF(
            "spellbook.delivery",
            Loc.T(SchoolKey(spell.School)), Loc.T(DeliveryKey(spell.Delivery))))));

        bool known = _spellcasting!.IsKnown(spell);
        int tierNow = (int)SpellLearning.TierOf(_spellcasting.Entity!);
        if (SpellBookRules.IsTierLocked(known, tierNow, (int)spell.MinCorruptionTier))
        {
            col.AddChild(UiTheme.Prose(LockReason(spell, tierNow), UiTheme.CorruptionText));
        }
        else if (!known && SpellLearnRules.IsCorrupted((int)spell.MinCorruptionTier))
        {
            col.AddChild(UiTheme.Prose(Loc.T("magic.book.embrace_warning"), UiTheme.CorruptionText));
        }

        col.AddChild(UiTheme.Divider());

        // Cost and cooldown, then what the region's Weave and the reader's mastery do to them.
        bool corrupted = SpellLearnRules.IsCorrupted((int)spell.MinCorruptionTier);
        float weaveCost = _spellcasting?.EffectiveManaCost(spell) ?? spell.ManaCost * Weave.CostMultiplier(corrupted);
        AddStat(col, Loc.T("spellbook.mana_label"),
            Mathf.Abs(weaveCost - spell.ManaCost) >= 0.5f
                ? Loc.TF("magic.book.here_value", spell.ManaCost.ToString("0"), weaveCost.ToString("0"))
                : spell.ManaCost.ToString("0"));

        float cooldown = spell.Cooldown * (_mastery?.CooldownMultiplier(spell.School) ?? 1f);
        AddStat(col, Loc.T("spellbook.cooldown_label"),
            Mathf.Abs(cooldown - spell.Cooldown) >= 0.05f
                ? Loc.TF("magic.book.here_value", spell.Cooldown.ToString("0.#"), cooldown.ToString("0.#"))
                : spell.Cooldown.ToString("0.#"));

        float windup = EffectiveWindup(spell);
        if (windup > 0f)
        {
            AddStat(col, Loc.T("magic.book.windup_label"), Loc.TF("magic.book.seconds", windup.ToString("0.##")));
        }

        if (spell.BaseDamage > 0f)
        {
            AddStat(col, Loc.T("spellbook.damage_label"), spell.BaseDamage.ToString("0"));
        }

        if (spell.Healing > 0f)
        {
            AddStat(col, Loc.T("spellbook.healing_label"), spell.Healing.ToString("0"));
        }

        if (spell.HealthCost > 0f)
        {
            AddStat(col, Loc.T("magic.book.health_cost_label"), spell.HealthCost.ToString("0"));
        }

        if (spell.Delivery is SpellDelivery.Projectile or SpellDelivery.Ground or SpellDelivery.Barrier)
        {
            AddStat(col, Loc.T("spellbook.range_label"),
                (spell.Delivery == SpellDelivery.Projectile ? spell.Range : spell.PlaceRange).ToString("0"));
        }

        if (Weave.Band != WeaveBand.Strong)
        {
            col.AddChild(Wrapped(UiTheme.Caption(
                Loc.TF(
                    "magic.book.weave_spell",
                    Signed(WeaveMath.PowerPercent(Weave.Potency, corrupted)),
                    Signed((int)Mathf.Round((WeaveMath.CostMultiplier(Weave.Potency, corrupted) - 1f) * 100f))),
                WeaveTint(Weave.Band))));
        }

        // What it applies, then the special rules in plain words.
        if (spell.HasStatusEffect && StatusEffectDatabase.Get(spell.StatusEffectId) is { } status)
        {
            col.AddChild(UiTheme.Divider());
            col.AddChild(UiTheme.Chip(
                Loc.TF("magic.book.applies_chip", SpellText.Name(status)),
                status.IsBeneficial ? UiTheme.Good : SpellSchools.Color(status.School)));
            col.AddChild(Wrapped(UiTheme.Caption(Loc.TF(
                "magic.book.status_line", status.Duration.ToString("0.#"), Mathf.Max(1, status.MaxStacks)))));
            if (SpellText.Description(status) is { Length: > 0 } statusText)
            {
                col.AddChild(UiTheme.Prose(statusText));
            }
        }

        List<string> rules = new(SpellBookRules.RuleText(spell));
        if (rules.Count > 0)
        {
            col.AddChild(UiTheme.Divider());
            foreach (string rule in rules)
            {
                col.AddChild(UiTheme.Prose(rule, UiTheme.Text));
            }
        }

        string description = SpellText.Description(spell);
        if (!string.IsNullOrWhiteSpace(description))
        {
            col.AddChild(UiTheme.Divider());
            col.AddChild(UiTheme.Prose(description));
        }

        return scroll;
    }

    private static void AddStat(VBoxContainer col, string label, string value)
    {
        var row = new HBoxContainer();
        Label name = UiTheme.Caption(label);
        name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(name);
        row.AddChild(UiTheme.Body(value));
        col.AddChild(row);
    }

    private static Control RankPips(int rank, int maxRank, Color tint)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", UiTheme.Space2xs);
        for (int i = 0; i < Mathf.Max(1, maxRank); i++)
        {
            row.AddChild(new ColorRect
            {
                Color = i < rank ? tint : UiTheme.Engrave,
                CustomMinimumSize = new Vector2(9f, 6f),
                MouseFilter = Control.MouseFilterEnum.Ignore,
            });
        }

        return row;
    }

    private static Control Centred(Control control)
    {
        control.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        return control;
    }

    private static string SchoolKey(DamageType school) => school switch
    {
        DamageType.Fire => "school.fire",
        DamageType.Frost => "school.frost",
        DamageType.Lightning => "school.lightning",
        DamageType.Arcane => "school.arcane",
        DamageType.Nature => "school.nature",
        DamageType.Necrotic => "school.necrotic",
        _ => "school.fire",
    };

    private static string DeliveryKey(SpellDelivery delivery) => delivery switch
    {
        SpellDelivery.Area => "spellbook.delivery_area",
        SpellDelivery.Self => "spellbook.delivery_self",
        SpellDelivery.Cone => "spellbook.delivery_cone",
        SpellDelivery.Ground => "magic.book.delivery_ground",
        SpellDelivery.Barrier => "magic.book.delivery_barrier",
        SpellDelivery.Dash => "magic.book.delivery_dash",
        _ => "spellbook.delivery_projectile",
    };
}
