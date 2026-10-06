using System.Collections.Generic;
using Embervale.Combat;
using Embervale.Core;
using Embervale.Enemies;
using Embervale.Localization;
using Embervale.Stats;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The Ash Hunters' field journal (Phase 34G): every creature in the game, what the party has
/// killed, how many of those were corrupted, and lore that opens as you hunt. LORE casts the Ash
/// Hunters as a "monster hunting organization" that "track dragons and corrupted beasts" — hence
/// the Ashen tally rather than a plain count.
///
/// Built on the 30.5F <see cref="UiPanel"/> framework, so the modal contract, the toggle input, the
/// dirty-flag rebuild and focus restoration all come from the base. Service-backed like
/// <see cref="MapScreen"/> rather than component-backed like <see cref="InventoryPanel"/> — it
/// documents the world, not the player.
///
/// A list and a page, like the quest journal beside it in the hub. The list is every creature of
/// the open category; one the party has never brought down is a sealed row with no name. The page
/// opens in the stages <see cref="BestiaryStages"/> has always had (what each stage shows is
/// <see cref="BestiaryFactRules"/>): nothing, then a name and a tally, then the study itself, which
/// is the creature's wards and the lore.
/// </summary>
public partial class BestiaryPanel : UiPanel
{
    private static readonly (BestiaryCategory Category, string Key)[] TabDefs =
    {
        (BestiaryCategory.Beast, "bestiary.tab_beasts"),
        (BestiaryCategory.Humanoid, "bestiary.tab_humanoids"),
        (BestiaryCategory.Undead, "bestiary.tab_undead"),
        (BestiaryCategory.Construct, "bestiary.tab_constructs"),
        (BestiaryCategory.Elemental, "bestiary.tab_elementals"),
        (BestiaryCategory.Ashen, "bestiary.tab_ashen"),
        (BestiaryCategory.Boss, "bestiary.tab_bosses"),
    };

    private static readonly DamageType[] Schools =
    {
        DamageType.Fire, DamageType.Frost, DamageType.Lightning,
        DamageType.Arcane, DamageType.Nature, DamageType.Necrotic,
    };

    private BestiaryService? _bestiary;
    private UiTabs _tabs = null!;
    private VBoxContainer _index = null!;
    private VBoxContainer _list = null!;
    private VBoxContainer _detail = null!;
    private Label _progress = null!;
    private ProgressBar _meter = null!;
    private BestiaryCategory _activeTab = BestiaryCategory.Beast;
    private string? _selectedId;
    private int _seenRevision = -1;

    // Modal by default: the tab buttons need the mouse (the reason MapScreen gives).
    protected override string? ToggleAction => GameInput.Bestiary;

    protected override HubTab? Hub => HubTab.Bestiary;

    protected override IReadOnlyList<LegendEntry> Legend
    {
        get
        {
            var entries = new List<LegendEntry>
            {
                new(GameInput.MenuSubPrev, Loc.T("kn.legend.category"), GameInput.MenuSubNext),
            };
            entries.AddRange(base.Legend);
            return entries;
        }
    }

    /// <summary>Injected by the bootstrap, the way <see cref="MapScreen"/> takes its services.</summary>
    public void SetBestiary(BestiaryService service)
    {
        _bestiary = service;
        MarkDirty();
    }

    /// <summary>The creature whose page is open, as of the last rebuild. Read by the screenshot harness.</summary>
    public string? SelectedId => _selectedId;

    /// <summary>The stage the open page is at, or null with no page open.</summary>
    public BestiaryStage? SelectedStage =>
        _bestiary != null && _selectedId != null && BestiaryDatabase.Get(_selectedId) is { } entry
            ? _bestiary.StageOf(entry)
            : null;

