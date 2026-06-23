using System;
using System.Collections.Generic;
using Embervale.Core;
using Embervale.Core.Diagnostics;
using Embervale.Core.Events;
using Godot;

namespace Embervale.Save;

/// <summary>
/// Collects every active <see cref="ISaveable"/> and serializes them into a versioned JSON
/// document per save slot. Registered as the <c>SaveManager</c> autoload.
///
/// As of Phase 24B each slot is a <b>directory</b> under <c>user://saves/&lt;slot&gt;/</c> holding
/// <c>save.json</c> (the full envelope) and <c>header.json</c> (lightweight metadata the slot
/// browser reads without deserializing the whole save). The envelope is a versioned map of
/// <c>SaveId -&gt; state</c>, so on load each registered saveable pulls its own entry — the set of
/// live objects drives restoration, scaling to hundreds of actors without bespoke save code.
///
/// Legacy single-file saves (<c>user://saves/&lt;slot&gt;.json</c>) are still readable and are
/// migrated to the directory layout on the next save.
/// </summary>
public sealed partial class SaveManager : Node
{
    private const int SaveFormatVersion = 1;
    private const string SaveDirectory = "user://saves";

    public static SaveManager Instance { get; private set; } = null!;

    private readonly List<ISaveable> _saveables = new();

    /// <summary>
    /// Optional source of gameplay header fields (<c>region</c>, <c>level</c>,
    /// <c>corruption_tier</c>) stamped into each save, set by the bootstrap so this manager stays
    /// decoupled from gameplay types. Null while no world is built (e.g. the bare main menu).
    /// </summary>
    public Func<Godot.Collections.Dictionary>? HeaderProvider { get; set; }

    /// <summary>The slot that quick/manual saves (F5/F9, pause menu) target. Set to a chosen slot
    /// when a game is started or loaded from the slot browser (Phase 24C); defaults to <c>quick</c>.</summary>
    public string ActiveSlot { get; set; } = "quick";

    // Accumulated in-world play time for the active save; ticked while Playing, persisted in the
    // header and restored on load so it continues per-slot.
    private double _playtimeSeconds;

    // While a load is in flight these hold the loaded snapshot so a saveable that comes online
    // mid-load (an actor recreated by the PersistentSpawnDirector) can restore itself immediately.
    private Godot.Collections.Dictionary? _activeLoad;
    private HashSet<string>? _activeClaimed;

    public override void _EnterTree()
    {
        if (Instance != null && Instance != this)
        {
            QueueFree();
            return;
        }

        Instance = this;
    }

    public override void _ExitTree()
    {
        if (Instance == this)
        {
            _saveables.Clear();
            Instance = null!;
        }
    }

    public override void _Process(double delta)
    {
        // Only the active session's wall-time counts toward this save's playtime.
        if (GameManager.Instance is { IsPlaying: true })
        {
            _playtimeSeconds += delta;
        }
    }

    /// <summary>Resets the playtime counter; the bootstrap calls this when starting a New Game.</summary>
    public void ResetPlaytime() => _playtimeSeconds = 0d;

    public void Register(ISaveable saveable)
    {
        if (!_saveables.Contains(saveable))
        {
            _saveables.Add(saveable);
        }

        // If a load is in flight, an actor that registers now (e.g. one the spawn director just
        // recreated) restores itself from the in-flight snapshot rather than missing this load.
        if (_activeLoad != null)
        {
            string id = saveable.SaveId;
            if (_activeLoad.TryGetValue(id, out Variant state) && state.VariantType == Variant.Type.Dictionary)
            {
                _activeClaimed?.Add(id);
                try
                {
                    saveable.Load(state.AsGodotDictionary());
                }
                catch (Exception ex)
                {
                    Log.Error($"Saveable '{id}' threw in Load() during spawn restore: {ex}");
                }
            }
        }
    }

    public void Unregister(ISaveable saveable)
    {
        _saveables.Remove(saveable);
    }

    // --- Slot paths ---------------------------------------------------------

    private static string SlotDir(string slot) => $"{SaveDirectory}/{slot}";
    private static string SlotSavePath(string slot) => $"{SlotDir(slot)}/save.json";
    private static string SlotHeaderPath(string slot) => $"{SlotDir(slot)}/header.json";
    private static string LegacySlotPath(string slot) => $"{SaveDirectory}/{slot}.json";

