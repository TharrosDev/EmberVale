using System.Collections.Generic;
using Embervale.Core;
using Embervale.Core.Diagnostics;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Player;
using Embervale.Save;
using Godot;

namespace Embervale.Companions;

/// <summary>
/// The party: who has been recruited, what order each companion is under, and the live actors that
/// realise them (Phase 32A). It is the single entry point for recruiting — dialogue, quests and the
/// dev console all call <see cref="Recruit"/> with an id and never build an actor themselves.
///
/// It persists (<see cref="ISaveable"/>) the way <see cref="PersistentSpawnDirector"/> does, and for
/// the same reason: the <see cref="SaveManager"/> alone restores the components of actors that are
/// already in the scene, and a freshly-built world contains no companions at all. <see cref="Load"/>
/// therefore reconciles — despawning companions the save doesn't have and re-spawning the ones it
/// does, whose own components (stats, status effects) then restore themselves through the manager's
/// in-flight-load hook because each actor carries a stable <c>PersistentId</c>.
///
/// It also keeps the party from being left behind: a companion that ends up absurdly far from the
/// player (a load, a fast travel, a region transition) is snapped to its formation slot rather than
/// asked to walk back across the world.
/// </summary>
[GlobalClass]
public partial class CompanionRoster : Node, ISaveable
{
    public string SaveId => "companions";

    /// <summary>How many companions may be in the party at once (LORE: a small band, not an army).</summary>
    [Export] public int MaxPartySize { get; set; } = 3;

    /// <summary>Distance from the player past which a following companion is teleported to its slot
    /// instead of walking — the world moved under it, it didn't wander off.</summary>
    [Export] public float CatchUpDistance { get; set; } = 35f;

    /// <summary>Seconds between catch-up checks.</summary>
    [Export] public float CatchUpInterval { get; set; } = 1f;

    private readonly Dictionary<string, CompanionEntity> _active = new();
    private readonly Dictionary<string, CompanionStance> _stances = new();

    // Loyalty is tracked for every companion the player has ever affected, recruited or not — a
    // companion you dismissed has not forgotten how you treated them.
    private readonly Dictionary<string, int> _loyalty = new();
    private double _catchUpTimer;

    /// <summary>The ids of every companion currently in the party.</summary>
    public IReadOnlyCollection<string> RecruitedIds => _active.Keys;

    public int Count => _active.Count;

    public override void _EnterTree()
    {
        ServiceLocator.Instance?.Register(this);
        SaveManager.Instance?.Register(this);
    }

    public override void _ExitTree()
    {
        SaveManager.Instance?.Unregister(this);
        ServiceLocator.Instance?.Unregister(this);
    }

    public override void _Process(double delta)
    {
        // The quick command (32B): one key cycles the whole band's standing order. Guarded on menus
        // so cycling orders never fires from under an open panel.
        if (!UiState.MenuOpen && GameManager.Instance is { IsPlaying: true } &&
            Godot.Input.IsActionJustPressed(GameInput.CompanionCommand) && _active.Count > 0)
        {
            CycleOrder();
        }

        _catchUpTimer -= delta;
        if (_catchUpTimer > 0d)
        {
            return;
        }

        _catchUpTimer = CatchUpInterval;
        StandDownSpentOrders();
        CatchUpStragglers(CatchUpDistance);
    }

    /// <summary>
    /// Advances the party's standing order one step (follow → hold → engage → follow) and applies it
    /// to every companion, so the band acts as one under a single key. Returns the new order.
    /// </summary>
    public CompanionStance CycleOrder()
    {
        CompanionStance next = CompanionOrders.Next(PartyOrder());
        foreach (string id in new List<string>(_active.Keys))
        {
            SetStance(id, next);
        }

        EventBus.Instance?.Publish(new CompanionOrderIssuedEvent(next));
        return next;
    }

    /// <summary>The order the party is under — the first companion's, since the quick command moves
    /// them together (per-companion orders are a later pass).</summary>
    public CompanionStance PartyOrder()
    {
        foreach (string id in _active.Keys)
        {
            return StanceOf(id);
        }

        return CompanionStance.Follow;
    }