    /// <summary>Opens a creature's page (and its category) for the screenshot harness, the way
    /// stepping to the tab and choosing the row does.</summary>
    public void SelectForCapture(string id)
    {
        if (BestiaryDatabase.Get(id) is not { } entry)
        {
            return;
        }

        for (int i = 0; i < TabDefs.Length; i++)
        {
            if (TabDefs[i].Category == entry.Category)
            {
                _tabs.Select(i);
            }
        }

        _selectedId = id;
        MarkDirty();
    }

    protected override void BuildShell(PanelContainer shell)
    {
        UiTheme.ApplyScreenInset(shell);

        _tabs = new UiTabs();
        foreach ((BestiaryCategory _, string key) in TabDefs)
        {
            _tabs.Add(Loc.T(key));
        }

        _tabs.TabChanged += index =>
        {
            _activeTab = TabDefs[index].Category;
            _selectedId = null;
            MarkDirty();
        };

        VBoxContainer column = UiTheme.HubPage(shell, Loc.T("kn.title.bestiary"), out HBoxContainer aside, _tabs);

        // How much of the book is written: the one line of context this screen has.
        _progress = UiTheme.Caption(string.Empty, UiTheme.Dim);
        _progress.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        aside.AddChild(_progress);
        _meter = UiTheme.Bar(UiTheme.Accent, 120f);
        _meter.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        aside.AddChild(_meter);

        var body = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", UiTheme.SpaceMd);
        column.AddChild(body);

        // Bare ground under the rows, one Band round the page: one frame per list (UI_STYLE §4).
        _index = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        _index.CustomMinimumSize = new Vector2(JournalLayoutRules.IndexWidth(UiTheme.UsableWidth(shell)), 0f);
        body.AddChild(_index);
        (ScrollContainer indexScroll, VBoxContainer indexList) = UiTheme.ScrollList();
        _list = indexList;
        _index.AddChild(indexScroll);

        PanelContainer page = UiTheme.Band(UiTheme.Accent);
        page.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        page.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        body.AddChild(page);
        (ScrollContainer pageScroll, VBoxContainer pageList) = UiTheme.ScrollList();
        _detail = pageList;
        _detail.AddThemeConstantOverride("separation", UiTheme.SpaceSm);
        page.AddChild(pageScroll);
    }

    /// <summary>Categories step on the sub-tab actions (Z / C, LT / RT), wrapping at the ends.</summary>
    protected override void OnSubTab(int delta)
    {
        int next = (((_tabs.Current + delta) % TabDefs.Length) + TabDefs.Length) % TabDefs.Length;
        UiAudio.Play(UiCue.Tab);
        _tabs.Select(next);

        // The tab row scrolls sideways on a narrow viewport; keep the lit tab in it.
        if (_tabs.GetParent() is ScrollContainer rail && _tabs.GetChild(next) is Control tab)
        {
            rail.EnsureControlVisible(tab);
        }
    }

    /// <summary>Kills accrue while the panel is shut, and the service raises no event — so poll its
    /// revision the way <see cref="MapScreen"/> polls the map's.</summary>
    public override void _Process(double delta)
    {
        base._Process(delta);

        if (IsOpen && _bestiary != null && _bestiary.Revision != _seenRevision)
        {
            _seenRevision = _bestiary.Revision;
            MarkDirty();
        }
    }

