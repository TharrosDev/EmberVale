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

    public int Level { get; set; } = 1;

    /// <summary>The player's corruption tier label at save time (e.g. "Marked").</summary>
    public string CorruptionTier { get; set; } = "Untainted";

    public Godot.Collections.Dictionary ToDictionary() => new()
    {
        ["slot"] = Slot,
        ["timestamp"] = TimestampUnix,
        ["playtime"] = PlaytimeSeconds,
        ["region"] = Region,
        ["level"] = Level,
        ["corruption_tier"] = CorruptionTier,
    };

    public static SaveSlotInfo FromDictionary(Godot.Collections.Dictionary data)
    {
        var info = new SaveSlotInfo();
        if (data.TryGetValue("slot", out Variant slot)) { info.Slot = slot.AsString(); }
        if (data.TryGetValue("timestamp", out Variant ts)) { info.TimestampUnix = ts.AsDouble(); }
        if (data.TryGetValue("playtime", out Variant pt)) { info.PlaytimeSeconds = pt.AsDouble(); }
        if (data.TryGetValue("region", out Variant region)) { info.Region = region.AsString(); }
        if (data.TryGetValue("level", out Variant level)) { info.Level = level.AsInt32(); }
        if (data.TryGetValue("corruption_tier", out Variant tier)) { info.CorruptionTier = tier.AsString(); }
        return info;
    }
}
