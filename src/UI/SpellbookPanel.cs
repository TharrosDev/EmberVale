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

    protected override string? ToggleAction => GameInput.Spellbook;

    protected override void BuildShell(PanelContainer shell)
    {
        UiTheme.ApplyScreenInset(shell);

        // The cold ground. Overrides UiPanel's default parchment frame rather than extending it —
        // this screen is deliberately not made of the same material as the rest of the UI.
        var box = new StyleBoxFlat
        {
            BgColor = UiTheme.ArcaneGround,
            BorderColor = UiTheme.ArcaneSilver with { A = 0.75f },
        };
        box.SetBorderWidthAll(2);
        box.SetCornerRadiusAll(UiTheme.RadiusLg);
        box.ShadowColor = UiTheme.Engrave;
        box.ShadowSize = 1;
        shell.AddThemeStyleboxOverride("panel", box);

        // Vellum rather than parchment: much finer grain, tinted toward the glyph light so the
        // surface reads cold instead of merely dark.
        UiTheme.ApplyGrain(shell, grain: 0.22f, fibre: 0.10f, mottle: 0.16f, tint: UiTheme.GlyphLight);

        // Ambient sigils, behind everything. Added first so every widget draws over it.
        shell.AddChild(UiOrnament.SigilField(alphaMax: 0.07f, density: 11f));

        MarginContainer margin = UiTheme.Padding(12);
        shell.AddChild(margin);

        _body = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        _body.AddThemeConstantOverride("separation", UiTheme.SpaceMd);
        margin.AddChild(_body);
    }

    protected override void OnReady()
    {
        EventBus.Instance?.Subscribe<SpellsChangedEvent>(OnSpellsChanged);
        EventBus.Instance?.Subscribe<XpGainedEvent>(OnDirty);
        EventBus.Instance?.Subscribe<LeveledUpEvent>(OnLevelled);
        EventBus.Instance?.Subscribe<CorruptionChangedEvent>(OnCorruption);
        EventBus.Instance?.Subscribe<SchoolRankedUpEvent>(OnRanked);
        EventBus.Instance?.Subscribe<SpellLearnedEvent>(OnLearned);
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
        EventBus.Instance?.Unsubscribe<XpGainedEvent>(OnDirty);
        EventBus.Instance?.Unsubscribe<LeveledUpEvent>(OnLevelled);
        EventBus.Instance?.Unsubscribe<CorruptionChangedEvent>(OnCorruption);
        EventBus.Instance?.Unsubscribe<SchoolRankedUpEvent>(OnRanked);
        EventBus.Instance?.Unsubscribe<SpellLearnedEvent>(OnLearned);
    }

    private void OnRanked(SchoolRankedUpEvent e) => MarkDirty();

    private void OnLearned(SpellLearnedEvent e) => MarkDirty();

    private void OnSpellsChanged(SpellsChangedEvent e) => MarkDirty();

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

        _body.AddChild(BuildPrepared());

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

        Label title = UiTheme.Display(Loc.T("spellbook.title"), UiTheme.ArcaneSilver);
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
        yield return UiTheme.Caption(Loc.TF(
            "magic.book.weave_line",
            Signed(WeaveMath.PowerPercent(p, false)),
            Signed((int)Mathf.Round((WeaveMath.CostMultiplier(p, false) - 1f) * 100f)),
            Signed(WeaveMath.PowerPercent(p, true)),
            Signed((int)Mathf.Round((WeaveMath.CostMultiplier(p, true) - 1f) * 100f))));
    }

    /// <summary>
    /// The prepared row: the known spells in cycle order, the current one lit.
    ///
    /// This exists because <c>Q</c> casts "the selected spell" and <c>F</c> cycles it, and until now
    /// nothing on any screen said what that order was or where in it you were — the HUD shows only
    /// the current spell's name. A caster with six spells was cycling blind.
    /// </summary>
    private Control BuildPrepared()
    {
        // A flow, not a row: a full roster is up to 25 chips and must wrap rather than run off the page.
        var row = new HFlowContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("h_separation", UiTheme.SpaceXs);
        row.AddThemeConstantOverride("v_separation", UiTheme.SpaceXs);
        row.AddChild(Centred(UiTheme.Caption(Loc.T("spellbook.prepared"))));

        IReadOnlyList<SpellResource> known = _spellcasting!.Spells;
        if (known.Count == 0)
        {
            row.AddChild(Centred(UiTheme.Caption(Loc.T("spellbook.prepared_none"), UiTheme.Disabled)));
            return row;
        }

        for (int i = 0; i < known.Count; i++)
        {
            SpellResource spell = known[i];
            bool current = i == _spellcasting.SelectedIndex;
            Color tint = SpellSchools.Color(spell.School);

            PanelContainer chip = UiTheme.Chip(SpellText.Name(spell), current ? tint : UiTheme.Dim);
            chip.TooltipText = SpellText.Description(spell);
            row.AddChild(Centred(chip));
        }

        return row;
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

        var col = new VBoxContainer();
        col.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        col.AddThemeConstantOverride("separation", UiTheme.SpaceXs);
        frame.AddChild(col);

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
        meter.AddThemeConstantOverride("separation", 2);
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
        col.AddThemeConstantOverride("separation", UiTheme.SpaceXs);

        Color tint = SpellSchools.Color(_school);
        col.AddChild(UiTheme.SectionRule(Loc.T(SchoolKey(_school))));

        foreach (Control mastery in BuildMastery(tint))
        {
            col.AddChild(mastery);
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
        yield return UiTheme.Caption(
            rank >= SchoolMasteryMath.MaxRank
                ? Loc.TF("magic.book.rank_maxed", rank, SchoolMasteryMath.MaxRank)
                : Loc.TF("magic.book.rank_progress", rank, SchoolMasteryMath.MaxRank, into, needed),
            UiTheme.Text);

        if (rank < SchoolMasteryMath.MaxRank)
        {
            ProgressBar bar = UiTheme.Bar(tint);
            bar.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            bar.CustomMinimumSize = new Vector2(0f, 4f);
            bar.Value = needed > 0 ? into / (double)needed : 0d;
            yield return bar;
        }

        yield return UiTheme.Caption(
            Loc.TF("magic.book.perk_power", (int)Mathf.Round(SchoolMasteryMath.PowerPerRank * 100f)),
            rank >= 1 ? tint : UiTheme.Dim);
        yield return UiTheme.Caption(
            Loc.TF("magic.book.perk_cooldown", SchoolMasteryMath.FirstCooldownRank,
                (int)Mathf.Round(SchoolMasteryMath.CooldownTrimPerStep * 100f), SchoolMasteryMath.SecondCooldownRank),
            rank >= SchoolMasteryMath.FirstCooldownRank ? tint : UiTheme.Dim);
        yield return UiTheme.Caption(
            Loc.TF("magic.book.perk_attune", SchoolMasteryMath.AttunementRank,
                (int)SchoolMasteryMath.AttunementResist, school),
            rank >= SchoolMasteryMath.AttunementRank ? tint : UiTheme.Dim);
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
        col.AddThemeConstantOverride("separation", 2);

        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", UiTheme.SpaceSm);

        var pick = new Button { Text = SpellText.Name(spell), Flat = true };
        pick.AddThemeColorOverride("font_color", known ? tint : UiTheme.Dim);
        pick.AddThemeColorOverride("font_hover_color", UiTheme.Text);
        pick.AddThemeColorOverride("font_focus_color", UiTheme.Accent);
        UiTheme.ApplyType(pick, UiTheme.FontRole.Display, UiTheme.BodyFontSize);
        pick.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        pick.Alignment = HorizontalAlignment.Left;
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
        chips.AddThemeConstantOverride("h_separation", UiTheme.SpaceXs);
        chips.AddThemeConstantOverride("v_separation", UiTheme.SpaceXs);
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

        if (locked)
        {
            col.AddChild(UiTheme.Caption(LockReason(spell, tierNow), UiTheme.CorruptionText));
        }

        List<SpellRuleLine> rules = SpellBookRules.Rules(SpellBookRules.FactsOf(spell));
        if (rules.Count > 0)
        {
            col.AddChild(UiTheme.Caption(Loc.TF(rules[0].Key, rules[0].Value.ToString("0.#")), UiTheme.Text));
        }

        if (ReferenceEquals(_armed, spell))
        {
            col.AddChild(UiTheme.Caption(Loc.T("magic.book.embrace_warning"), UiTheme.CorruptionText));
        }

        if (selected && !string.IsNullOrWhiteSpace(SpellText.Description(spell)))
        {
            col.AddChild(UiTheme.Flavour(SpellText.Description(spell)));
        }

        MarginContainer pad = UiTheme.Padding(UiTheme.SpaceXs);
        pad.AddChild(col);
        card.AddChild(pad);
        return card;
    }

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
            col.AddThemeConstantOverride("separation", 1);
            col.AddChild(UiTheme.Body(rule.Name, tint));
            col.AddChild(UiTheme.Caption(Loc.TF(
                "spellbook.synergy_line",
                Loc.T(SchoolKey(_school)), statusName, rule.BonusDamage.ToString("0"))));

            MarginContainer pad = UiTheme.Padding(UiTheme.SpaceXs);
            pad.AddChild(col);
            card.AddChild(pad);
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
            col.AddChild(UiTheme.Body(Loc.T("spellbook.select_hint"), UiTheme.Dim));
            return scroll;
        }

        Color tint = SpellSchools.Color(spell.School);
        Label name = UiTheme.Body(SpellText.Name(spell), tint);
        UiTheme.ApplyType(name, UiTheme.FontRole.Display, UiTheme.TitleFontSize);
        name.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        col.AddChild(name);

        col.AddChild(UiTheme.Caption(Loc.TF(
            "spellbook.delivery",
            Loc.T(SchoolKey(spell.School)), Loc.T(DeliveryKey(spell.Delivery)))));

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
        float weaveCost = spell.ManaCost * Weave.CostMultiplier(corrupted);
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
            col.AddChild(UiTheme.Caption(
                Loc.TF(
                    "magic.book.weave_spell",
                    Signed(WeaveMath.PowerPercent(Weave.Potency, corrupted)),
                    Signed((int)Mathf.Round((WeaveMath.CostMultiplier(Weave.Potency, corrupted) - 1f) * 100f))),
                WeaveTint(Weave.Band)));
        }

        // What it applies, then the special rules in plain words.
        if (spell.HasStatusEffect && StatusEffectDatabase.Get(spell.StatusEffectId) is { } status)
        {
            col.AddChild(UiTheme.Divider());
            col.AddChild(UiTheme.Chip(
                Loc.TF("magic.book.applies_chip", SpellText.Name(status)),
                status.IsBeneficial ? UiTheme.Good : SpellSchools.Color(status.School)));
            col.AddChild(UiTheme.Caption(Loc.TF(
                "magic.book.status_line", status.Duration.ToString("0.#"), Mathf.Max(1, status.MaxStacks))));
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
        row.AddThemeConstantOverride("separation", 2);
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
