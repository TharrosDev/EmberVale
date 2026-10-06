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

    // --- Slot browser fields (ics-base). All absent-default: a header written before them reads as
    // "kind by slot name, format unknown, no label, healthy". ---

    private SaveKind? _kind;

    /// <summary>How the save was made. A header that does not record it (every pre-ics save)
    /// answers by its slot name, via <see cref="SaveSlots.KindOf"/>.</summary>
    public SaveKind Kind
    {
        get => _kind ?? SaveSlots.KindOf(Slot);
        set => _kind = value;
    }

    /// <summary>The save format version the file was written in; 0 = the header does not say
    /// (<see cref="SaveManager.InspectSlot"/> fills it from the envelope).</summary>
    public int FormatVersion { get; set; }

    /// <summary>The player's own label for the save; empty = none, show the slot's default name.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Whether the slot can be loaded. <b>Never written to disk</b>: it is a finding about
    /// the file, made by <see cref="SaveManager.InspectSlot"/>, and anything else leaves it
    /// <see cref="SaveHealth.Ok"/>.</summary>
    public SaveHealth Health { get; set; } = SaveHealth.Ok;

    // --- Integrity fields (ics save-core). ---

    /// <summary>The game build that wrote the save (<c>application/config/version</c>); empty =
    /// the header does not say (every save before format 4).</summary>
    public string GameBuild { get; set; } = string.Empty;

    /// <summary>The content checksum the envelope carries (<see cref="SaveChecksum"/>), mirrored
    /// here so a header can be matched to its save; empty = none recorded.</summary>
    public string Checksum { get; set; } = string.Empty;

    /// <summary>What <see cref="SaveManager.InspectSlot"/> found in <c>save.json</c> itself, before
    /// the backup was considered. Differs from <see cref="Health"/> exactly when
    /// <see cref="RecoveredFromBackup"/> is set. <b>Never written to disk.</b></summary>
    public SaveHealth PrimaryHealth { get; set; } = SaveHealth.Ok;

    /// <summary>True when the slot's own save is damaged or gone and everything in this header
    /// describes its <b>previous generation</b> (<c>save.json.bak</c>), which is what a load of the
    /// slot will read. The browser should say so: the player is about to lose the newest save's
    /// progress. <b>Never written to disk.</b></summary>
    public bool RecoveredFromBackup { get; set; }

    /// <summary>Whether a loadable previous generation sits beside the save. <b>Never written to disk.</b></summary>
    public bool HasBackup { get; set; }

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
            ["kind"] = (int)Kind,
        };
        if (FormatVersion > 0)
        {
            data["format"] = FormatVersion;
        }

        if (DisplayName.Length > 0)
        {
            data["display_name"] = DisplayName;
        }

        if (GameBuild.Length > 0)
        {
            data["build"] = GameBuild;
        }

        if (Checksum.Length > 0)
        {
            data["checksum"] = Checksum;
        }

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

    /// <summary>Reads a header. Every field is optional and tolerant (<see cref="SaveRead"/>): an
    /// absent key or one of the wrong type leaves the default, so a damaged header still describes
    /// what it can instead of throwing in the slot browser.</summary>
    public static SaveSlotInfo FromDictionary(Godot.Collections.Dictionary data)
    {
        var info = new SaveSlotInfo();
        info.Slot = SaveRead.Text(data, "slot", info.Slot);
        info.TimestampUnix = SaveRead.Number(data, "timestamp");
        info.PlaytimeSeconds = SaveRead.Number(data, "playtime");
        info.Region = SaveRead.Text(data, "region", info.Region);
        info.RegionId = SaveRead.Text(data, "region_id");

        // The transform is all or nothing on X: a header with no player_x has no location, and
        // restoring the other three alone would be a teleport to wherever X defaults.
        if (SaveRead.TryNumber(data, "player_x", out double playerX))
        {
            info.PlayerX = (float)playerX;
            info.PlayerY = SaveRead.Float(data, "player_y");
            info.PlayerZ = SaveRead.Float(data, "player_z");
            info.PlayerYaw = SaveRead.Float(data, "player_yaw");
            info.HasLocation = true;
        }

        info.Level = SaveRead.Int(data, "level", info.Level);
        info.CorruptionTier = SaveRead.Text(data, "corruption_tier", info.CorruptionTier);
        info.RaceId = SaveRead.Text(data, "race_id", info.RaceId);
        info.CharacterName = SaveRead.Text(data, "char_name", info.CharacterName);
        info.Appearance = SaveRead.Text(data, "appearance");
        info.Background = SaveRead.Text(data, "background");
        if (SaveRead.TryNumber(data, "kind", out double kind))
        {
            info.Kind = (SaveKind)(int)System.Math.Clamp(kind, (double)SaveKind.Manual, (double)SaveKind.Auto);
        }

        info.FormatVersion = System.Math.Max(0, SaveRead.Int(data, "format"));
        info.DisplayName = SaveRead.Text(data, "display_name");
        info.GameBuild = SaveRead.Text(data, "build");
        info.Checksum = SaveRead.Text(data, "checksum");
        return info;
    }
}
