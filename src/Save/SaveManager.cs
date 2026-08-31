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
    private const int SaveFormatVersion = 2;
    private const string SaveDirectory = "user://saves";

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
    /// slot browser went through <c>GameBootstrap.StartLoadedGame</c>, which applied the header's
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
        if (!AtomicWrite(SlotHeaderPath(slot), Json.Stringify(header, "\t")))
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
            if (fields.TryGetValue("region_id", out Variant regionId)) { info.RegionId = regionId.AsString(); }
            if (fields.TryGetValue("player_x", out Variant px)) { info.PlayerX = (float)px.AsDouble(); info.HasLocation = true; }
            if (fields.TryGetValue("player_y", out Variant py)) { info.PlayerY = (float)py.AsDouble(); }
            if (fields.TryGetValue("player_z", out Variant pz)) { info.PlayerZ = (float)pz.AsDouble(); }
            if (fields.TryGetValue("player_yaw", out Variant yaw)) { info.PlayerYaw = (float)yaw.AsDouble(); }
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

    /// <summary>
    /// Loads the given slot and dispatches state to registered saveables. Returns false if the
    /// save could not be read <b>or if any saveable failed to restore</b> — see the partial-restore
    /// guard at the end. ⚠️ <b>Callers must not enter <c>GameState.Playing</c> on false</b>: the
    /// world is left partly restored and saving over it destroys the good file.
    /// </summary>
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

        // A missing "version" is not an old save, it is not one of ours. Every envelope this game has
        // ever written carries one, so the key's absence means a truncated write, a hand-edited file, or
        // some other JSON object entirely — and the migration path below would wave it through as
        // "version 0, older, best effort" and start feeding fragments to live components. Refused here so
        // the only unversioned outcome is a clean failure.
        if (!root.TryGetValue("version", out Variant versionVariant) ||
            versionVariant.VariantType is not (Variant.Type.Int or Variant.Type.Float))
        {
            Log.Error($"Save slot '{slot}' has no version field; refusing to load (it is not an Embervale save).");
            return false;
        }

        int version = versionVariant.AsInt32();
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

        // Continue this save's playtime from where it was last written, and keep the header around:
        // it also carries the region/transform the LocationApplier restores once the overlay lands.
        SaveSlotInfo? savedHeader = null;
        if (root.TryGetValue("header", out Variant headerVariant) && headerVariant.VariantType == Variant.Type.Dictionary)
        {
            savedHeader = SaveSlotInfo.FromDictionary(headerVariant.AsGodotDictionary());
            _playtimeSeconds = savedHeader.PlaytimeSeconds;
        }

        var objects = objectsVariant.AsGodotDictionary();
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

        // version < SaveFormatVersion: walk forward one step at a time.
        if (version == 1)
        {
            MigrateV1ToV2(slot, root);
            version = 2;
        }

        if (version == SaveFormatVersion)
        {
            return true;
        }

        // ⚠️ ANYTHING STILL BELOW THE FIRST FORMAT IS REFUSED RATHER THAN BEST-EFFORTED. There is no
        // such thing as a legitimate v0 Embervale save — nothing ever wrote one. A document that
        // declares a version below the first is hand-edited, foreign, or corrupt, and the old branch
        // waved all three through with a warning and started feeding their fragments to live
        // components. An unmigratable save must fail loudly, not load in pieces.
        Log.Error($"Save slot '{slot}' is version {version}, older than the first format this game " +
                  $"wrote (1), and no migration step covers it; refusing to load " +
                  "rather than feeding a partial document to live components.");
        return false;
    }

    /// <summary>
    /// v1 -> v2: THE WORLD MOVED UNDER THE SAVE (the 2026-08-29 geography overhaul).
    ///
    /// Every world coordinate a v1 document holds was written against a lattice that no longer
    /// exists: the Ember Crown's cells all moved except the town hub, Frostfang Reach was lifted
    /// out of the Ember Crown's coordinate space entirely (its old points are now inside the arena
    /// and the northern wilds), and the ground stopped being flat, so even an unmoved X/Z can have
    /// eight metres of hillside over it. A saved position is therefore not merely stale — it can
    /// put the player inside terrain or in the void, which is exactly the failure this step exists
    /// to make impossible.
    ///
    /// Three things carry world coordinates that a player can be TELEPORTED to, and all three are
    /// discarded rather than guessed at:
    ///   the header transform  — dropped, so <c>ApplySavedLocation</c> falls through to the region's
    ///                           own SpawnPoint, which is authored, on the ground, and always valid;
    ///   the fast-travel net   — dropped, because a jump to a v1 landing point is a jump into a hill
    ///                           (the posts themselves are unmoved and can be re-attuned by walking
    ///                            to them, and <c>FastTravelService.Refresh</c> keeps them honest
    ///                            from here on);
    ///   the map's saved pins  — dropped, because they are the positions of cells that have moved.
    ///                           Every one of them re-registers the moment its cell loads.
    ///
    /// ⚠️ EVERYTHING ELSE IS KEPT ON PURPOSE. Quests, flags, inventory, perks, reputation, the
    /// economy, blessings and companion rosters carry no coordinates and a player's progress is not
    /// a casualty of a terrain change. Persistent actor positions are kept too: they are dropped
    /// loot and world caches inside cells that mostly did not move relative to their own contents,
    /// and losing a chest is worse than a chest sitting a metre low.
    /// </summary>
    private static void MigrateV1ToV2(string slot, Godot.Collections.Dictionary root)
    {
        int cleared = 0;
        if (root.TryGetValue("header", out Variant headerV) &&
            headerV.VariantType == Variant.Type.Dictionary)
        {
            var header = headerV.AsGodotDictionary();
            foreach (string key in new[] { "player_x", "player_y", "player_z", "player_yaw" })
            {
                if (header.ContainsKey(key))
                {
                    header.Remove(key);
                    cleared++;
                }
            }
        }

        if (root.TryGetValue("state", out Variant stateV) &&
            stateV.VariantType == Variant.Type.Dictionary)
        {
            var state = stateV.AsGodotDictionary();
            foreach (string key in new[] { "fasttravel", "map" })
            {
                if (state.ContainsKey(key))
                {
                    state.Remove(key);
                    cleared++;
                }
            }
        }

        root["version"] = 2;
        Log.Info($"Save slot '{slot}': migrated v1 -> v2, discarding {cleared} pre-overhaul " +
                 "coordinate record(s). The player lands at the region's spawn point and " +
                 "fast-travel posts need re-attuning; nothing else was touched.");
    }
}