    protected override void Rebuild()
    {
        // Called here as well as in BuildShell: the UI-scale setting can change mid-session.
        UiTheme.ApplyScreenInset(Shell);
        _index.CustomMinimumSize = new Vector2(JournalLayoutRules.IndexWidth(UiTheme.UsableWidth(Shell)), 0f);

        UiTheme.ClearChildren(_list);
        UiTheme.ClearChildren(_detail);

        int total = BestiaryDatabase.All.Count;
        _meter.Visible = _bestiary != null && total > 0;
        if (_bestiary == null)
        {
            _progress.Text = string.Empty;
            _list.AddChild(UiTheme.Body(Loc.T("bestiary.empty"), UiTheme.Dim));
            return;
        }

        _progress.Text = Loc.TF("kn.bestiary.progress", _bestiary.DiscoveredCount, total);
        _meter.Value = total == 0 ? 0d : _bestiary.DiscoveredCount / (double)total;

        var shown = new List<BestiaryEntryResource>();
        foreach (BestiaryEntryResource entry in BestiaryDatabase.All)
        {
            if (entry.Category == _activeTab)
            {
                shown.Add(entry);
            }
        }

        if (shown.Count == 0)
        {
            _list.AddChild(UiTheme.Body(Loc.T("bestiary.empty"), UiTheme.Dim));
            return;
        }

        BestiaryEntryResource selected = Selected(shown);
        _selectedId = selected.Id;
        foreach (BestiaryEntryResource entry in shown)
        {
            _list.AddChild(IndexRow(entry, entry == selected));
        }

        BuildPage(selected);
    }

    /// <summary>The page a category opens on: the one already chosen, else the first creature the
    /// party has met (a category should not open on a sealed page while a written one exists), else
    /// the first listed.</summary>
    private BestiaryEntryResource Selected(List<BestiaryEntryResource> shown)
    {
        BestiaryEntryResource? met = null;
        foreach (BestiaryEntryResource entry in shown)
        {
            if (entry.Id == _selectedId)
            {
                return entry;
            }

            if (met == null && _bestiary!.StageOf(entry) != BestiaryStage.Unseen)
            {
                met = entry;
            }
        }

        return met ?? shown[0];
    }

    // --- Index ---------------------------------------------------------------------------

    private Control IndexRow(BestiaryEntryResource entry, bool selected)
    {
        BestiaryService bestiary = _bestiary!;
        BestiaryStage stage = bestiary.StageOf(entry);

        // The spine says how much is written, and the caption under the name says it in words.
        Color spine = stage switch
        {
            BestiaryStage.Known => UiTheme.Accent,
            BestiaryStage.Sighted => UiTheme.Dim,
            _ => UiTheme.Disabled,
        };

        PanelContainer card = UiTheme.CardButton(spine, out Button input, out VBoxContainer content);
        if (selected)
        {
            StyleBoxFlat style = UiTheme.CardStyle(spine);
            style.BgColor = UiTheme.CardBg with { A = 1f };
            style.BorderWidthLeft = 4;
            card.AddThemeStyleboxOverride("panel", style);
        }

        if (!BestiaryFactRules.ShowsName(stage))
        {
            // Sealed. No name and no lore leak: the blank row is the hook, and a column of them reads
            // as "not yet" rather than "broken" because each says so.
            var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            row.AddThemeConstantOverride("separation", UiTheme.SpaceSm);
            row.AddChild(UiIcon.Create(UiIcon.Kind.Lock, 16f, UiTheme.Disabled));
            Label sealedName = UiTheme.Body(Loc.T("kn.bestiary.unknown"), UiTheme.Disabled);
            sealedName.MouseFilter = Control.MouseFilterEnum.Ignore;
            row.AddChild(sealedName);
            content.AddChild(row);
        }
        else
        {
            int kills = bestiary.KillsOf(entry.Id);
            Label name = UiTheme.Body(NameOf(entry), selected ? UiTheme.Accent : UiTheme.Text);
            UiTheme.ApplyType(name, UiTheme.FontRole.Display, UiTheme.BodyFontSize);
            name.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            name.MouseFilter = Control.MouseFilterEnum.Ignore;
            content.AddChild(name);

            Label note = UiTheme.Caption(
                stage == BestiaryStage.Known
                    ? Loc.TF("bestiary.kills", kills)
                    : Loc.TF("kn.bestiary.row_sighted", kills, Mathf.Max(1, entry.KillsToKnow)),
                UiTheme.Dim);
            note.MouseFilter = Control.MouseFilterEnum.Ignore;
            content.AddChild(note);
        }

        string id = entry.Id;
        input.Pressed += () =>
        {
            _selectedId = id;
            MarkDirty();
        };
        return card;
    }

