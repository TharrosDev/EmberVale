using System.Collections.Generic;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Entities;
using Embervale.Factions;
using Embervale.Interaction;
using Embervale.Localization;
using Embervale.Player;
using Embervale.World;
using Godot;

namespace Embervale.Economy;

/// <summary>Raised when the player opens a shop. Carries the resolved <see cref="ShopResource"/> so
/// the UI never has to look it up — the same "publish what you resolved" shape
/// <c>StorageOpenedEvent</c> and <c>CraftingStationOpenedEvent</c> use.</summary>
public readonly record struct ShopOpenedEvent(IEntity Player, ShopResource Shop) : IGameEvent;

/// <summary>Raised when the shop window closes.</summary>
public readonly record struct ShopClosedEvent(IEntity Player) : IGameEvent;

/// <summary>Raised when an appraiser is asked what the player's goods are worth (Phase 38P2).
/// Answered by <c>AppraisalPanel</c>, the same event-driven seam <see cref="ShopOpenedEvent"/> and
/// <c>StorageOpenedEvent</c> use — so the service publishes and knows nothing about the UI.</summary>
public readonly record struct AppraisalOpenedEvent(IEntity Player, string AppraiserName) : IGameEvent;

/// <summary>Raised when the caravan board is read (Phase 38Q2). Answered by <c>ContractBoardPanel</c>.
/// It carries no postings: the board is derived from the day by <see cref="ContractRules"/>, so the
/// panel asks the clock rather than being handed a snapshot that could go stale behind it.</summary>
public readonly record struct ContractBoardOpenedEvent(
    IEntity Player, string BoardName, int Slots, int RotationDays) : IGameEvent;

/// <summary>
/// Raised when a throw at a gambling house settles (Phase 38R2). <paramref name="Gold"/> is the payout
/// on a win and the stake on a loss, so the toast can name a number either way.
///
/// ⚠️ <b>This event is not decoration.</b> A wager opens no window, so without a line of feedback the
/// only sign of a loss is the gold counter falling — which is indistinguishable from a bug, and is the
/// state a player would report as one. It is the smallest thing that makes a press readable.
/// </summary>
public readonly record struct WagerSettledEvent(
    IEntity Player, string HouseName, bool Won, int Gold) : IGameEvent;

/// <summary>
/// Raised when enough goods have reached a shocked settlement to break its shortage (Phase 38T).
///
/// ⚠️ <b>Same reason as the wager above.</b> The last cart of a haul looks exactly like the one before
/// it — the sale completes, the gold arrives — and the only visible difference is that the prices the
/// player was hauling *towards* have just fallen back to normal. Without a line saying so, the reward
/// for the whole run reads as the shortage having quietly expired on its own.
/// </summary>
public readonly record struct SupplyShockRelievedEvent(string CellId, string Tag) : IGameEvent;

/// <summary>
/// A merchant the player can trade with (Phase 38A). Authored on an entity with a collider (the
/// interact raycast needs one) and pointed at a <see cref="ShopResource"/> by id — the same
/// "declare it in the scene, resolve it at runtime" shape <c>PropertyStorageComponent</c> and
/// <c>TravelNodeComponent</c> use.
///
/// ⚠️ <b>An entity gets one interactable.</b> <c>EntityNode.GetComponent&lt;T&gt;</c> returns the
/// <em>first</em> child match, so a vendor component sitting behind a <c>DialogueComponent</c> on the
/// same actor never fires. That is why the three Ember Crown stub vendors are untouched here:
/// whether trade replaces their conversation or hangs off a dialogue choice is Phase 38E's call.
/// Until then a shop is opened with <c>shop &lt;id&gt;</c> in the F1 console.
///
/// ⚠️ <b><c>ContentValidator</c> does not scan <c>.tscn</c> files</b>, so a mistyped
/// <see cref="ShopId"/> yields <em>no prompt at all</em> rather than an error — the same trap
/// <c>PropertyStorageComponent.PropertyId</c> carries. If a merchant is silently unusable in game,
/// check this field first.
/// </summary>
[GlobalClass]
public partial class VendorComponent : InteractableComponent
{
    /// <summary>Which <see cref="ShopResource"/> this merchant trades from (a <c>shop.*</c> id).</summary>
    [Export] public string ShopId { get; set; } = string.Empty;

    public override string Prompt
    {
        get
        {
            // Nothing sensible to say about an id that resolves to nothing; the validator and the log
            // carry authoring faults, the prompt stays silent rather than lying.
            if (ShopDatabase.Get(ShopId) is not { } shop)
            {
                return string.Empty;
            }

            // An absent merchant has no prompt at all — he is not standing there to be spoken to, and
            // the toggle below has already hidden him. This is the one refusal with nothing to name.
            if (!IsInTown(shop))
            {
                return string.Empty;
            }

            string name = Loc.T(shop.NameKey);
            if (!WillTrade(shop))
            {
                return Loc.TF("shop.prompt_hostile", name);
            }

            // 38J: a closed shop says when it opens. "Closed" alone leaves the player standing at a
            // stall with nothing to do about it, while an hour is an instruction — sleep at the inn,
            // or go and do something until morning.
            return IsOpenNow(shop)
                ? Loc.TF("shop.prompt_trade", name)
                : Loc.TF("shop.prompt_closed", name, Hours.NextOpen(shop));
        }
    }

