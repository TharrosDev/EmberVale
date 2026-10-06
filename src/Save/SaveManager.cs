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
///
/// The rules that decide whether a file may be loaded are pure and live beside this class, where
/// xUnit can run them: <see cref="SaveEnvelope"/> (parse, shape, version, checksum),
/// <see cref="SaveMigrations"/> (the v1 -> v4 chain), <see cref="SaveChecksum"/> and
/// <see cref="SaveBackup"/> (the one previous generation each slot keeps as <c>save.json.bak</c>).
/// This class does the file work and the dispatch to live saveables. docs/SAVE_FORMAT.md is the
/// contract.
/// </summary>
public sealed partial class SaveManager : Node
{
    private const int SaveFormatVersion = SaveEnvelope.CurrentVersion;
    private static string SaveDirectory => Embervale.Core.UserDataPaths.Resolve("saves");

    public static SaveManager Instance { get; private set; } = null!;

    private readonly List<ISaveable> _saveables = new();

    /// <summary>The save ids currently registered — the <c>savecheck</c> dev command audits these for
    /// volatile (would-orphan) keys (Phase 25.5A).</summary>
    public IEnumerable<string> RegisteredSaveIds
    {
        get
        {
            foreach (ISaveable saveable in _saveables)
            {
                yield return saveable.SaveId;
            }
        }
    }

    /// <summary>
    /// Optional source of gameplay header fields (<c>region</c>, <c>level</c>,
    /// <c>corruption_tier</c>) stamped into each save, set by the bootstrap so this manager stays
    /// decoupled from gameplay types. Null while no world is built (e.g. the bare main menu).
    /// </summary>
    public Func<Godot.Collections.Dictionary>? HeaderProvider { get; set; }

    /// <summary>
    /// Optional sink for the saved player location, set by the bootstrap alongside
    /// <see cref="HeaderProvider"/> and invoked at the end of a successful <see cref="LoadGame"/>.
    ///
    /// ⚠️ <b>This exists because the restore used to live in ONE of the three load routes.</b> The
    /// slot browser went through <c>SessionLifecycleCoordinator.StartLoadedGame</c>, which applied the header's
    /// region and transform after its overlay — but F9 and the pause menu call
    /// <see cref="LoadGame"/> directly, so they rewound inventory, quests, stats, the economy and
    /// the world to the save point and left the player standing wherever they happened to be, in
    /// whatever region they happened to be in. Restoring from inside the load is what makes the
    /// three routes agree; a new route gets it for free.
    /// </summary>
    public Action<SaveSlotInfo>? LocationApplier { get; set; }

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
    private HashSet<string>? _activeDeferred;
    private int _activeLoadFailures;
    private bool _operationInProgress;

    public override void _EnterTree()
    {
        if (Instance != null && Instance != this)
        {
            QueueFree();
            return;
        }

        Instance = this;
    }

    public override void _Ready()
    {
        // A crash between staging a file and renaming it over its target leaves the staged copy
        // behind. It is never read, so it is only removed: once, before anything can be saving.
        CleanOrphanedTemps();
    }

