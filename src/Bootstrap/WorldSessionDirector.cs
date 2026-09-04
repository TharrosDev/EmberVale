using System.Collections.Generic;
using Embervale.Companions;
using Embervale.Core;
using Embervale.Core.Diagnostics;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Economy;
using Embervale.Items;
using Embervale.Player;
using Embervale.Save;
using Embervale.World;
using Godot;

namespace Embervale.Bootstrap;

/// <summary>
/// The loaded world: what it is made of, and how the player moves between regions.
///
/// <para>It builds the world-lifetime services under <see cref="GameSession.World"/> — the
/// environment, weather, sky, the encounter and world-event directors, the region streamer and the
/// portals a region puts in the player's way — and it owns the two routes that change which region
/// is loaded: a portal transition and a fast-travel jump, both converging on one hard load.</para>
///
/// <para><b>A region transition does not destroy the world scope.</b> The streamer reconfigures in
/// place: unload the old cells, configure the new region, rebuild portals and safe zones, publish
/// the change. Weather and the clock are deliberately untouched, so arrival respects the time of
/// day the player left in.</para>
/// </summary>
public sealed partial class WorldSessionDirector : Node
{
    private readonly List<Entities.Entity> _portals = new();

    private RegionStreamer? _streamer;
    private MapService? _mapService;
    private WorldEnvironmentBuilder.Result _environment;

    public GameSession Session { get; init; } = null!;

    /// <summary>The streamer, or null before <see cref="BuildStreamer"/>. The loading gate reads it
    /// to know whether the region is whole.</summary>
    public RegionStreamer? Streamer => _streamer;

    public override void _EnterTree()
    {
        EventBus.Instance?.Subscribe<RegionTransitionRequestedEvent>(OnRegionTransitionRequested);
        EventBus.Instance?.Subscribe<FastTravelRequestedEvent>(OnFastTravelRequested);
    }

    public override void _ExitTree()
    {
        EventBus.Instance?.Unsubscribe<RegionTransitionRequestedEvent>(OnRegionTransitionRequested);
        EventBus.Instance?.Unsubscribe<FastTravelRequestedEvent>(OnFastTravelRequested);

        // The safe zones and the Weave potency are per-region statics; a world going away must not
        // leave the next one standing in the last one's sanctuary.
        SafeZones.Clear();
        Weave.Reset();
    }

    /// <summary>Sun, sky material, tonemap, glow and the ground plane. The handles are kept because
    /// the sky controller is their only other reader.</summary>
    public void BuildEnvironment()
    {
        _environment = WorldEnvironmentBuilder.Build(Session.World);
    }

    public void BuildWeatherAndSky(UICompositionRoot ui, DeveloperToolsHost? devTools)
    {
        var weather = new WeatherDirector { Name = "Weather" };
        Session.World.AddChild(weather);
        ui.SetWeather(weather);
        devTools?.SetWeather(weather);

        Session.World.AddChild(new SkyController
        {
            Name = "Sky",
            Sun = _environment.Sun,
            Environment = _environment.Environment,
        });
    }

    public void BuildDirectors(UICompositionRoot ui, DeveloperToolsHost? devTools)
    {
        Session.World.AddChild(new EncounterDirector { Name = "Encounters" });
        Log.Info("Encounter director online — patrols by day, warbands by night, more in storms.");

        var events = new WorldEventDirector { Name = "WorldEvents" };
        Session.World.AddChild(events);
        ui.SetWorldEvents(events);
        devTools?.SetWorldEvents(events);
        Log.Info("World-event director online — raids, caches and champion hunts with rewards.");
    }