    /// <summary>Returns companions to formation once their engage order has nothing left to fight,
    /// so an "attack" command is a burst of aggression rather than a permanent posture.</summary>
    private void StandDownSpentOrders()
    {
        foreach (string id in new List<string>(_active.Keys))
        {
            if (StanceOf(id) == CompanionStance.Engage && TryGet(id, out CompanionEntity companion) &&
                companion.GetComponent<CompanionAIComponent>()?.EngageOrderSpent == true)
            {
                SetStance(id, CompanionStance.Follow);
            }
        }
    }

    public bool IsRecruited(string companionId) => _active.ContainsKey(companionId);

    public bool TryGet(string companionId, out CompanionEntity companion)
    {
        bool found = _active.TryGetValue(companionId, out CompanionEntity? entity) &&
            IsInstanceValid(entity);
        companion = entity!;
        return found;
    }

    /// <summary>The order a companion is under (defaults to <see cref="CompanionStance.Follow"/>).</summary>
    public CompanionStance StanceOf(string companionId) =>
        _stances.TryGetValue(companionId, out CompanionStance stance) ? stance : CompanionStance.Follow;

    /// <summary>
    /// Recruits a companion by id: builds the actor, drops it into its formation slot behind the
    /// player and announces it. A no-op (false) when the id is unknown, it is already in the party, or
    /// the party is full.
    /// </summary>
    public bool Recruit(string companionId)
    {
        if (string.IsNullOrEmpty(companionId) || _active.ContainsKey(companionId))
        {
            return false;
        }

        if (_active.Count >= MaxPartySize)
        {
            Log.Warn($"Cannot recruit '{companionId}': the party is full ({MaxPartySize}).");
            return false;
        }

        return RecruitAt(companionId, SlotPosition(_active.Count), yawDegrees: null);
    }

    /// <summary>
    /// Recruits a companion directly into a world position — the load path (Phase 32D), which must
    /// put them back exactly where they were standing rather than teleporting the whole band to the
    /// player's heels on every reload. <paramref name="yawDegrees"/> null keeps the built facing.
    /// </summary>
    public bool RecruitAt(string companionId, Vector3 position, float? yawDegrees) =>
        RecruitAt(companionId, position, yawDegrees, announce: true);

    /// <summary>
    /// The shared body. <paramref name="announce"/> is false for the restore half of
    /// <see cref="Load"/>: rebuilding a companion the save already had is not a recruitment, and
    /// <see cref="CompanionPartyReconcile"/> exists precisely so a load does not "re-run its recruit
    /// announcement". It only got half of that — the *Keep* set is spared, but a load into a freshly
    /// built world has no live companions at all, so the entire party lands in *Recruit* and every
    /// reload toasted "Kael joins you" as though the player had just met him.
    ///
    /// Suppressing it is safe because nothing else leans on the event to survive a load:
    /// <see cref="CompanionRecruiterComponent"/> and <c>PartyWidget</c> both re-derive from
    /// <c>GameLoadedEvent</c> instead, having already learned that events fired before their cell
    /// streamed in cannot be trusted.
    /// </summary>
    private bool RecruitAt(string companionId, Vector3 position, float? yawDegrees, bool announce)
    {
        if (string.IsNullOrEmpty(companionId) || _active.ContainsKey(companionId))
        {
            return false;
        }

        if (_active.Count >= MaxPartySize)
        {
            Log.Warn($"Cannot recruit '{companionId}': the party is full ({MaxPartySize}).");
            return false;
        }

        // Lifted onto the ground for the same reason the Keep branch of the load is: a saved
        // position from before a landform edit, or a slot derived from a player further up a slope,
        // otherwise builds the companion inside the hillside.
        CompanionEntity? companion = CompanionRegistry.Create(companionId, World.WorldGround.Lift(position));
        if (companion == null)
        {
            return false;
        }

        if (yawDegrees is { } yaw)
        {
            companion.RotationDegrees = new Vector3(companion.RotationDegrees.X, yaw, companion.RotationDegrees.Z);
        }

        GetParent().AddChild(companion);
        _active[companionId] = companion;
        _stances.TryAdd(companionId, CompanionStance.Follow);
        ApplyStance(companionId);
        AssignSlots();

        // Losing the actor (a cell unload, an errant free) must not leave a ghost in the party — but
        // only drop the key if THIS actor is still the tracked one, so a re-recruit isn't undone by
        // the previous instance's deferred TreeExited.
        companion.TreeExited += () =>
        {
            if (_active.TryGetValue(companionId, out CompanionEntity? current) && ReferenceEquals(current, companion))
            {
                _active.Remove(companionId);
            }
        };

        if (announce)
        {
            EventBus.Instance?.Publish(new CompanionRecruitedEvent(companionId, companion.NameKey, companion));
            Log.Info($"Companion '{companionId}' joined the party.");
        }
        else
        {
            // Still logged, and worded for what it is — the boot log claiming a companion "joined
            // the party" on every load was itself misleading to read.
            Log.Info($"Companion '{companionId}' restored to the party.");
        }

        return true;
    }

