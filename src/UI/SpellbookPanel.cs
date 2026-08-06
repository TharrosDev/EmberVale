using System.Collections.Generic;
using Embervale.Combat;
using Embervale.Core;
using Embervale.Core.Events;
using Embervale.Corruption;
using Embervale.Localization;
using Embervale.Magic;
using Embervale.Progression;
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

    /// <summary>Screen-edge gutter, matching the character screen so the two feel like facing
    /// pages rather than differently-sized windows.</summary>
    private const float ScreenMargin = 70f;

    protected override string? ToggleAction => GameInput.Spellbook;

    protected override void BuildShell(PanelContainer shell)
    {
        shell.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        shell.OffsetLeft = ScreenMargin;
        shell.OffsetTop = ScreenMargin;
        shell.OffsetRight = -ScreenMargin;
        shell.OffsetBottom = -ScreenMargin;

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
    }

    public override void _ExitTree()
    {
        EventBus.Instance?.Unsubscribe<SpellsChangedEvent>(OnSpellsChanged);
        EventBus.Instance?.Unsubscribe<XpGainedEvent>(OnDirty);
        EventBus.Instance?.Unsubscribe<LeveledUpEvent>(OnLevelled);
        EventBus.Instance?.Unsubscribe<CorruptionChangedEvent>(OnCorruption);
    }

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

    public void SetProgression(ProgressionComponent? progression)
    {
        _progression = progression;
        MarkDirty();
    }

    protected override void Rebuild()
    {
        UiTheme.ClearChildren(_body);

        if (_spellcasting == null || SpellDatabase.All.Count == 0)
        {
            _body.AddChild(UiTheme.Body(Loc.T("spellbook.none"), UiTheme.Dim));
            return;
        }

        _body.AddChild(BuildHeader());
        _body.AddChild(BuildPrepared());

        var row = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", UiTheme.SpaceLg);
        _body.AddChild(row);

        row.AddChild(BuildSchoolRing());
        row.AddChild(BuildSpellList());
        row.AddChild(BuildDetail());
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

        return row;
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
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", UiTheme.SpaceXs);
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

            PanelContainer chip = UiTheme.Chip(spell.DisplayName, current ? tint : UiTheme.Dim);
            chip.TooltipText = spell.Description;
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
    private Control BuildSchoolRing()
    {
        var frame = new Control { CustomMinimumSize = new Vector2(300f, 0f) };

        ColorRect ring = UiOrnament.RuneCircle(300f, UiTheme.GlyphLight, intensity: 0.5f, ticks: 30f);
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

        var button = new Button { Flat = true, ToggleMode = false };
        var box = new StyleBoxFlat
        {
            BgColor = active ? UiTheme.CardBg with { A = 0.75f } : new Color(0f, 0f, 0f, 0f),
            BorderColor = active ? tint : new Color(0f, 0f, 0f, 0f),
        };
        box.SetBorderWidthAll(0);
        box.BorderWidthLeft = 3;
        box.SetContentMarginAll(UiTheme.SpaceXs);
        box.SetCornerRadiusAll(UiTheme.RadiusSm);
        button.AddThemeStyleboxOverride("normal", box);

        StyleBoxFlat hover = (StyleBoxFlat)box.Duplicate();
        hover.BgColor = UiTheme.CardBg;
        button.AddThemeStyleboxOverride("hover", hover);
        button.AddThemeStyleboxOverride("pressed", hover);

        StyleBoxFlat focus = (StyleBoxFlat)box.Duplicate();
        focus.BorderColor = UiTheme.Accent;
        focus.SetBorderWidthAll(1);
        focus.BorderWidthLeft = 3;
        button.AddThemeStyleboxOverride("focus", focus);

        DamageType captured = school;
        button.Pressed += () =>
        {
            _school = captured;
            _selected = null;
            MarkDirty();
        };

        var col = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        col.AddThemeConstantOverride("separation", 2);
        col.SetAnchorsPreset(Control.LayoutPreset.FullRect);

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
                CustomMinimumSize = new Vector2(22f, 4f),
                MouseFilter = Control.MouseFilterEnum.Ignore,
            });
        }

        meter.AddChild(UiTheme.Caption(
            $"  +{(int)Mathf.Round((SchoolMasteryMath.PowerMultiplier(rank) - 1f) * 100f)}%",
            rank > 0 ? tint : UiTheme.Disabled));
        col.AddChild(meter);

        button.AddChild(col);
        button.CustomMinimumSize = new Vector2(0f, 44f);
        return button;
    }

    /// <summary>The selected school's spells, one card each.</summary>
    private Control BuildSpellList()
    {
        var col = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        col.AddThemeConstantOverride("separation", UiTheme.SpaceXs);

        Color tint = SpellSchools.Color(_school);
        col.AddChild(UiTheme.SectionRule(Loc.T(SchoolKey(_school))));

        // Progress toward the next mastery rank, hidden once capped.
        int rank = _mastery?.RankOf(_school) ?? 0;
        if (_mastery != null && rank < SchoolMasteryMath.MaxRank)
        {
            ProgressBar bar = UiTheme.Bar(tint);
            bar.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            bar.CustomMinimumSize = new Vector2(0f, 4f);
            bar.Value = (_mastery.PointsIn(_school) % SchoolMasteryMath.PointsPerRank)
                / (double)SchoolMasteryMath.PointsPerRank;
            col.AddChild(bar);
        }

        int shown = 0;
        foreach (SpellResource spell in SpellDatabase.All)
        {
            // Enemy-only loadouts (the Phase 34 caster roster) stay out of the player's book —
            // unless the player has actually recovered one (35F: an Ancient dragon teaches lost
            // spellcraft that can never be bought). A known spell must always be listed, or the
            // reward for the fight is a spell the spellbook says you do not have.
            if (spell.School != _school || (!spell.PlayerLearnable && !_spellcasting!.IsKnown(spell)))
            {
                continue;
            }

            col.AddChild(BuildSpellCard(spell, tint));
            shown++;
        }

        if (shown == 0)
        {
            col.AddChild(UiTheme.Body(Loc.T("spellbook.school_empty"), UiTheme.Dim));
        }

        foreach (Control synergy in BuildSynergies(tint))
        {
            col.AddChild(synergy);
        }

        return col;
    }

    private Control BuildSpellCard(SpellResource spell, Color tint)
    {
        bool known = _spellcasting!.IsKnown(spell);
        int rank = _spellcasting.RankOf(spell);
        bool selected = ReferenceEquals(spell, _selected);

        PanelContainer card = UiTheme.Card(known ? tint : UiTheme.Disabled);
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 2);

        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", UiTheme.SpaceSm);

        var pick = new Button { Text = spell.DisplayName, Flat = true };
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

        head.AddChild(Centred(ActionFor(spell, known, rank)));
        col.AddChild(head);

        // The at-a-glance costs. A spell's mana, cooldown and cast mode decide whether it is usable
        // in the fight you are in, and they were previously only in the description text.
        var chips = new HBoxContainer();
        chips.AddThemeConstantOverride("separation", UiTheme.SpaceXs);
        chips.AddChild(UiTheme.Chip(Loc.TF("spellbook.mana", spell.ManaCost.ToString("0")), UiTheme.Mana));
        chips.AddChild(UiTheme.Chip(Loc.TF("spellbook.cooldown", spell.Cooldown.ToString("0.#")), UiTheme.Dim));

        if (spell.CastMode is CastMode.Charged)
        {
            chips.AddChild(UiTheme.Chip(Loc.T("char.mode_charged"), UiTheme.GlyphLight));
        }
        else if (spell.CastMode is CastMode.Channeled)
        {
            chips.AddChild(UiTheme.Chip(Loc.T("char.mode_channeled"), UiTheme.GlyphLight));
        }

        if (!known && !_spellcasting.MeetsCorruption(spell))
        {
            chips.AddChild(UiTheme.Chip(
                Loc.TF("char.spell_needs", CorruptionTiers.DisplayName(spell.MinCorruptionTier)),
                UiTheme.CorruptionText));
        }

        col.AddChild(chips);

        if (selected && !string.IsNullOrWhiteSpace(spell.Description))
        {
            col.AddChild(UiTheme.Flavour(spell.Description));
        }

        MarginContainer pad = UiTheme.Padding(UiTheme.SpaceXs);
        pad.AddChild(col);
        card.AddChild(pad);
        return card;
    }

    /// <summary>The card's one verb: buy, upgrade, or a chip saying why neither is available.
    /// Refusals name themselves rather than the button simply being absent.</summary>
    private Control ActionFor(SpellResource spell, bool known, int rank)
    {
        if (!known && _spellcasting!.CanBuy(spell))
        {
            Button buy = UiTheme.Action(Loc.TF("char.spell_buy", spell.LearnCost));
            SpellResource captured = spell;
            buy.Pressed += () => _spellcasting!.Buy(captured);
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

    /// <summary>The right-hand page: the selected spell in full.</summary>
    private Control BuildDetail()
    {
        var col = new VBoxContainer { CustomMinimumSize = new Vector2(280f, 0f) };
        col.AddThemeConstantOverride("separation", UiTheme.SpaceSm);

        if (_selected is not { } spell)
        {
            col.AddChild(UiTheme.Body(Loc.T("spellbook.select_hint"), UiTheme.Dim));
            return col;
        }

        Color tint = SpellSchools.Color(spell.School);
        Label name = UiTheme.Body(spell.DisplayName, tint);
        UiTheme.ApplyType(name, UiTheme.FontRole.Display, UiTheme.TitleFontSize);
        col.AddChild(name);

        col.AddChild(UiTheme.Caption(Loc.TF(
            "spellbook.delivery",
            Loc.T(SchoolKey(spell.School)), Loc.T(DeliveryKey(spell.Delivery)))));

        col.AddChild(UiTheme.Divider());

        AddStat(col, Loc.T("spellbook.mana_label"), spell.ManaCost.ToString("0"));
        AddStat(col, Loc.T("spellbook.cooldown_label"), spell.Cooldown.ToString("0.#"));

        if (spell.BaseDamage > 0f)
        {
            AddStat(col, Loc.T("spellbook.damage_label"), spell.BaseDamage.ToString("0"));
        }

        if (spell.Healing > 0f)
        {
            AddStat(col, Loc.T("spellbook.healing_label"), spell.Healing.ToString("0"));
        }

        AddStat(col, Loc.T("spellbook.range_label"), spell.Range.ToString("0"));

        if (!string.IsNullOrWhiteSpace(spell.Description))
        {
            col.AddChild(UiTheme.Divider());
            col.AddChild(UiTheme.Prose(spell.Description));
        }

        return col;
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
        _ => "spellbook.delivery_projectile",
    };
}