    public override void _ExitTree()
    {
        // A write still queued when the process ends is a save the player was told had been taken.
        SaveWriteQueue.Flush();
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
        if (_saveables.Contains(saveable))
        {
            return;
        }
        _saveables.Add(saveable);

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
                    _activeLoadFailures++;
                    Log.Error($"Saveable '{id}' threw in Load() during spawn restore: {ex}");
                }
            }
        }
    }

    public void Unregister(ISaveable saveable)
    {
        _saveables.Remove(saveable);
    }

    /// <summary>
    /// Declares that <paramref name="id"/>'s state has an owner that simply is not in the tree yet, so
    /// the orphan report below must not call it drift. <see cref="CellPersistenceDirector"/> is the
    /// caller: a cell-scoped saveable (a holding's stash, a trophy stand) writes a top-level entry
    /// while its cell is streamed in, and is legitimately absent when the save is loaded from
    /// somewhere else — its state rides the cell ledger and is re-applied when the cell streams back.
    ///
    /// Deliberately a *separate* set from the claimed one: claiming would also suppress the live
    /// component's own restore in the main loop, which is exactly wrong in the case where the cell
    /// <em>is</em> loaded. This only silences the diagnostic, and only for ids something is holding.
    /// </summary>
    public void ClaimDeferred(string id)
    {
        _activeDeferred?.Add(id);
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

    /// <summary>Whether a slot has a save in either the new or the legacy layout, or a backup
    /// generation with nothing in front of it (a save interrupted between moving the old file aside
    /// and committing the new one), which a load reads as the slot.</summary>
    public bool SaveExists(string slot)
    {
        SaveWriteQueue.Flush();
        return FileAccess.FileExists(SlotSavePath(slot)) || FileAccess.FileExists(LegacySlotPath(slot)) ||
               FileAccess.FileExists(SlotBackupPath(slot));
    }

    // --- Save ---------------------------------------------------------------

    /// <summary>Serializes all registered saveables to the given slot. Returns success.</summary>
    public bool SaveGame(string slot) => SaveGame(slot, isAutosave: false);

    /// <summary>Serializes all registered saveables to the given slot. <paramref name="isAutosave"/>
    /// only flavours the published <see cref="GameSavedEvent"/> (Phase 24D) — the autosave cadence
    /// lives in <see cref="AutosaveService"/>; this stays the low-level writer. Returns success.
    ///
    /// ⚠️ <b>A live <see cref="PushSaveBlock"/> refuses every save, autosaves included.</b> A block
    /// says "a save taken now would be a bad place to come back to" (mid boss fight, mid
    /// conversation), and that is exactly as true of a save the game takes as of one the player
    /// asks for. The refusal publishes <see cref="SaveFailedEvent"/> with the block's own reason and
    /// no <see cref="SaveStartedEvent"/>, like the busy refusal below.
    ///
    /// ⚠️ <b>In windowed play a true return means "captured and queued".</b> The files are written
    /// by <see cref="SaveWriteQueue"/> off the main thread and the <see cref="GameSavedEvent"/> (or a
    /// <see cref="SaveFailedEvent"/>) follows when the disk answers. Headless and tooling runs write
    /// before returning, as they always did. Every read of a slot flushes the queue first.</summary>
    public bool SaveGame(string slot, bool isAutosave)
    {
        if (_saveBlocks.Count > 0)
        {
            string reason = _saveBlocks[^1].ReasonKey;
            Log.Info($"Save to slot '{slot}' refused: saving is blocked ({reason}).");
            EventBus.Instance?.Publish(new SaveFailedEvent(slot, reason));
            return false;
        }

        if (_operationInProgress)
        {
            Log.Warn($"Cannot save slot '{slot}' while another save/load is in progress.");
            EventBus.Instance?.Publish(new SaveFailedEvent(slot, ReasonBusy));
            return false;
        }
        _operationInProgress = true;
        bool saved = false;
        try
        {
            EventBus.Instance?.Publish(new SaveStartedEvent(slot, isAutosave ? SaveKind.Auto : KindOfSlot(slot)));
            saved = SaveGameCore(slot, isAutosave);
            return saved;
        }
        catch (Exception ex)
        {
            Log.Error($"Could not save slot '{slot}': {ex}");
            return false;
        }
        finally
        {
            _operationInProgress = false;
            if (!saved)
            {
                EventBus.Instance?.Publish(new SaveFailedEvent(slot, ReasonWriteFailed));
            }
        }
    }

    private bool SaveGameCore(string slot, bool isAutosave)
    {
        Error directoryError = DirAccess.MakeDirRecursiveAbsolute(SlotDir(slot));
        if (directoryError != Error.Ok)
        {
            Log.Error($"Could not create save slot '{slot}': {directoryError}");
            return false;
        }

        // Collect everything before touching the authoritative file. A partial snapshot destroys
        // progress just as surely as a truncated write, so any failed or duplicate entry refuses
        // the commit and preserves the previous save.
        var objects = new Godot.Collections.Dictionary();
        int failures = 0;
        foreach (ISaveable saveable in _saveables.ToArray())
        {
            if (saveable is Node node && (!IsInstanceValid(node) || node.IsQueuedForDeletion())) { continue; }
            string id = saveable.SaveId;
            if (string.IsNullOrEmpty(id) || objects.ContainsKey(id))
            {
                failures++;
                Log.Error($"Save slot '{slot}' has an empty or duplicate SaveId '{id}'; refusing to overwrite progress.");
                continue;
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

        if (failures > 0)
        {
            Log.Error($"Save slot '{slot}' could not capture {failures} object(s); previous save preserved.");
            return false;
        }

        // The checksum is of the objects exactly as they are serialized, so it is computed from the
        // text about to be written and then put in over a placeholder: one serialization, and the
        // hash is of the bytes a load will actually read. The placeholder is unique to this save,
        // so no captured state can contain it.
        string pending = "pending:" + Guid.NewGuid().ToString("N");
        SaveSlotInfo info = BuildHeader(slot);
        info.Checksum = pending;
        Godot.Collections.Dictionary header = info.ToDictionary();

        var root = new Godot.Collections.Dictionary
        {
            [SaveEnvelope.VersionKey] = SaveFormatVersion,
            [SaveEnvelope.TimestampKey] = Time.GetUnixTimeFromSystem(),
            [SaveEnvelope.ChecksumKey] = pending,
            [SaveEnvelope.HeaderKey] = header,
            [SaveEnvelope.ObjectsKey] = objects,
        };

        // ⚠️ Reading the document back before it is committed is also the last guard on the write:
        // a value the serializer emits and no parser accepts (a NaN that became the bare word nan)
        // would otherwise replace a good save with one that can never be loaded.
        string document = Json.Stringify(root, "\t");
        if (SaveChecksum.ComputeForEnvelope(document) is not { } checksum)
        {
            Log.Error($"Save slot '{slot}' serialized to a document that does not read back; previous save preserved.");
            return false;
        }

        document = document.Replace(pending, checksum, StringComparison.Ordinal);
        string headerDocument = Json.Stringify(header, "\t").Replace(pending, checksum, StringComparison.Ordinal);

        // One previous generation is kept, and only a sound one: moving a damaged save.json over
        // the backup would destroy the good copy the backup exists to be.
        SaveWriteQueue.Flush();
        bool keepPrevious = SaveBackup.ShouldRotate(SaveEnvelope.Read(ReadText(SlotSavePath(slot))).Health);

        // Windowed play hands the disk work to the write queue so a save never stalls a frame; the
        // outcome arrives through FinishSave. Gates and tooling take the synchronous path below,
        // which is the one whose log lines the save-audit probe pins.
        if (!SaveWriteQueue.RunsInline)
        {
            int objectCount = objects.Count;
            SaveWriteQueue.CommitSave(slot, SlotSavePath(slot), document, SlotHeaderPath(slot), headerDocument,
                LegacySlotPath(slot), keepPrevious, landed => FinishSave(slot, isAutosave, objectCount, landed));
            CaptureScreenshot(slot);
            return true;
        }

        if (!AtomicWrite(SlotSavePath(slot), document, keepPrevious))
        {
            return false;
        }

        // The header mirror is a read optimization for the slot browser; if it fails the save is
        // still valid (the header also lives inside the envelope), so warn rather than fail.
        //
        // ⚠️ BUT A STALE MIRROR IS WORSE THAN A MISSING ONE, so a failed write deletes it. These are
        // two independent atomic writes with no transaction across them: save.json has already been
        // committed above, so leaving the PREVIOUS save's header.json beside it means ReadHeader —
        // which prefers the mirror — answers every question about this save with the last one's
        // answers. That is not only a wrong row in the slot browser: the header carries the region
        // and the player transform that ApplySavedLocation restores, and the race that
        // StartLoadedGame spawns, so a stale mirror loads the new save and puts the player in the
        // old save's position, in the old save's region, as the old save's character.
        //
        // Deleting it costs a slower ReadHeader (it parses the envelope instead) and is always
        // correct, because the envelope carries the same header and ReadHeader already falls back
        // to it. ponytail: a mirror that can be rebuilt does not need a transaction, it needs to be
        // absent when it would lie.
        if (!AtomicWrite(SlotHeaderPath(slot), headerDocument))
        {
            string mirror = SlotHeaderPath(slot);
            if (FileAccess.FileExists(mirror) && DirAccess.RemoveAbsolute(mirror) != Error.Ok)
            {
                Log.Error($"Slot '{slot}' has a STALE header.json that could not be written or removed; " +
                          "the slot browser and a load will read the previous save's region, position " +
                          "and character until it is deleted by hand.");
            }
            else
            {
                Log.Warn($"Saved slot '{slot}' but could not write its header.json mirror; removed it " +
                         "so reads fall back to the header inside save.json.");
            }
        }

        CaptureScreenshot(slot);

        // One-time migration: once the directory layout holds the save, drop the legacy flat file.
        string legacy = LegacySlotPath(slot);
        if (FileAccess.FileExists(legacy))
        {
            DirAccess.RemoveAbsolute(legacy);
        }

        FinishSave(slot, isAutosave, objects.Count, landed: true);
        return true;
    }

    /// <summary>Announces how a save ended. Called on the main thread: straight away for a
    /// synchronous write, and from the write queue's completion for a queued one, which keeps
    /// "every start is followed by exactly one GameSavedEvent or SaveFailedEvent" true.</summary>
    private void FinishSave(string slot, bool isAutosave, int objectCount, bool landed)
    {
        if (!landed)
        {
            Log.Error($"Save slot '{slot}' could not be written; previous save preserved.");
            EventBus.Instance?.Publish(new SaveFailedEvent(slot, ReasonWriteFailed));
            return;
        }

        Log.Info($"Saved {objectCount} object(s) to slot '{slot}'.");
        EventBus.Instance?.Publish(new GameSavedEvent(slot, isAutosave));
    }

    /// <summary>Atomic write: stage to a temp file, then rename over the target so a crash
    /// mid-write can never truncate a previously-good file. With <paramref name="keepPrevious"/>
    /// the file being replaced is moved to <c>&lt;target&gt;.bak</c> between the two, so the slot
    /// always holds the generation before this one. A staged file that could not be committed is
    /// removed rather than left behind.</summary>
    private static bool AtomicWrite(string target, string contents, bool keepPrevious = false)
    {
        string temp = target + SaveBackup.TempSuffix;
        bool staged;
        using (FileAccess? file = FileAccess.Open(temp, FileAccess.ModeFlags.Write))
        {
            if (file == null)
            {
                Log.Error($"Could not open temp file '{temp}': {FileAccess.GetOpenError()}");
                return false;
            }

            file.StoreString(contents);
            file.Flush();
            staged = file.GetError() == Error.Ok;
            if (!staged)
            {
                Log.Error($"Could not write temp file '{temp}': {file.GetError()}; previous file preserved.");
            }
        }

        if (!staged)
        {
            RemoveOrphan(temp);
            return false;
        }

        // The window between the two renames has no primary file. A crash inside it is survivable
        // by construction: the previous generation is whole under its .bak name, SaveExists counts
        // it, and a load reads it (reporting that it did).
        string previous = target + SaveBackup.Suffix;
        bool keptPrevious = false;
        if (keepPrevious && FileAccess.FileExists(target))
        {
            Error kept = DirAccess.RenameAbsolute(target, previous);
            keptPrevious = kept == Error.Ok;
            if (!keptPrevious)
            {
                Log.Warn($"Could not keep '{target}' as a backup generation ({kept}); replacing it without one.");
            }
        }

        Error renamed = DirAccess.RenameAbsolute(temp, target);
        if (renamed != Error.Ok)
        {
            if (keptPrevious)
            {
                DirAccess.RenameAbsolute(previous, target);
            }

            Log.Error($"Could not commit '{target}' (rename failed: {renamed}); previous file preserved.");
            RemoveOrphan(temp);
            return false;
        }

        return true;
    }

    /// <summary>The slot browser's thumbnail (Phase 24C). <see cref="SaveThumbnailService"/> picks
    /// the frame (the live one in play, the one cached on the way into a menu otherwise) and encodes
    /// and writes it behind the save on the write queue. Best effort: it never breaks a save.</summary>
    private void CaptureScreenshot(string slot) =>
        SaveThumbnailService.Write(ScreenshotPath(slot), GetViewport());

    private SaveSlotInfo BuildHeader(string slot)
    {
        var info = new SaveSlotInfo
        {
            Slot = slot,
            TimestampUnix = Time.GetUnixTimeFromSystem(),
            PlaytimeSeconds = _playtimeSeconds,
            Kind = KindOfSlot(slot),
            FormatVersion = SaveFormatVersion,
            GameBuild = ProjectSettings.GetSetting("application/config/version", string.Empty).AsString(),
        };

        if (HeaderProvider?.Invoke() is { } fields)
        {
            if (fields.TryGetValue("region", out Variant region)) { info.Region = region.AsString(); }
            if (fields.TryGetValue("region_id", out Variant regionId)) { info.RegionId = regionId.AsString(); }
            if (fields.TryGetValue("player_x", out Variant px)) { info.PlayerX = (float)px.AsDouble(); info.HasLocation = true; }
            if (fields.TryGetValue("player_y", out Variant py)) { info.PlayerY = (float)py.AsDouble(); }
            if (fields.TryGetValue("player_z", out Variant pz)) { info.PlayerZ = (float)pz.AsDouble(); }
            if (fields.TryGetValue("player_yaw", out Variant yaw)) { info.PlayerYaw = (float)yaw.AsDouble(); }
            if (fields.TryGetValue("level", out Variant level)) { info.Level = level.AsInt32(); }
            if (fields.TryGetValue("corruption_tier", out Variant tier)) { info.CorruptionTier = tier.AsString(); }
            if (fields.TryGetValue("race_id", out Variant race)) { info.RaceId = race.AsString(); }
            if (fields.TryGetValue("char_name", out Variant name)) { info.CharacterName = name.AsString(); }
            if (fields.TryGetValue("appearance", out Variant appearance)) { info.Appearance = appearance.AsString(); }
            if (fields.TryGetValue("background", out Variant background)) { info.Background = background.AsString(); }
        }

        return info;
    }

    // --- Slot management ----------------------------------------------------

    /// <summary>Reads a slot's lightweight header (from <c>header.json</c>, falling back to the
    /// header embedded in <c>save.json</c>, then to the one embedded in the backup generation when
    /// the slot has nothing else). Null if the slot has no readable header.
    ///
    /// ⚠️ This is the cheap read and it trusts the files it finds. It does not notice a damaged
    /// <c>save.json</c>; <see cref="InspectSlot"/> does, and answers with the header of whichever
    /// generation a load will really read. A caller about to load a slot wants that one.</summary>
    public SaveSlotInfo? ReadHeader(string slot)
    {
        SaveWriteQueue.Flush();
        if (ReadJsonObject(SlotHeaderPath(slot)) is { } headerDoc)
        {
            SaveSlotInfo info = SaveSlotInfo.FromDictionary(headerDoc);
            info.Slot = slot;
            return info;
        }

        // Fall back to the header inside the full save (or a bare header for a legacy save), and
        // last to the backup generation's, which only ever has the embedded copy.
        string fullPath = FileAccess.FileExists(SlotSavePath(slot)) ? SlotSavePath(slot) : LegacySlotPath(slot);
        if ((ReadJsonObject(fullPath) ?? ReadJsonObject(SlotBackupPath(slot))) is { } root)
        {
            SaveSlotInfo info = SaveRead.Section(root, SaveEnvelope.HeaderKey) is { } embedded
                ? SaveSlotInfo.FromDictionary(embedded)
                : new SaveSlotInfo();
            info.Slot = slot;
            if (info.TimestampUnix == 0d)
            {
                info.TimestampUnix = SaveRead.Number(root, SaveEnvelope.TimestampKey);
            }

            return info;
        }

        return null;
    }

    /// <summary>Every save slot's header, for the load/continue browser.</summary>
    public IReadOnlyList<SaveSlotInfo> ListSlots()
    {
        SaveWriteQueue.Flush();
        var slots = new List<SaveSlotInfo>();
        using DirAccess? dir = DirAccess.Open(SaveDirectory);
        if (dir == null)
        {
            return slots;
        }

        foreach (string name in dir.GetDirectories())
        {
            if (SaveExists(name) && ReadHeader(name) is { } info)
            {
                slots.Add(info);
            }
        }

        // Flat saves are still supported by LoadGame; they must also be discoverable by Continue
        // and the slot browser. Prefer the directory layout when both forms exist.
        foreach (string file in dir.GetFiles())
        {
            if (!file.EndsWith(".json", StringComparison.Ordinal)) { continue; }
            string name = file.Substring(0, file.Length - 5);
            if (FileAccess.FileExists(SlotSavePath(name))) { continue; }
            if (ReadHeader(name) is { } info) { slots.Add(info); }
        }

        return slots;
    }

    /// <summary>Deletes a slot's directory (and any legacy flat file). Returns success.</summary>
    public bool DeleteSlot(string slot) => DeleteSlot(slot, out _);

    /// <summary>
    /// Deletes everything a slot owns: the save, its backup generation, the header, the thumbnail,
    /// any staged file, and the legacy flat file. True only when the slot existed and is now gone.
    /// <paramref name="failures"/> names each file that would not go, with the engine's reason, so
    /// a caller can say what is still on disk instead of showing an emptied slot that comes back.
    /// </summary>
    public bool DeleteSlot(string slot, out IReadOnlyList<string> failures)
    {
        SaveWriteQueue.Flush();
        var failed = new List<string>();
        failures = failed;
        bool existed = false;

        string directory = SlotDir(slot);
        if (DirAccess.DirExistsAbsolute(directory))
        {
            existed = true;
            using (DirAccess? dir = DirAccess.Open(directory))
            {
                if (dir == null)
                {
                    failed.Add($"{slot}/ ({DirAccess.GetOpenError()})");
                }
                else
                {
                    foreach (string file in dir.GetFiles())
                    {
                        Error removed = dir.Remove(file);
                        if (removed != Error.Ok)
                        {
                            failed.Add($"{slot}/{file} ({removed})");
                        }
                    }

                    // Nothing the game writes is a directory, so one here is removed only if empty.
                    foreach (string child in dir.GetDirectories())
                    {
                        Error removed = dir.Remove(child);
                        if (removed != Error.Ok)
                        {
                            failed.Add($"{slot}/{child}/ ({removed})");
                        }
                    }
                }
            }

            if (failed.Count == 0)
            {
                Error removed = DirAccess.RemoveAbsolute(directory);
                if (removed != Error.Ok)
                {
                    failed.Add($"{slot}/ ({removed})");
                }
            }
        }

        foreach (string flat in new[] { LegacySlotPath(slot), LegacySlotPath(slot) + SaveBackup.TempSuffix })
        {
            if (!FileAccess.FileExists(flat))
            {
                continue;
            }

            existed = true;
            Error removed = DirAccess.RemoveAbsolute(flat);
            if (removed != Error.Ok)
            {
                failed.Add($"{flat.Substring(flat.LastIndexOf('/') + 1)} ({removed})");
            }
        }

        if (failed.Count > 0)
        {
            Log.Warn($"Save slot '{slot}' was not fully deleted; still on disk: {string.Join(", ", failed)}.");
            return false;
        }

        if (existed)
        {
            Log.Info($"Deleted save slot '{slot}'.");
        }

        return existed;
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

    /// <summary>
    /// Loads the given slot and dispatches state to registered saveables. Returns false if the
    /// save could not be read <b>or if any saveable failed to restore</b> — see the partial-restore
    /// guard at the end. ⚠️ <b>Callers must not enter <c>GameState.Playing</c> on false</b>: the
    /// world is left partly restored and saving over it destroys the good file.
    /// </summary>
    public bool LoadGame(string slot)
    {
        // A load must never read a slot whose write is still queued.
        SaveWriteQueue.Flush();
        if (_operationInProgress)
        {
            Log.Warn($"Cannot load slot '{slot}' while another save/load is in progress.");
            return false;
        }
        _operationInProgress = true;
        try
        {
            return LoadGameCore(slot);
        }
        catch (Exception ex)
        {
            Log.Error($"Could not load slot '{slot}'; refusing a partial restore: {ex}");
            return false;
        }
        finally
        {
            _operationInProgress = false;
        }
    }

    private bool LoadGameCore(string slot)
    {
        LastLoadUsedBackup = false;

        // Prefer the new directory layout; fall back to a legacy flat file.
        string path = FileAccess.FileExists(SlotSavePath(slot)) ? SlotSavePath(slot) : LegacySlotPath(slot);
        bool hasPrimary = FileAccess.FileExists(path);
        if (!hasPrimary && !FileAccess.FileExists(SlotBackupPath(slot)))
        {
            Log.Warn($"Save slot '{slot}' does not exist.");
            return false;
        }

        // Everything that can refuse a save without touching live state is decided here, on the
        // file's text, by the same pure validation InspectSlot shows the browser: parse, shape,
        // version, checksum. Nothing below this block can discover that the document is bad.
        Error openError = Error.Ok;
        string? json = hasPrimary ? ReadText(path, out openError) : null;
        SaveEnvelope envelope = SaveEnvelope.Read(json);
        bool usedBackup = false;
        if (!envelope.IsLoadable)
        {
            string? backupJson = ReadText(SlotBackupPath(slot));
            SaveEnvelope backup = SaveEnvelope.Read(backupJson);
            if (SaveBackup.Choose(envelope.Health, backup.Health) == SaveSource.Backup)
            {
                Log.Warn($"Save slot '{slot}' cannot be loaded from its save file ({envelope.Fault}); " +
                         "loading the previous generation from save.json.bak instead.");
                json = backupJson;
                envelope = backup;
                usedBackup = true;
            }
        }

        if (!envelope.IsLoadable || json == null)
        {
            Log.Error(hasPrimary && json == null
                ? $"Could not read save slot '{slot}': {openError}"
                : FaultMessage(slot, envelope));
            return false;
        }

        if (envelope.NeedsMigration)
        {
            var notes = new List<string>();
            if (SaveMigrations.MigrateText(json, MigrationLookups(), notes) is not { } migrated)
            {
                Log.Error($"Save slot '{slot}' is version {envelope.Version} and could not be migrated to " +
                          $"{SaveFormatVersion}; refusing to load rather than feeding a partial document to live components.");
                return false;
            }

            foreach (string note in notes)
            {
                Log.Info($"Save slot '{slot}': {note}");
            }

            json = migrated;
        }

        // The engine's own parser builds what the saveables read, so every number and string
        // reaches them typed exactly as it always has.
        Variant parsed = Json.ParseString(json);
        if (parsed.VariantType != Variant.Type.Dictionary)
        {
            Log.Error($"Save slot '{slot}' is corrupt or not an object.");
            return false;
        }

        var root = parsed.AsGodotDictionary();
        if (SaveRead.Section(root, SaveEnvelope.ObjectsKey) is not { } objects)
        {
            Log.Error($"Save slot '{slot}' has no 'objects' section.");
            return false;
        }

        foreach (KeyValuePair<Variant, Variant> entry in objects)
        {
            if (entry.Value.VariantType != Variant.Type.Dictionary)
            {
                Log.Error($"Save slot '{slot}' entry '{entry.Key}' is not an object; refusing to load.");
                return false;
            }
        }

        // Continue this save's playtime from where it was last written, and keep the header around:
        // it also carries the region/transform the LocationApplier restores once the overlay lands.
        SaveSlotInfo? savedHeader = SaveRead.Section(root, SaveEnvelope.HeaderKey) is { } savedHeaderData
            ? SaveSlotInfo.FromDictionary(savedHeaderData)
            : null;
        _playtimeSeconds = savedHeader?.PlaytimeSeconds ?? 0d;

        int restored = 0;
        int reset = 0;
        int failures = 0;
        var claimed = new HashSet<string>();
        var deferred = new HashSet<string>();

        // Publish the snapshot so the Register hook can restore actors spawned during this load
        // (e.g. the PersistentSpawnDirector recreating saved actors as it is itself restored).
        _activeLoad = objects;
        _activeClaimed = claimed;
        _activeDeferred = deferred;
        _activeLoadFailures = 0;
        try
        {
            EventBus.Instance?.Publish(new GameLoadingEvent(slot));
            // Iterate a snapshot: a saveable's Load() may spawn actors that register new saveables,
            // which would otherwise mutate the live list mid-enumeration.
            foreach (ISaveable saveable in _saveables.ToArray())
            {
                // Earlier restores can despawn/rebuild actors. Their old wrappers may still be in
                // this snapshot, but no longer belong to the live collection.
                if (!_saveables.Contains(saveable)) { continue; }
                if (saveable is Node node && (!IsInstanceValid(node) || node.IsQueuedForDeletion())) { continue; }
                string id = saveable.SaveId;
                if (claimed.Contains(id))
                {
                    continue; // already restored via the spawn hook
                }

                if (!objects.TryGetValue(id, out Variant state) || state.VariantType != Variant.Type.Dictionary)
                {
                    // ⚠️ A MISSING ENTRY IS A RESET, NOT A SKIP. Leaving the saveable "at its current
                    // state" is only harmless when a load builds a fresh world — and a quickload does
                    // not: every live actor and component survives it. So loading a save written
                    // BEFORE a system existed (or before its SaveId was assigned) carried that
                    // system's state over from the timeline the player just abandoned: a companion
                    // still in the party, a shop still emptied, a shock still running, a holding
                    // still claimed. Nothing about the symptom points at the save.
                    //
                    // The reset is Load() with an empty document, which needs no new interface
                    // method and no per-component work: ISaveable.Load is already contractually
                    // required to REPLACE state rather than merge over it (CLAUDE.md §7), so an
                    // empty document is exactly "restore nothing" for every correct implementation.
                    // An implementation that throws on it is one that does not honour that contract,
                    // which is worth a warning of its own.
                    claimed.Add(id);
                    try
                    {
                        saveable.Load(new Godot.Collections.Dictionary());
                        reset++;
                    }
                    catch (Exception ex)
                    {
                        failures++;
                        Log.Error($"Saveable '{id}' has no entry in slot '{slot}' and threw while " +
                                  $"being reset to empty; it may keep state from the abandoned " +
                                  $"timeline. Its Load() must tolerate an empty document: {ex}");
                    }

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
            // Entries a streamed-out cell is holding for later (see ClaimDeferred) are not drift and
            // are not reported: warning on the healthy path is how a diagnostic teaches you to ignore it.
            foreach (System.Collections.Generic.KeyValuePair<Variant, Variant> entry in objects)
            {
                string id = entry.Key.AsString();
                if (!claimed.Contains(id) && !deferred.Contains(id))
                {
                    Log.Warn($"Save slot '{slot}' entry '{id}' had no live claimant on load (orphaned state).");
                }
            }
        }
        finally
        {
            failures += _activeLoadFailures;
            _activeLoadFailures = 0;
            _activeLoad = null;
            _activeClaimed = null;
            _activeDeferred = null;
        }

        Log.Info($"Loaded slot '{slot}'; restored {restored} object(s)" +
                 (reset > 0 ? $", reset {reset} the save did not carry" : string.Empty) +
                 (failures > 0 ? $" ({failures} failed)." : "."));

        // ⚠️ A PARTIAL RESTORE IS A FAILED LOAD, NOT A LOAD. Each saveable's exception is caught so
        // one bad entry cannot abort the other thirty-three, but this used to then return true and
        // publish GameLoadedEvent regardless — a load where every saveable threw was indistinguishable
        // from a clean one to every caller. The world proceeds half-restored and the next quest
        // completion autosaves over the good file. Report it instead; the caller abandons the session.
        if (failures > 0)
        {
            Log.Error($"Save slot '{slot}' restored {restored} object(s) but {failures} failed; the world is only partly restored. Treating the load as failed.");
            return false;
        }

        // Put the player back BEFORE announcing the load: MapScreen, RegionTransitionComponent and
        // the party widget all rebuild on GameLoadedEvent, and they should see the restored region
        // and position rather than wherever the player was standing when they pressed F9.
        // A pre-29.5 header has no location (HasLocation false) and is left alone.
        if (savedHeader is { HasLocation: true })
        {
            LocationApplier?.Invoke(savedHeader);
        }

        LastLoadUsedBackup = usedBackup;
        EventBus.Instance?.Publish(new GameLoadedEvent(slot));

        // Said to the player, not only to the log: they are standing in the save before the one
        // they chose, and the next save in this slot makes that permanent.
        if (usedBackup)
        {
            EventBus.Instance?.Publish(new Narrative.StoryToastRequestedEvent(RecoveredTitleKey, RecoveredDetailKey));
        }

        return true;
    }

    /// <summary>The exact refusal line for a save the envelope validation turned away. ⚠️ These
    /// strings are pinned by regex in <c>tools/world_quality_check.py</c> (the save-audit gate's
    /// expected errors); change one there in the same commit or the gate fails on its own probe.</summary>
    private static string FaultMessage(string slot, SaveEnvelope envelope) => envelope.Fault switch
    {
        SaveEnvelopeFault.Missing =>
            $"Save slot '{slot}' has no save file and no loadable backup generation.",

        // A missing "version" is not an old save, it is not one of ours. Every envelope this game
        // has ever written carries one, so its absence means a truncated write, a hand-edited file,
        // or some other JSON object entirely.
        SaveEnvelopeFault.NoVersion =>
            $"Save slot '{slot}' has no version field; refusing to load (it is not an Embervale save).",
        SaveEnvelopeFault.InvalidVersion =>
            $"Save slot '{slot}' has an invalid version; refusing to load.",
        SaveEnvelopeFault.Newer =>
            $"Save slot '{slot}' is version {envelope.Version}, newer than this build supports ({SaveFormatVersion}); refusing to load.",

        // ⚠️ ANYTHING BELOW THE FIRST FORMAT IS REFUSED RATHER THAN BEST-EFFORTED. There is no such
        // thing as a legitimate v0 Embervale save: nothing ever wrote one. A document that declares
        // one is hand-edited, foreign, or corrupt, and an unmigratable save must fail loudly, not
        // load in pieces.
        SaveEnvelopeFault.TooOld =>
            $"Save slot '{slot}' is version {envelope.Version}, older than the first format this game " +
            "wrote (1), and no migration step covers it; refusing to load " +
            "rather than feeding a partial document to live components.",
        SaveEnvelopeFault.NoObjects =>
            $"Save slot '{slot}' has no 'objects' section.",
        SaveEnvelopeFault.BadEntry =>
            $"Save slot '{slot}' entry '{envelope.FaultDetail}' is not an object; refusing to load.",
        SaveEnvelopeFault.ChecksumMismatch =>
            $"Save slot '{slot}' failed its integrity check (stored {envelope.StoredChecksum}, " +
            $"content is {envelope.ComputedChecksum}); refusing to load.",
        _ => $"Save slot '{slot}' is corrupt or not an object.",
    };

    /// <summary>The world data the v2 -> v3 migration step needs, read from the content databases.
    /// The chain itself is pure (<see cref="SaveMigrations"/>); this is its one seam to the game.</summary>
    private static SaveMigrationLookups MigrationLookups() => new()
    {
        StartSpawn = static () => World.RegionDatabase.Get(GameIds.Regions.EmberCrown) is { } region
            ? ((double)region.SpawnPoint.X, (double)region.SpawnPoint.Z)
            : ((double X, double Z)?)null,
        PropertyYard = static id => Housing.PropertyDatabase.Get(id) is { } property
            ? ((double)property.PlacementWorldCenter.X, (double)property.PlacementWorldCenter.Z)
            : ((double X, double Z)?)null,
    };
}
