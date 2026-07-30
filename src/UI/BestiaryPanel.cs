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
    /// <summary>Screen-edge gutter, matching the character screen.</summary>
    private const float ScreenMargin = 70f;

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
        shell.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        shell.OffsetLeft = ScreenMargin;
        shell.OffsetTop = ScreenMargin;
        shell.OffsetRight = -ScreenMargin;
        shell.OffsetBottom = -ScreenMargin;

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

        _list.AddChild(UiTheme.Header(Loc.TF(
            "bestiary.progress", _bestiary.DiscoveredCount, BestiaryDatabase.All.Count)));

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

        if (stage == BestiaryStage.Unseen)
        {
            // Never killed one: the blank page is the hook, so no name and no lore leak.
            _list.AddChild(UiTheme.Body(Loc.T("bestiary.unknown"), UiTheme.Dim));
            return;
        }

        int kills = bestiary.KillsOf(entry.Id);
        _list.AddChild(UiTheme.Header(Loc.TF("bestiary.entry", NameOf(entry), kills)));

        if (stage == BestiaryStage.Known)
        {
            Label lore = UiTheme.Body(Loc.T(entry.LoreKey));
            lore.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            _list.AddChild(lore);
        }
        else
        {
            // Sighted: tell them there is more to learn, and exactly how much more.
            _list.AddChild(UiTheme.Caption(
                Loc.TF("bestiary.sighted", entry.KillsToKnow - kills), UiTheme.Dim));
        }

        int ashen = bestiary.AshenKillsOf(entry.Id);
        if (ashen > 0)
        {
            _list.AddChild(UiTheme.Caption(Loc.TF("bestiary.corrupted", ashen), UiTheme.Corruption));
        }
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