    /// <summary>Dismisses a companion: frees the actor and drops it from the party. Its stance is
    /// forgotten, so re-recruiting starts it following again.</summary>
    public bool Dismiss(string companionId)
    {
        if (!_active.TryGetValue(companionId, out CompanionEntity? companion))
        {
            return false;
        }

        string nameKey = IsInstanceValid(companion) ? companion.NameKey : string.Empty;
        _active.Remove(companionId);
        _stances.Remove(companionId);
        if (IsInstanceValid(companion))
        {
            companion.QueueFree();
        }

        AssignSlots();
        EventBus.Instance?.Publish(new CompanionDismissedEvent(companionId, nameKey));
        Log.Info($"Companion '{companionId}' left the party.");
        return true;
    }

    /// <summary>
    /// Puts a companion under a new standing order. Holding anchors it where it currently stands;
    /// following returns it to formation. Returns false when the companion isn't in the party.
    /// </summary>
    public bool SetStance(string companionId, CompanionStance stance)
    {
        if (!_active.ContainsKey(companionId))
        {
            return false;
        }

        _stances[companionId] = stance;
        ApplyStance(companionId);
        EventBus.Instance?.Publish(new CompanionStanceChangedEvent(companionId, stance));
        return true;
    }

    // --- Loyalty (32C) -------------------------------------------------------

    /// <summary>
    /// A companion's loyalty (0-100). A companion never met answers with their authored
    /// <see cref="CompanionResource.StartingLoyalty"/>, so a first meeting starts where the writer
    /// said it does rather than at zero.
    /// </summary>
    public int LoyaltyOf(string companionId)
    {
        if (_loyalty.TryGetValue(companionId, out int value))
        {
            return value;
        }

        return CompanionLoyalty.Clamp(CompanionDatabase.Get(companionId)?.StartingLoyalty ?? 0);
    }

    /// <summary>The band a companion's loyalty falls into - what banter, abilities and (Phase 44)
    /// ending flags key off.</summary>
    public LoyaltyTier TierOf(string companionId) => CompanionLoyalty.Of(LoyaltyOf(companionId));

    /// <summary>Shifts a companion's loyalty and announces it; returns the new value. Crossing a tier
    /// boundary also raises <see cref="CompanionLoyaltyTierChangedEvent"/>, which is what the combat
    /// bonus and the tier-gated dialogue react to.</summary>
    public int AddLoyalty(string companionId, int delta)
    {
        if (string.IsNullOrEmpty(companionId) || delta == 0)
        {
            return LoyaltyOf(companionId);
        }

        return SetLoyalty(companionId, LoyaltyOf(companionId) + delta);
    }

    /// <summary>Sets a companion's loyalty outright (clamped). Returns the stored value.</summary>
    public int SetLoyalty(string companionId, int value)
    {
        if (string.IsNullOrEmpty(companionId))
        {
            return 0;
        }

        LoyaltyTier before = TierOf(companionId);
        int clamped = CompanionLoyalty.Clamp(value);
        _loyalty[companionId] = clamped;

        LoyaltyTier after = CompanionLoyalty.Of(clamped);
        EventBus.Instance?.Publish(new CompanionLoyaltyChangedEvent(companionId, clamped, after));
        if (after != before)
        {
            EventBus.Instance?.Publish(new CompanionLoyaltyTierChangedEvent(companionId, after, after > before));
        }

        return clamped;
    }