    public override bool Interact(IEntity instigator)
    {
        if (ShopDatabase.Get(ShopId) is not { } shop ||
            !IsInTown(shop) || !WillTrade(shop) || !IsOpenNow(shop))
        {
            return false; // the prompt has already said why
        }

        EventBus.Instance?.Publish(new ShopOpenedEvent(instigator, shop));
        return true;
    }

    protected override void OnInitialize()
    {
        // The hourly tick ScheduleComponent already rides — no new event, no _Process, and the day
        // rolls over inside it, so arriving and leaving costs nothing per frame.
        EventBus.Instance?.Subscribe<TimeOfDayChangedEvent>(OnTimeChanged);
        ApplyPresence();
    }

    protected override void OnTeardown()
    {
        EventBus.Instance?.Unsubscribe<TimeOfDayChangedEvent>(OnTimeChanged);
    }

    private void OnTimeChanged(TimeOfDayChangedEvent e) => ApplyPresence();

    /// <summary>
    /// Shows or hides the merchant for today (Phase 38J). Presence is a pure function of
    /// <c>WorldClock.Day</c>, so there is nothing to save and nothing that can drift out of step with a
    /// reloaded clock — the cheapest possible version of a merchant who comes and goes.
    ///
    /// ⚠️ <b>Hiding a <c>Node3D</c> does not disable its collision.</b> A hidden trader still stops the
    /// interact raycast and the player's own body, so the square would carry an invisible wall that
    /// reads as a physics bug rather than as a merchant being away. The collider's layer is zeroed and
    /// restored alongside the visibility, and both live in this one function so neither can be done
    /// without the other.
    /// </summary>
    private void ApplyPresence()
    {
        if (Entity is not { } owner || ShopDatabase.Get(ShopId) is not { } shop)
        {
            return;
        }

        bool here = IsInTown(shop);
        if (owner.Body.Visible == here)
        {
            return; // nothing changed; do not re-walk the colliders every hour
        }

        owner.Body.Visible = here;

        foreach (Node child in owner.Body.GetChildren())
        {
            if (child is not CollisionObject3D collider)
            {
                continue;
            }

            // Cached on the first hide so a scene that authors its own layers is restored to them
            // rather than to a constant. A merchant who came back on the wrong collision layer would
            // be a ghost the raycast passes straight through — the same failure, inverted.
            if (!here)
            {
                _restoreLayer.TryAdd(collider, collider.CollisionLayer);
                collider.CollisionLayer = 0;
            }
            else if (_restoreLayer.TryGetValue(collider, out uint layer))
            {
                collider.CollisionLayer = layer;
            }
        }
    }

    private readonly Dictionary<CollisionObject3D, uint> _restoreLayer = new();

    /// <summary>Whether the merchant is in town today (Phase 38J). A resident shop
    /// (<c>VisitEveryDays = 0</c>) always is, and so is any shop with no clock to read.</summary>
    private static bool IsInTown(ShopResource shop) =>
        Clock() is not { } clock || ShopHours.IsInTown(clock.Day, shop.VisitEveryDays, shop.VisitDayOffset);

    /// <summary>Whether the shop is trading at this hour. ⚠️ No clock means <b>open</b>, the same
    /// inverted fail-safe an unresolvable standing gets: a half-built world trades normally rather than
    /// refusing everywhere.</summary>
    private static bool IsOpenNow(ShopResource shop) =>
        Clock() is not { } clock || ShopHours.IsOpenAt(clock.Hour, shop.OpenHour, shop.CloseHour);

    private static WorldClock? Clock() =>
        ServiceLocator.Instance is { } locator && locator.TryGet(out WorldClock clock) ? clock : null;

    /// <summary>The hour strings a prompt needs, formatted the one way <c>WorldClock.Clock()</c>
    /// formats them so the shop and the debug overlay cannot disagree about what 8 o'clock looks
    /// like.</summary>
    private static class Hours
    {
        public static string NextOpen(ShopResource shop)
        {
            int hour = Clock() is { } clock
                ? ShopHours.NextOpenHour(clock.Hour, shop.OpenHour, shop.CloseHour)
                : shop.OpenHour;

            return $"{hour:00}:00";
        }
    }

    /// <summary>
    /// Whether the merchant deals with the player at all (Phase 38C). Read by both the prompt and the
    /// interaction, so a refusal cannot say one thing and the press do another — the same rule
    /// <c>PropertyClaim.Resolve</c> enforces for a deed.
    ///
    /// Hostility is <see cref="ReputationComponent.IsHostile"/>, the game's one existing reputation
    /// verb, which already keys off each faction's authored <c>HostileThreshold</c> — the Frostfang
    /// clans tolerate someone the villagers would turn away, and that is content, not a second
    /// threshold to invent here.
    ///
    /// ⚠️ <b>The default is inverted from the AI's.</b> <c>EnemyAIComponent.PlayerIsTarget</c> treats a
    /// missing <c>ReputationComponent</c> as hostile, which is the right fail-safe for a creature
    /// deciding whether to attack. For a shop it would mean every merchant in a half-built world
    /// refusing to trade, so an unresolvable standing trades normally.
    /// </summary>
    private static bool WillTrade(ShopResource shop)
    {
        if (string.IsNullOrEmpty(shop.FactionId))
        {
            return true;
        }

        return Player()?.GetComponent<ReputationComponent>() is not { } reputation ||
            !reputation.IsHostile(shop.FactionId);
    }

    private static PlayerCharacter? Player() =>
        ServiceLocator.Instance is { } locator && locator.TryGet(out PlayerCharacter player) ? player : null;
}