    /// <summary>The slot's screenshot thumbnail path (may not exist), for the slot browser.</summary>
    public string ScreenshotPath(string slot) => $"{SlotDir(slot)}/screenshot.png";

    /// <summary>The full-save file path for a slot (the new directory layout).</summary>
    public string SlotPath(string slot) => SlotSavePath(slot);

    /// <summary>Whether a slot has a save in either the new or the legacy layout.</summary>
    public bool SaveExists(string slot) =>
        FileAccess.FileExists(SlotSavePath(slot)) || FileAccess.FileExists(LegacySlotPath(slot));

    // --- Save ---------------------------------------------------------------

    /// <summary>Serializes all registered saveables to the given slot. Returns success.</summary>
    public bool SaveGame(string slot) => SaveGame(slot, isAutosave: false);

    /// <summary>Serializes all registered saveables to the given slot. <paramref name="isAutosave"/>
    /// only flavours the published <see cref="GameSavedEvent"/> (Phase 24D) — the autosave cadence
    /// lives in <see cref="AutosaveService"/>; this stays the low-level writer. Returns success.</summary>
    public bool SaveGame(string slot, bool isAutosave)
    {
        DirAccess.MakeDirRecursiveAbsolute(SlotDir(slot));

        // Collect state defensively: a single component throwing in Save() must not
        // abort the whole save or corrupt the file — log it and persist the rest.
        var objects = new Godot.Collections.Dictionary();
        int failures = 0;
        foreach (ISaveable saveable in _saveables)
        {
            string id = saveable.SaveId;
            if (objects.ContainsKey(id))
            {
                Log.Warn($"Two saveables share SaveId '{id}'; the later one overwrites the earlier. State will be lost.");
            }

            try
            {
                objects[id] = saveable.Save();
            }
            catch (Exception ex)
            {
                failures++;
                Log.Error($"Saveable '{id}' threw in Save(); skipping it: {ex}");
            }
        }

        Godot.Collections.Dictionary header = BuildHeader(slot).ToDictionary();

        var root = new Godot.Collections.Dictionary
        {
            ["version"] = SaveFormatVersion,
            ["timestamp"] = Time.GetUnixTimeFromSystem(),
            ["header"] = header,
            ["objects"] = objects,
        };

        if (!AtomicWrite(SlotSavePath(slot), Json.Stringify(root, "\t")))
        {
            return false;
        }

        // The header mirror is a read optimization for the slot browser; if it fails the save is
        // still valid (the header also lives inside the envelope), so warn rather than fail.
        if (!AtomicWrite(SlotHeaderPath(slot), Json.Stringify(header, "\t")))
        {
            Log.Warn($"Saved slot '{slot}' but could not write its header.json mirror.");
        }

        CaptureScreenshot(slot);

        // One-time migration: once the directory layout holds the save, drop the legacy flat file.
        string legacy = LegacySlotPath(slot);
        if (FileAccess.FileExists(legacy))
        {
            DirAccess.RemoveAbsolute(legacy);
        }

        Log.Info($"Saved {objects.Count} object(s) to slot '{slot}'" + (failures > 0 ? $" ({failures} skipped)." : "."));
        EventBus.Instance?.Publish(new GameSavedEvent(slot, isAutosave));
        return true;
    }

    /// <summary>Atomic write: stage to a temp file, then rename over the target so a crash
    /// mid-write can never truncate a previously-good file.</summary>
    private static bool AtomicWrite(string target, string contents)
    {
        string temp = $"{target}.tmp";
        using (FileAccess? file = FileAccess.Open(temp, FileAccess.ModeFlags.Write))
        {
            if (file == null)
            {
                Log.Error($"Could not open temp file '{temp}': {FileAccess.GetOpenError()}");
                return false;
            }

            file.StoreString(contents);
        }

        Error renamed = DirAccess.RenameAbsolute(temp, target);
        if (renamed != Error.Ok)
        {
            Log.Error($"Could not commit '{target}' (rename failed: {renamed}); previous file preserved.");
            return false;
        }

        return true;
    }