    // --- Persistence ---------------------------------------------------------

    public Godot.Collections.Dictionary Save()
    {
        var party = new Godot.Collections.Array();
        foreach (KeyValuePair<string, CompanionEntity> kv in _active)
        {
            if (!IsInstanceValid(kv.Value))
            {
                continue;
            }

            // Position travels with the entry: a party that respawns at the player's heels every load
            // silently undoes a Hold order and teleports companions across whatever they were doing.
            Vector3 p = kv.Value.GlobalPosition;
            party.Add(new Godot.Collections.Dictionary
            {
                ["id"] = kv.Key,
                ["stance"] = (int)StanceOf(kv.Key),
                ["x"] = p.X,
                ["y"] = p.Y,
                ["z"] = p.Z,
                ["yaw"] = kv.Value.RotationDegrees.Y,
            });
        }

        // Loyalty persists for everyone it has been recorded for, including companions not currently
        // travelling with the player - dismissing someone must not wipe the history between you.
        var loyalty = new Godot.Collections.Dictionary();
        foreach (KeyValuePair<string, int> kv in _loyalty)
        {
            loyalty[kv.Key] = kv.Value;
        }

        return new Godot.Collections.Dictionary { ["party"] = party, ["loyalty"] = loyalty };
    }

    public void Load(Godot.Collections.Dictionary data)
    {
        _loyalty.Clear();
        if (data.TryGetValue("loyalty", out Variant loyaltyVariant) &&
            loyaltyVariant.VariantType == Variant.Type.Dictionary)
        {
            Godot.Collections.Dictionary saved = loyaltyVariant.AsGodotDictionary();
            foreach (Variant key in saved.Keys)
            {
                _loyalty[key.AsString()] = CompanionLoyalty.Clamp(saved[key].AsInt32());
            }
        }

        if (!data.TryGetValue("party", out Variant partyVariant) ||
            partyVariant.VariantType != Variant.Type.Array)
        {
            return;
        }

        // The desired party, from the save.
        var desired = new Dictionary<string, (CompanionStance Stance, Vector3 Position, float Yaw)>();
        foreach (Variant element in partyVariant.AsGodotArray())
        {
            if (element.VariantType != Variant.Type.Dictionary)
            {
                continue;
            }

            var entry = element.AsGodotDictionary();
            string id = entry.TryGetValue("id", out Variant idV) ? idV.AsString() : string.Empty;
            if (string.IsNullOrEmpty(id))
            {
                continue;
            }

            CompanionStance stance = entry.TryGetValue("stance", out Variant stanceV)
                ? (CompanionStance)stanceV.AsInt32()
                : CompanionStance.Follow;
            var position = new Vector3(
                entry.TryGetValue("x", out Variant x) ? x.AsSingle() : 0f,
                entry.TryGetValue("y", out Variant y) ? y.AsSingle() : 0f,
                entry.TryGetValue("z", out Variant z) ? z.AsSingle() : 0f);
            float yaw = entry.TryGetValue("yaw", out Variant yawV) ? yawV.AsSingle() : 0f;
            desired[id] = (stance, position, yaw);
        }

        // A load is a reconcile, not a rebuild: a companion already standing in the world keeps its
        // actor (and its live component state) and is simply moved.
        CompanionReconcilePlan plan = CompanionPartyReconcile.Plan(_active.Keys, desired.Keys);

        foreach (string id in plan.Dismiss)
        {
            Dismiss(id);
        }

        foreach (string id in plan.Recruit)
        {
            (CompanionStance _, Vector3 position, float yaw) = desired[id];
            RecruitAt(id, position, yaw, announce: false);
        }

        foreach (string id in plan.Keep)
        {
            (CompanionStance _, Vector3 position, float yaw) = desired[id];
            if (TryGet(id, out CompanionEntity survivor))
            {
                // Lifted, not snapped: a companion saved on a terrace or a rooftop keeps that height,
                // while one whose saved Y predates a landform edit comes back out of the hillside.
                survivor.GlobalPosition = World.WorldGround.Lift(position);
                survivor.RotationDegrees = new Vector3(survivor.RotationDegrees.X, yaw, survivor.RotationDegrees.Z);
                survivor.Velocity = Vector3.Zero;
            }
        }

        // Stances last, so a restored Hold anchors at the restored position rather than the old one.
        foreach (KeyValuePair<string, (CompanionStance Stance, Vector3 Position, float Yaw)> kv in desired)
        {
            if (_active.ContainsKey(kv.Key))
            {
                _stances[kv.Key] = kv.Value.Stance;
                ApplyStance(kv.Key);
            }
        }

        // Only followers are pulled to the player after the load overlay repositions them; a
        // companion left holding a spot stays where the player told it to stand.
        _catchUpTimer = 0d;
    }