    // --- Page ----------------------------------------------------------------------------

    private void BuildPage(BestiaryEntryResource entry)
    {
        BestiaryService bestiary = _bestiary!;
        BestiaryStage stage = bestiary.StageOf(entry);

        if (!BestiaryFactRules.ShowsName(stage))
        {
            BuildSealedPage(entry);
            return;
        }

        bool known = stage == BestiaryStage.Known;
        int kills = bestiary.KillsOf(entry.Id);

        Label name = UiTheme.Header(NameOf(entry));
        name.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _detail.AddChild(name);

        HFlowContainer chips = UiTheme.FlowRow();
        chips.AddChild(UiTheme.Chip(CategoryName(entry.Category), UiTheme.Dim));
        chips.AddChild(UiTheme.Chip(
            Loc.T(known ? "kn.bestiary.stage_known" : "kn.bestiary.stage_sighted"),
            known ? UiTheme.Accent : UiTheme.Text));
        _detail.AddChild(chips);

        _detail.AddChild(HeroFact(kills, bestiary.AshenKillsOf(entry.Id)));

        if (BestiaryFactRules.ShowsProgress(stage))
        {
            // Sighted: say there is more to learn, and show how much of the page is filled. The bar
            // turns "3 more to fill the page" into something readable without counting.
            int needed = Mathf.Max(1, entry.KillsToKnow);
            Label more = UiTheme.Caption(Loc.TF("bestiary.sighted", needed - kills), UiTheme.Dim);
            more.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            _detail.AddChild(more);

            ProgressBar toward = UiTheme.Bar(UiTheme.Dim);
            toward.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            toward.CustomMinimumSize = new Vector2(0f, UiTheme.Space2xs);
            toward.Value = Mathf.Clamp(kills / (double)needed, 0d, 1d);
            _detail.AddChild(toward);
        }

        if (!BestiaryFactRules.ShowsStudy(stage))
        {
            return;
        }

        List<SchoolResist> wards = WardsOf(entry);
        AddSchoolRow("kn.bestiary.resists", BestiaryFactRules.Resists(wards));
        AddSchoolRow("kn.bestiary.open_to", BestiaryFactRules.OpenTo(wards));

        // The page is written: the lore reads as a book, in the book face and at a book's measure.
        _detail.AddChild(UiTheme.SectionRule(Loc.T("kn.bestiary.lore")));
        _detail.AddChild(UiTheme.Measure(UiTheme.Prose(Loc.T(entry.LoreKey)), DetailWidth()));
    }

    private void BuildSealedPage(BestiaryEntryResource entry)
    {
        var mark = new HBoxContainer();
        mark.AddThemeConstantOverride("separation", UiTheme.SpaceMd);
        mark.AddChild(UiIcon.Create(UiIcon.Kind.Lock, UiTheme.DisplayFontSize + UiTheme.SpaceMd, UiTheme.Disabled));

        var copy = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        copy.AddThemeConstantOverride("separation", UiTheme.LineGap);
        Label title = UiTheme.Body(Loc.T("kn.bestiary.unknown"), UiTheme.Dim);
        UiTheme.ApplyType(title, UiTheme.FontRole.Display, UiTheme.HeaderFontSize);
        copy.AddChild(title);
        copy.AddChild(UiTheme.Caption(CategoryName(entry.Category), UiTheme.Dim));
        mark.AddChild(copy);
        _detail.AddChild(mark);

        _detail.AddChild(UiTheme.Measure(UiTheme.Prose(Loc.T("kn.bestiary.unknown_hint"), UiTheme.Dim), DetailWidth()));
    }

    /// <summary>The one number the page leads with: how many the party has brought down. Large, with
    /// its word beside it, and the Ashen share as a chip when there is one.</summary>
    private static Control HeroFact(int kills, int ashen)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", UiTheme.SpaceSm);

