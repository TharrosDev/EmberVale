using System.Collections.Generic;
using Embervale.Core;
using Embervale.Enemies;
using Embervale.Localization;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The Ash Hunters' field journal (Phase 34G): every creature in the game, what the party has
/// killed, how many of those were corrupted, and lore that opens as you hunt. LORE casts the Ash
/// Hunters as a "monster hunting organization" that "track dragons and corrupted beasts" — hence
/// the Ashen column rather than a plain tally.
///
/// Built on the 30.5F <see cref="UiPanel"/> framework, so the modal contract, the toggle input, the
/// dirty-flag rebuild and focus restoration all come from the base. Service-backed like
/// <see cref="MapScreen"/> rather than component-backed like <see cref="InventoryPanel"/> — it
/// documents the world, not the player.
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

    private BestiaryService? _bestiary;
    private UiTabs _tabs = null!;
    private VBoxContainer _list = null!;
    private BestiaryCategory _activeTab = BestiaryCategory.Beast;
    private int _seenRevision = -1;

    // Modal by default: the tab buttons need the mouse (the reason MapScreen gives).
    protected override string? ToggleAction => GameInput.Bestiary;

    /// <summary>Injected by the bootstrap, the way <see cref="MapScreen"/> takes its services.</summary>
    public void SetBestiary(BestiaryService service)
    {
        _bestiary = service;
        MarkDirty();
    }

    protected override void BuildShell(PanelContainer shell)
    {
        UiTheme.ApplyScreenInset(shell);

        MarginContainer margin = UiTheme.Padding(12);
        shell.AddChild(margin);

        var column = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        column.AddThemeConstantOverride("separation", UiTheme.SpaceSm);
        margin.AddChild(column);

        _tabs = new UiTabs();
        foreach ((BestiaryCategory _, string key) in TabDefs)
        {
            _tabs.Add(Loc.T(key));
        }

        _tabs.TabChanged += index =>
        {
            _activeTab = TabDefs[index].Category;
            MarkDirty();
        };
        column.AddChild(_tabs);

        (ScrollContainer scroll, _list) = UiTheme.ScrollList();
        column.AddChild(scroll);
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
        UiTheme.ClearChildren(_list);

        if (_bestiary == null)
        {
            _list.AddChild(UiTheme.Body(Loc.T("bestiary.empty"), UiTheme.Dim));
            return;
        }

        // A codex page opens as you fill it (37.5E): a progress meter over cards, each entry sealed,
        // part-written or complete. The staging was always in the data - BestiaryStage has had three
        // values since 34G - and the old flat list spent it on three differently-worded text lines.
        var head = new VBoxContainer();
        head.AddThemeConstantOverride("separation", 2);
        head.AddChild(UiTheme.Title(Loc.TF(
            "bestiary.progress", _bestiary.DiscoveredCount, BestiaryDatabase.All.Count)));

        ProgressBar meter = UiTheme.Bar(UiTheme.Accent);
        meter.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        meter.CustomMinimumSize = new Vector2(0f, 4f);
        meter.Value = BestiaryDatabase.All.Count == 0
            ? 0d
            : _bestiary.DiscoveredCount / (double)BestiaryDatabase.All.Count;
        head.AddChild(meter);
        _list.AddChild(head);

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

        foreach (BestiaryEntryResource entry in shown)
        {
            AddEntry(entry);
        }
    }

    private void AddEntry(BestiaryEntryResource entry)
    {
        BestiaryService bestiary = _bestiary!;
        BestiaryStage stage = bestiary.StageOf(entry);

        // Sealed page. No name and no lore leak - the blank entry is the hook, and the spine being
        // the disabled colour is what makes a column of them read as "not yet" rather than "broken".
        if (stage == BestiaryStage.Unseen)
        {
            PanelContainer sealedCard = UiTheme.Card(UiTheme.Disabled);
            MarginContainer sealedPad = UiTheme.Padding(UiTheme.SpaceXs);
            sealedPad.AddChild(UiTheme.Body(Loc.T("bestiary.unknown"), UiTheme.Disabled));
            sealedCard.AddChild(sealedPad);
            _list.AddChild(sealedCard);
            return;
        }

        int kills = bestiary.KillsOf(entry.Id);
        bool known = stage == BestiaryStage.Known;

        PanelContainer card = UiTheme.Card(known ? UiTheme.Accent : UiTheme.Dim);
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 2);

        var titleRow = new HBoxContainer();
        titleRow.AddThemeConstantOverride("separation", UiTheme.SpaceSm);

        Label name = UiTheme.Body(NameOf(entry), known ? UiTheme.Accent : UiTheme.Text);
        UiTheme.ApplyType(name, UiTheme.FontRole.Display, UiTheme.HeaderFontSize);
        name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        titleRow.AddChild(name);

        PanelContainer killChip = UiTheme.Chip(Loc.TF("bestiary.kills", kills), UiTheme.Dim);
        killChip.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        titleRow.AddChild(killChip);

        int ashen = bestiary.AshenKillsOf(entry.Id);
        if (ashen > 0)
        {
            PanelContainer ashChip = UiTheme.Chip(Loc.TF("bestiary.corrupted", ashen), UiTheme.CorruptionText);
            ashChip.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            titleRow.AddChild(ashChip);
        }

        col.AddChild(titleRow);

        if (known)
        {
            // The page is written: the lore reads as a book, in the book face.
            col.AddChild(UiTheme.Prose(Loc.T(entry.LoreKey)));
        }
        else
        {
            // Sighted: say there is more to learn, and show how much of the page is filled. The bar
            // turns "3 more to fill the page" into something readable without counting.
            col.AddChild(UiTheme.Caption(
                Loc.TF("bestiary.sighted", entry.KillsToKnow - kills), UiTheme.Dim));

            int needed = Mathf.Max(1, entry.KillsToKnow);
            ProgressBar toward = UiTheme.Bar(UiTheme.Dim);
            toward.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            toward.CustomMinimumSize = new Vector2(0f, 3f);
            toward.Value = Mathf.Clamp(kills / (double)needed, 0d, 1d);
            col.AddChild(toward);
        }

        MarginContainer pad = UiTheme.Padding(UiTheme.SpaceXs);
        pad.AddChild(col);
        card.AddChild(pad);
        _list.AddChild(card);
    }

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
