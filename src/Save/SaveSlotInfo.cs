using Godot;

namespace Embervale.Save;

/// <summary>
/// Lightweight metadata for one save slot (Phase 24B): enough to render a slot in the
/// load/continue browser (24C) without deserializing the whole save. Written to
/// <c>user://saves/&lt;slot&gt;/header.json</c> alongside the full <c>save.json</c> and mirrored
/// inside the save envelope so playtime continues across a load.
///
/// Gameplay fields (<see cref="Region"/>, <see cref="Level"/>, <see cref="CorruptionTier"/>) are
/// supplied by <see cref="SaveManager.HeaderProvider"/> at save time so <see cref="SaveManager"/>
/// stays decoupled from gameplay types; <see cref="SaveManager"/> stamps the timestamp and playtime.
/// </summary>
public sealed class SaveSlotInfo
{
    public string Slot { get; set; } = string.Empty;

    /// <summary>Wall-clock save time (Unix seconds), for "last played" ordering and display.</summary>
    public double TimestampUnix { get; set; }

    /// <summary>Accumulated in-world play time for this save, in seconds.</summary>
    public double PlaytimeSeconds { get; set; }

    public string Region { get; set; } = "Unknown";

    /// <summary>The restorable region <b>id</b> (e.g. "region.ember_crown") — distinct from
    /// <see cref="Region"/> (the display name). Lets a load return to the region it was saved in.</summary>
    public string RegionId { get; set; } = string.Empty;

    /// <summary>Saved player world transform, so a load returns the player to where they stood.</summary>
    public float PlayerX { get; set; }
    public float PlayerY { get; set; }
    public float PlayerZ { get; set; }
    public float PlayerYaw { get; set; }

    /// <summary>True when this header carried a saved player position (a post-Phase-29.5 save).</summary>
    public bool HasLocation { get; set; }

    public int Level { get; set; } = 1;

    /// <summary>The player's corruption tier label at save time (e.g. "Marked").</summary>
    public string CorruptionTier { get; set; } = "Untainted";

    /// <summary>The chosen race id (Phase 26C), e.g. "race.umbral" — drives the spawned player's traits.</summary>
    public string RaceId { get; set; } = "race.human";

    /// <summary>The character's chosen name (Phase 26C).</summary>
    public string CharacterName { get; set; } = "Wanderer";

    /// <summary>Optional creator identity fields; older saves default to empty.</summary>
    public string Appearance { get; set; } = string.Empty;
    public string Background { get; set; } = string.Empty;

    public Godot.Collections.Dictionary ToDictionary()
    {
        var data = new Godot.Collections.Dictionary
        {
            ["slot"] = Slot,
            ["timestamp"] = TimestampUnix,
            ["playtime"] = PlaytimeSeconds,
            ["region"] = Region,
            ["region_id"] = RegionId,
            ["level"] = Level,
            ["corruption_tier"] = CorruptionTier,
            ["race_id"] = RaceId,
            ["char_name"] = CharacterName,
            ["appearance"] = Appearance,
            ["background"] = Background,
        };
        // Absence is meaningful for older saves and for a save with no live player. Emitting
        // default zero coordinates turns that absence into a teleport to the world origin.
        if (HasLocation)
        {
            data["player_x"] = PlayerX;
            data["player_y"] = PlayerY;
            data["player_z"] = PlayerZ;
            data["player_yaw"] = PlayerYaw;
        }
        return data;
    }

    public static SaveSlotInfo FromDictionary(Godot.Collections.Dictionary data)
    {
        var info = new SaveSlotInfo();
        if (data.TryGetValue("slot", out Variant slot)) { info.Slot = slot.AsString(); }
        if (data.TryGetValue("timestamp", out Variant ts)) { info.TimestampUnix = ts.AsDouble(); }
        if (data.TryGetValue("playtime", out Variant pt)) { info.PlaytimeSeconds = pt.AsDouble(); }
        if (data.TryGetValue("region", out Variant region)) { info.Region = region.AsString(); }
        if (data.TryGetValue("region_id", out Variant regionId)) { info.RegionId = regionId.AsString(); }
        if (data.TryGetValue("player_x", out Variant px)) { info.PlayerX = (float)px.AsDouble(); info.HasLocation = true; }
        if (data.TryGetValue("player_y", out Variant py)) { info.PlayerY = (float)py.AsDouble(); }
        if (data.TryGetValue("player_z", out Variant pz)) { info.PlayerZ = (float)pz.AsDouble(); }
        if (data.TryGetValue("player_yaw", out Variant yaw)) { info.PlayerYaw = (float)yaw.AsDouble(); }
        if (data.TryGetValue("level", out Variant level)) { info.Level = level.AsInt32(); }
        if (data.TryGetValue("corruption_tier", out Variant tier)) { info.CorruptionTier = tier.AsString(); }
        if (data.TryGetValue("race_id", out Variant race)) { info.RaceId = race.AsString(); }
        if (data.TryGetValue("char_name", out Variant name)) { info.CharacterName = name.AsString(); }
        if (data.TryGetValue("appearance", out Variant appearance)) { info.Appearance = appearance.AsString(); }
        if (data.TryGetValue("background", out Variant background)) { info.Background = background.AsString(); }
        return info;
    }
}