    /// <summary>Grabs a small thumbnail of the current frame for the slot browser (Phase 24C).
    /// Best-effort: any failure is logged and ignored — a missing thumbnail never breaks a save.</summary>
    private void CaptureScreenshot(string slot)
    {
        try
        {
            Image? image = GetViewport()?.GetTexture()?.GetImage();
            if (image == null)
            {
                return;
            }

            image.Resize(320, 180, Image.Interpolation.Bilinear);
            Error error = image.SavePng(ScreenshotPath(slot));
            if (error != Error.Ok)
            {
                Log.Warn($"Could not write screenshot for slot '{slot}': {error}.");
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Screenshot capture failed for slot '{slot}'; continuing without one: {ex.Message}");
        }
    }

    private SaveSlotInfo BuildHeader(string slot)
    {
        var info = new SaveSlotInfo
        {
            Slot = slot,
            TimestampUnix = Time.GetUnixTimeFromSystem(),
            PlaytimeSeconds = _playtimeSeconds,
        };

        if (HeaderProvider?.Invoke() is { } fields)
        {
            if (fields.TryGetValue("region", out Variant region)) { info.Region = region.AsString(); }
            if (fields.TryGetValue("level", out Variant level)) { info.Level = level.AsInt32(); }
            if (fields.TryGetValue("corruption_tier", out Variant tier)) { info.CorruptionTier = tier.AsString(); }
        }

        return info;
    }

    // --- Slot management ----------------------------------------------------

    /// <summary>Reads a slot's lightweight header (from <c>header.json</c>, falling back to the
    /// header embedded in <c>save.json</c>). Null if the slot has no readable header.</summary>
    public SaveSlotInfo? ReadHeader(string slot)
    {
        if (ReadJsonObject(SlotHeaderPath(slot)) is { } headerDoc)
        {
            SaveSlotInfo info = SaveSlotInfo.FromDictionary(headerDoc);
            info.Slot = slot;
            return info;
        }

        // Fall back to the header inside the full save (or a bare header for a legacy save).
        string fullPath = FileAccess.FileExists(SlotSavePath(slot)) ? SlotSavePath(slot) : LegacySlotPath(slot);
        if (ReadJsonObject(fullPath) is { } root)
        {
            SaveSlotInfo info = root.TryGetValue("header", out Variant h) && h.VariantType == Variant.Type.Dictionary
                ? SaveSlotInfo.FromDictionary(h.AsGodotDictionary())
                : new SaveSlotInfo();
            info.Slot = slot;
            if (info.TimestampUnix == 0d && root.TryGetValue("timestamp", out Variant ts))
            {
                info.TimestampUnix = ts.AsDouble();
            }

            return info;
        }

        return null;
    }

    /// <summary>Every save slot's header, for the load/continue browser.</summary>
    public IReadOnlyList<SaveSlotInfo> ListSlots()
    {
        var slots = new List<SaveSlotInfo>();
        using DirAccess? dir = DirAccess.Open(SaveDirectory);
        if (dir == null)
        {
            return slots;
        }

        foreach (string name in dir.GetDirectories())
        {
            if (ReadHeader(name) is { } info)
            {
                slots.Add(info);
            }
        }

        return slots;
    }

    /// <summary>Deletes a slot's directory (and any legacy flat file). Returns success.</summary>
    public bool DeleteSlot(string slot)
    {
        bool removedAnything = false;

        using (DirAccess? dir = DirAccess.Open(SlotDir(slot)))
        {
            if (dir != null)
            {
                foreach (string file in dir.GetFiles())
                {
                    dir.Remove(file);
                }

                removedAnything = true;
            }
        }

        if (removedAnything)
        {
            DirAccess.RemoveAbsolute(SlotDir(slot));
        }

        string legacy = LegacySlotPath(slot);
        if (FileAccess.FileExists(legacy))
        {
            DirAccess.RemoveAbsolute(legacy);
            removedAnything = true;
        }

        if (removedAnything)
        {
            Log.Info($"Deleted save slot '{slot}'.");
        }

        return removedAnything;
    }

    private static Godot.Collections.Dictionary? ReadJsonObject(string path)
    {
        if (!FileAccess.FileExists(path))
        {
            return null;
        }

        using FileAccess? file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (file == null)
        {
            return null;
        }

        Variant parsed = Json.ParseString(file.GetAsText());
        return parsed.VariantType == Variant.Type.Dictionary ? parsed.AsGodotDictionary() : null;
    }

    // --- Load ---------------------------------------------------------------

    /// <summary>Loads the given slot and dispatches state to registered saveables.</summary>
    public bool LoadGame(string slot)
    {
        // Prefer the new directory layout; fall back to a legacy flat file.
        string path = FileAccess.FileExists(SlotSavePath(slot)) ? SlotSavePath(slot) : LegacySlotPath(slot);
        if (!FileAccess.FileExists(path))
        {
            Log.Warn($"Save slot '{slot}' does not exist.");
            return false;
        }

        using FileAccess? file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (file == null)
        {
            Log.Error($"Could not read save slot '{slot}': {FileAccess.GetOpenError()}");
            return false;
        }

        string json = file.GetAsText();
        Variant parsed = Json.ParseString(json);
        if (parsed.VariantType != Variant.Type.Dictionary)
        {
            Log.Error($"Save slot '{slot}' is corrupt or not an object.");
            return false;
        }

        var root = parsed.AsGodotDictionary();

        int version = root.TryGetValue("version", out Variant versionVariant) ? versionVariant.AsInt32() : 0;
        if (!TryMigrate(slot, version, ref root))
        {
            return false;
        }

        if (!root.TryGetValue("objects", out Variant objectsVariant) ||
            objectsVariant.VariantType != Variant.Type.Dictionary)
        {
            Log.Error($"Save slot '{slot}' has no 'objects' section.");
            return false;
        }

        // Continue this save's playtime from where it was last written.
        if (root.TryGetValue("header", out Variant headerVariant) && headerVariant.VariantType == Variant.Type.Dictionary)
        {
            _playtimeSeconds = SaveSlotInfo.FromDictionary(headerVariant.AsGodotDictionary()).PlaytimeSeconds;
        }

        var objects = objectsVariant.AsGodotDictionary();
        int restored = 0;
        int failures = 0;
        var claimed = new HashSet<string>();

        // Publish the snapshot so the Register hook can restore actors spawned during this load
        // (e.g. the PersistentSpawnDirector recreating saved actors as it is itself restored).
        _activeLoad = objects;
        _activeClaimed = claimed;
        try
        {
            // Iterate a snapshot: a saveable's Load() may spawn actors that register new saveables,
            // which would otherwise mutate the live list mid-enumeration.
            foreach (ISaveable saveable in _saveables.ToArray())
            {
                string id = saveable.SaveId;
                if (claimed.Contains(id))
                {
                    continue; // already restored via the spawn hook
                }

                if (!objects.TryGetValue(id, out Variant state) || state.VariantType != Variant.Type.Dictionary)
                {
                    Log.Warn($"Save slot '{slot}' has no usable entry for '{id}'; it keeps its current state.");
                    continue;
                }

                claimed.Add(id);
                try
                {
                    saveable.Load(state.AsGodotDictionary());
                    restored++;
                }
                catch (Exception ex)
                {
                    failures++;
                    Log.Error($"Saveable '{id}' threw in Load(); leaving it at its current state: {ex}");
                }
            }

            // Surface state that has no live owner — usually a transient/runtime-id actor
            // that no longer exists, or a renamed SaveId. Helps catch persistence drift.
            foreach (System.Collections.Generic.KeyValuePair<Variant, Variant> entry in objects)
            {
                string id = entry.Key.AsString();
                if (!claimed.Contains(id))
                {
                    Log.Warn($"Save slot '{slot}' entry '{id}' had no live claimant on load (orphaned state).");
                }
            }
        }
        finally
        {
            _activeLoad = null;
            _activeClaimed = null;
        }

        Log.Info($"Loaded slot '{slot}'; restored {restored} object(s)" + (failures > 0 ? $" ({failures} failed)." : "."));
        EventBus.Instance?.Publish(new GameLoadedEvent(slot));
        return true;
    }

    /// <summary>
    /// Migration seam for the versioned save envelope. Today the format is at
    /// <see cref="SaveFormatVersion"/>; this is where future format changes upgrade an
    /// older document in place before it reaches the saveables. A newer-than-known file
    /// is refused rather than silently misread.
    /// </summary>
    private bool TryMigrate(string slot, int version, ref Godot.Collections.Dictionary root)
    {
        if (version == SaveFormatVersion)
        {
            return true;
        }

        if (version > SaveFormatVersion)
        {
            Log.Error($"Save slot '{slot}' is version {version}, newer than this build supports ({SaveFormatVersion}); refusing to load.");
            return false;
        }

        // version < SaveFormatVersion: walk forward one step at a time. No upgrade steps
        // exist yet (v1 is the first format); this branch is the documented seam.
        Log.Warn($"Save slot '{slot}' is an older version {version}; loading at best effort (no migration steps registered).");
        return true;
    }
}
