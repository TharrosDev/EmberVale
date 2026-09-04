using System.Collections.Generic;
using Embervale.Corruption;
using Embervale.Player;
using Embervale.Progression;
using Embervale.Save;
using Embervale.World;
using Godot;

namespace Embervale.Bootstrap;

/// <summary>
/// The two seams between the save manager and live gameplay: what goes in a slot's header, and what
/// to do with a header when it is read back.
///
/// <para>Both are wired as delegates on <see cref="SaveManager"/> by the lifecycle coordinator, so
/// the manager stays free of gameplay types and a session going away takes the wiring with it —
/// which is why <see cref="SessionLifecycleCoordinator.DestroySession"/> clears them.</para>
/// </summary>
public sealed class SaveHeaderComposer
{
    private readonly GameSession _session;

    public SaveHeaderComposer(GameSession session)
    {
        _session = session;
    }

    /// <summary>Supplies the gameplay fields of a save header. Read lazily by the save manager at
    /// save time; the region name comes from the active region's resource.</summary>
    public Godot.Collections.Dictionary Build()
    {
        string regionId = _session.CurrentRegionId;

        // region_id is the restorable id (vs. the display name) so a load returns to the saved region.
        var header = new Godot.Collections.Dictionary
        {
            ["region"] = RegionDatabase.Get(regionId)?.DisplayName ?? "Unknown Region",
            ["region_id"] = regionId,
        };

        // The chosen race + identity, so a reload rebuilds the right character.
        foreach (KeyValuePair<string, string> field in _session.Profile.ToHeaderFields())
        {
            header[field.Key] = field.Value;
        }

        if (_session.Players.Player is { } player && GodotObject.IsInstanceValid(player))
        {
            if (player.GetComponent<ProgressionComponent>() is { } progression)
            {
                header["level"] = progression.Level;
            }

            if (player.GetComponent<CorruptionComponent>() is { } corruption)
            {
                header["corruption_tier"] = CorruptionTiers.Label(corruption.Tier);
            }

            // Player world transform, so a load returns them to where they stood (not the start tile).
            Vector3 pos = player.GlobalPosition;
            header["player_x"] = pos.X;
            header["player_y"] = pos.Y;
            header["player_z"] = pos.Z;
            header["player_yaw"] = player.Rotation.Y;
        }

        return header;
    }

    /// <summary>
    /// Returns the player to the transform and region a save was written at. Wired into
    /// <see cref="SaveManager.LocationApplier"/>, so it runs at the end of <b>every</b> load — the
    /// slot browser, F9, and the pause menu — rather than only the route that happened to implement
    /// it.
    ///
    /// ⚠️ <b>A cross-region load is a hard load, not a teleport.</b> Writing the transform alone
    /// would drop the player into a region whose cells, portals, safe zones and Weave potency are
    /// still configured for the one they were standing in.
    /// </summary>
    public void ApplySavedLocation(SaveSlotInfo header)
    {
        PlayerCharacter? player = _session.Players.Player;
        if (player == null || !GodotObject.IsInstanceValid(player))
        {
            return;
        }

        Vector3 landing = WorldSessionDirector.SafeLanding(
            new Vector3(header.PlayerX, header.PlayerY, header.PlayerZ));

        // Cross-region: reuse the streamer swap + loading-screen settle the portal path already uses.
        // A load from the slot browser sets the region before the session is built, so it never takes
        // this branch — only a mid-session F9 or pause-menu load can.
        if (!string.IsNullOrEmpty(header.RegionId) && header.RegionId != _session.CurrentRegionId &&
            RegionDatabase.Get(header.RegionId) is { } destination)
        {
            _session.WorldDirector.PerformRegionLoad(
                destination, landing, $"Loaded into {destination.DisplayName} from a save.", autosave: false);
        }
        else
        {
            player.Velocity = Vector3.Zero;
            player.GlobalPosition = landing;
        }

        player.Rotation = new Vector3(player.Rotation.X, header.PlayerYaw, player.Rotation.Z);
    }
}