    /// <summary>
    /// Streams the active region's sub-cells around the player. The procedural sandbox stays the
    /// always-loaded base; the streamer manages the region's authored cells.
    /// </summary>
    public void BuildStreamer(MapService mapService)
    {
        _mapService = mapService;
        _streamer = new RegionStreamer { Name = "RegionStreamer" };
        RegionResource? region = RegionDatabase.Get(Session.CurrentRegionId);
        _streamer.Configure(region);

        // ⚠️ THE PLAYER'S SPAWN IS THE REGION'S, AND IT IS ONLY KNOWABLE HERE. The player is built
        // earlier in the session's build order and had no region to ask, so it used a literal that
        // was one region's spawn point copied by hand — correct for that region by coincidence, and
        // an absolute Y on a world that has real elevation. Configure has just published the
        // region's heightfield to WorldGround, so this is the first point at which the ground under
        // the spawn is a knowable number. A loaded game overwrites this again from its header.
        if (region != null && Session.Players.Player is { } player && IsInstanceValid(player))
        {
            player.Velocity = Vector3.Zero;
            player.GlobalPosition = RegionSpawn(region);
        }

        Session.World.AddChild(_streamer);
        ServiceScope.RegisterOwned(_streamer, _streamer);
        RegionSetup.RebuildPortals(Session.World, _portals, region);
        RegionSetup.ApplySafeZones(region);
        Weave.Set(region?.WeavePotency ?? Weave.DefaultPotency);
    }

    /// <summary>
    /// Where a region starts the player, seated on the ground it actually has.
    ///
    /// ⚠️ <b>A REGION'S <c>SpawnPoint.Y</c> IS AN OFFSET, NOT A WORLD Y</b> — live invariant 23,
    /// learned three times during the world-generation replacement and true here too. Both authored
    /// spawns are <c>y = 1.2</c>, which was the player capsule's resting height back when every
    /// floor's top face was exactly y = 0. The generator gave Ember Crown real elevation and the
    /// ground under its spawn is now −1.81 m, so a literal 1.2 left the player hanging 3.01 m in the
    /// air — one centimetre outside the loading gate's 3 m ground probe, which is why <b>New Game
    /// could not reach Playing at all</b> and the failure looked like a streaming problem.
    ///
    /// <see cref="SafeLanding"/> could not catch it: it lifts and never lowers, and the player was
    /// above the ground, not below it. So the authored Y is read as the clearance it always meant.
    /// </summary>
    public static Vector3 RegionSpawn(RegionResource region) =>
        WorldGround.OnGround(region.SpawnPoint, region.SpawnPoint.Y);

    /// <summary>
    /// ⚠️ <b>NEVER PUT THE PLAYER UNDER THE GROUND (the 2026-08-29 geography overhaul).</b> Every
    /// teleport in the game — a portal, a fast-travel jump, a save restore, the <c>region</c> dev
    /// command — writes an absolute Y that was safe for exactly as long as every cell floor's top
    /// face was y = 0. The world has real elevation now, so a point that has drifted below the
    /// surface leaves the player inside a hillside with no way out and no error anywhere.
    ///
    /// The v1 -> v2 save migration throws pre-overhaul transforms away, but this covers the routes
    /// a migration cannot: a hand-edited save, a fast-travel point recorded before a cell was
    /// re-cut, a landform edit under someone's autosave. It lifts and never lowers, so a legitimate
    /// jump onto a terrace or a rooftop is untouched.
    /// </summary>
    public static Vector3 SafeLanding(Vector3 landing)
    {
        if (!WorldGround.IsBelowGround(landing))
        {
            return landing;
        }

        Vector3 lifted = WorldGround.Lift(landing);
        Log.Warn($"Landing {landing} is below the ground at {lifted.Y:F2} m; lifting the player onto it.");
        return lifted;
    }