        Label number = UiTheme.Display(kills.ToString(), UiTheme.Text);
        UiTheme.ApplyType(number, UiTheme.FontRole.Interface, UiTheme.DisplayFontSize);
        row.AddChild(number);

        Label word = UiTheme.Caption(Loc.T("kn.bestiary.slain"), UiTheme.Dim);
        word.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        row.AddChild(word);

        if (ashen > 0)
        {
            PanelContainer ashChip = UiTheme.Chip(Loc.TF("bestiary.corrupted", ashen), UiTheme.CorruptionText);
            ashChip.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            row.AddChild(ashChip);
        }

        return row;
    }

    /// <summary>A caption and a wrapping row of school chips, each an icon and the school's name in
    /// the school's colour. Nothing when no school qualifies.</summary>
    private void AddSchoolRow(string captionKey, List<int> schools)
    {
        if (schools.Count == 0)
        {
            return;
        }

        HFlowContainer row = UiTheme.FlowRow();
        Label caption = UiTheme.Caption(Loc.T(captionKey), UiTheme.Dim);
        caption.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        row.AddChild(caption);
        foreach (int school in schools)
        {
            var type = (DamageType)school;
            row.AddChild(UiTheme.IconChip(UiIcon.Kind.Spell, Loc.T(SchoolNameKey(type)), UiTheme.SchoolColor(type)));
        }

        _detail.AddChild(row);
    }

    /// <summary>A creature's ward against each school, from the attribute set its archetype spawns
    /// with. Empty for the bespoke creatures that have no archetype.</summary>
    private static List<SchoolResist> WardsOf(BestiaryEntryResource entry)
    {
        var wards = new List<SchoolResist>();
        if (EnemyArchetypeDatabase.Get(entry.Id) is not { AttributesPath.Length: > 0 } archetype ||
            ResidentResources.Load<AttributeSet>(archetype.AttributesPath) is not { } stats)
        {
            return wards;
        }

        foreach (DamageType school in Schools)
        {
            wards.Add(new SchoolResist((int)school, school switch
            {
                DamageType.Fire => stats.FireResist,
                DamageType.Frost => stats.FrostResist,
                DamageType.Lightning => stats.LightningResist,
                DamageType.Arcane => stats.ArcaneResist,
                DamageType.Nature => stats.NatureResist,
                _ => stats.NecroticResist,
            }));
        }

        return wards;
    }

    private static string SchoolNameKey(DamageType school) => school switch
    {
        DamageType.Frost => "school.frost",
        DamageType.Lightning => "school.lightning",
        DamageType.Arcane => "school.arcane",
        DamageType.Nature => "school.nature",
        DamageType.Necrotic => "school.necrotic",
        _ => "school.fire",
    };

    private static string CategoryName(BestiaryCategory category)
    {
        foreach ((BestiaryCategory tab, string key) in TabDefs)
        {
            if (tab == category)
            {
                return Loc.T(key);
            }
        }

        return string.Empty;
    }

    /// <summary>The width the page's text has: the screen less the index, the gap between the panes,
    /// the band's own margins and the scroll gutter.</summary>
    private float DetailWidth() =>
        UiTheme.UsableWidth(Shell) - _index.CustomMinimumSize.X - UiTheme.SpaceMd -
        (UiTheme.SpaceLg + UiTheme.SpaceMd) - UiTheme.ScrollGutter;

    /// <summary>An entry's own name key wins; otherwise the archetype's. The bespoke creatures
    /// (goblin, Iron King, Ashen Acolyte) have no archetype, which is why the override exists.</summary>
    private static string NameOf(BestiaryEntryResource entry)
    {
        if (entry.NameKey.Length > 0)
        {
            return Loc.T(entry.NameKey);
        }

        EnemyArchetypeResource? archetype = EnemyArchetypeDatabase.Get(entry.Id);
        return archetype is { NameKey.Length: > 0 } ? Loc.T(archetype.NameKey) : entry.Id;
    }
}
