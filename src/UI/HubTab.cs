namespace Embervale.UI;

/// <summary>
/// The five screens of the in-game hub, in the fixed order the <see cref="HubStrip"/> draws them.
/// A panel joins the hub by overriding <c>UiPanel.Hub</c> with its own tab; the order here is the
/// order the shoulder buttons walk, and it never changes with what is open.
/// </summary>
public enum HubTab
{
    /// <summary>The character sheet and pack (<c>InventoryPanel</c>).</summary>
    Character = 0,

    Spellbook = 1,

    /// <summary>The quest journal (<c>QuestLogPanel</c>).</summary>
    Journal = 2,

    Map = 3,

    Bestiary = 4,
}

/// <summary>
/// Whoever holds the hub's panels: the node the five are parented to. A panel stepping to its
/// neighbour asks its parent for it, so no panel holds another and the UI layer needs no reference
/// to the composition root that built it.
/// </summary>
public interface IHubHost
{
    /// <summary>The panel behind <paramref name="tab"/>, or null when this session did not build one.</summary>
    UiPanel? HubPanel(HubTab tab);
}