    /// <summary>
    /// The shared hard load. Shows the loading screen, swaps the streamer to the destination region
    /// (only when it actually changes), teleports the player, autosaves the boundary, then settles
    /// for a few frames so the new cells stream in before play resumes.
    /// </summary>
    /// <param name="autosave">False when the move is itself a restore: autosaving the state just
    /// read back is churn, and on the autosave ring it would overwrite an older save with a copy of
    /// the one being loaded.</param>
    public void PerformRegionLoad(RegionResource destination, Vector3 landing, string message, bool autosave = true)
    {
        GameManager.Instance?.ChangeState(GameState.Loading);

        if (destination.Id != Session.CurrentRegionId)
        {
            string from = Session.CurrentRegionId;
            _streamer!.UnloadAll();
            Session.CurrentRegionId = destination.Id;
            _streamer.Configure(destination);
            _mapService?.DiscoverRegion(destination.Id); // entering reveals it on the map
            RegionSetup.RebuildPortals(Session.World, _portals, destination);
            RegionSetup.ApplySafeZones(destination);
            Weave.Set(destination.WeavePotency);

            // The seam every "clean up after a region change" subscriber hangs off. Published here
            // rather than at the request, because the request is refusable.
            EventBus.Instance?.Publish(new RegionChangedEvent(from, destination.Id));
        }

        PlayerCharacter? player = Session.Players.Player;
        if (player != null && IsInstanceValid(player))
        {
            player.Velocity = Vector3.Zero;

            // After Configure, so WorldGround is the DESTINATION region's field rather than the one
            // the player is leaving — a cross-region jump clamped against the wrong heightfield is
            // worse than not clamping at all.
            player.GlobalPosition = SafeLanding(landing);
        }

        // The band comes with you. Walking companions across a region boundary is not a thing they
        // can do, so they are cut to formation the moment the player lands.
        if (ServiceLocator.Instance is { } locator && locator.TryGet(out CompanionRoster party))
        {
            party.RegroupNow();
        }

        if (autosave && ServiceLocator.Instance is { } saveLocator &&
            saveLocator.TryGet(out AutosaveService autosaveService))
        {
            autosaveService.RequestRegionChangeAutosave();
        }

        Session.Loading.Begin(message, null);
    }

    /// <summary>
    /// Performs a hard region-to-region load through a portal: unload the current region's cells,
    /// re-target the streamer, teleport the player to the destination spawn, rebuild its portals,
    /// autosave the boundary, then settle before play resumes.
    /// </summary>
    private void OnRegionTransitionRequested(RegionTransitionRequestedEvent e)
    {
        RegionResource? destination = RegionDatabase.Get(e.RegionId);
        if (destination == null || Session.Players.Player == null || _streamer == null)
        {
            Log.Warn($"Region transition to '{e.RegionId}' aborted (unknown region or world not built).");
            return;
        }

        if (e.RegionId == Session.CurrentRegionId)
        {
            return;
        }

        if (!RegionSetup.PayToll(Session.Players.Player, destination))
        {
            return;
        }

        PerformRegionLoad(destination, destination.SpawnPoint, $"Entering {destination.DisplayName}...");
    }

    /// <summary>
    /// Fast-travels to a discovered travel node: resolves the node and reuses the hard-load path,
    /// but lands the player at the node's exact position and allows same-region jumps, unlike a
    /// neighbour portal.
    /// </summary>
    private void OnFastTravelRequested(FastTravelRequestedEvent e)
    {
        if (ServiceLocator.Instance is not { } locator || !locator.TryGet(out FastTravelService travel) ||
            !travel.TryGetNode(e.NodeId, out TravelNode node))
        {
            Log.Warn($"Fast travel to '{e.NodeId}' aborted (unknown node).");
            return;
        }

        RegionResource? destination = RegionDatabase.Get(node.RegionId);
        PlayerCharacter? player = Session.Players.Player;
        if (destination == null || player == null || _streamer == null)
        {
            Log.Warn($"Fast travel to '{e.NodeId}' aborted (unknown region or world not built).");
            return;
        }

        // The fee is charged here rather than at the map screen because this is where the map button
        // and the `travel goto` console command converge — gating either one alone would leave the
        // other a free ride. Fails closed: no gold, no jump.
        int fee = TravelCosts.FeeFor(node, _streamer.ActiveRegionId);
        if (fee > 0 &&
            (player.GetComponent<InventoryComponent>() is not { } purse ||
                !purse.RemoveItem(GameIds.Currency.Gold, fee)))
        {
            Log.Warn($"Fast travel to '{e.NodeId}' refused: {fee} gold required.");
            return;
        }

        PerformRegionLoad(destination, node.Position, $"Fast travelling to {node.Label}...");
    }
}