    // --- Internals -----------------------------------------------------------

    /// <summary>Hands each companion its formation index, in recruitment order, so party members keep
    /// distinct slots and don't pile onto one shoulder.</summary>
    private void AssignSlots()
    {
        int index = 0;
        foreach (CompanionEntity companion in _active.Values)
        {
            if (IsInstanceValid(companion) && companion.GetComponent<CompanionAIComponent>() is { } ai)
            {
                ai.SlotIndex = index;
            }

            index++;
        }
    }

    private void ApplyStance(string companionId)
    {
        if (TryGet(companionId, out CompanionEntity companion) &&
            companion.GetComponent<CompanionAIComponent>() is { } ai)
        {
            ai.SetStance(StanceOf(companionId));
        }
    }

    /// <summary>
    /// Pulls the whole following party into formation immediately, regardless of distance — the hard
    /// cut after the world moves under them (a region transition, a fast travel). The bootstrap calls
    /// this once the player has been placed at the destination.
    /// </summary>
    public void RegroupNow()
    {
        CatchUpStragglers(minimumDistance: 0f);
    }

    /// <summary>Teleports following companions that the world moved away from (load, fast travel,
    /// region transition) back into formation. A downed companion is left where it fell.</summary>
    private void CatchUpStragglers(float minimumDistance)
    {
        if (_active.Count == 0 || GetPlayer() is not { } player)
        {
            return;
        }

        int index = 0;
        foreach (CompanionEntity companion in _active.Values)
        {
            int slot = index++;
            if (!IsInstanceValid(companion) || StanceOf(companion.CompanionId) != CompanionStance.Follow)
            {
                continue;
            }

            if (companion.GetComponent<CompanionAIComponent>()?.State == CompanionState.Downed)
            {
                continue;
            }

            if (companion.GlobalPosition.DistanceTo(player.GlobalPosition) > minimumDistance)
            {
                // On the ground, not on the player's plane. CompanionFormation.Slot copies the
                // player's Y (it is Godot-free and has no terrain to ask), so a catch-up on a slope
                // put the band four metres behind and a metre inside the hill.
                companion.GlobalPosition = World.WorldGround.OnGround(CompanionFormation.Slot(
                    player.GlobalPosition, -player.GlobalTransform.Basis.Z, slot, FollowDistanceOf(companion)));
                companion.Velocity = Vector3.Zero;
            }
        }
    }

    private static float FollowDistanceOf(CompanionEntity companion) =>
        companion.GetComponent<CompanionAIComponent>()?.FollowDistance ?? 3f;

    /// <summary>The formation slot a newly-recruited companion appears in — beside the player, or the
    /// world origin when there is somehow no player yet.</summary>
    private Vector3 SlotPosition(int index)
    {
        if (GetPlayer() is not { } player)
        {
            return Vector3.Zero;
        }

        return World.WorldGround.OnGround(
            CompanionFormation.Slot(player.GlobalPosition, -player.GlobalTransform.Basis.Z, index, 3f));
    }

    private static PlayerCharacter? GetPlayer()
    {
        if (ServiceLocator.Instance != null && ServiceLocator.Instance.TryGet(out PlayerCharacter player) &&
            IsInstanceValid(player))
        {
            return player;
        }

        return null;
    }
}
