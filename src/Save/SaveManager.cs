using System;
using System.Collections.Generic;
using Embervale.Core.Diagnostics;
using Embervale.Core.Events;
using Godot;

namespace Embervale.Save;

/// <summary>
/// Collects every active <see cref="ISaveable"/> and serializes them into a
/// single JSON document under <c>user://saves/</c>. Registered as the
/// <c>SaveManager</c> autoload.
///
/// The format is intentionally simple and forward-compatible: a versioned
/// envelope wrapping a map of <c>SaveId -&gt; state</c>. On load, each currently
/// registered saveable pulls its own entry, so the set of live objects drives
/// restoration. This scales to hundreds of persistent actors without bespoke
/// per-system save code.
/// </summary>
public sealed partial class SaveManager : Node
{
    private const int SaveFormatVersion = 1;
    private const string SaveDirectory = "user://saves";

    public static SaveManager Instance { get; private set; } = null!;

    private readonly List<ISaveable> _saveables = new();

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

    public void Register(ISaveable saveable)
    {
        if (!_saveables.Contains(saveable))
        {
            _saveables.Add(saveable);
        }
    }

    public void Unregister(ISaveable saveable)
    {
        _saveables.Remove(saveable);
    }

    public string SlotPath(string slot) => $"{SaveDirectory}/{slot}.json";

    public bool SaveExists(string slot) => FileAccess.FileExists(SlotPath(slot));

    /// <summary>Serializes all registered saveables to the given slot. Returns success.</summary>
    public bool SaveGame(string slot)
    {
        DirAccess.MakeDirRecursiveAbsolute(SaveDirectory);

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

        var root = new Godot.Collections.Dictionary
        {
            ["version"] = SaveFormatVersion,
            ["timestamp"] = Time.GetUnixTimeFromSystem(),
            ["objects"] = objects,
        };

        string json = Json.Stringify(root, "\t");

        // Atomic write: stage to a temp file, then rename over the target so a crash
        // mid-write can never truncate a previously-good save.
        string target = SlotPath(slot);
        string temp = $"{target}.tmp";
        using (FileAccess? file = FileAccess.Open(temp, FileAccess.ModeFlags.Write))
        {
            if (file == null)
            {
                Log.Error($"Could not open temp save '{temp}': {FileAccess.GetOpenError()}");
                return false;
            }

            file.StoreString(json);
        }

        Error renamed = DirAccess.RenameAbsolute(temp, target);
        if (renamed != Error.Ok)
        {
            Log.Error($"Could not commit save slot '{slot}' (rename failed: {renamed}); previous save preserved.");
            return false;
        }

        Log.Info($"Saved {objects.Count} object(s) to slot '{slot}'" + (failures > 0 ? $" ({failures} skipped)." : "."));
        EventBus.Instance?.Publish(new GameSavedEvent(slot));
        return true;
    }

    /// <summary>Loads the given slot and dispatches state to registered saveables.</summary>
    public bool LoadGame(string slot)
    {
        if (!SaveExists(slot))
        {
            Log.Warn($"Save slot '{slot}' does not exist.");
            return false;
        }

        using FileAccess? file = FileAccess.Open(SlotPath(slot), FileAccess.ModeFlags.Read);
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

        var objects = objectsVariant.AsGodotDictionary();
        int restored = 0;
        int failures = 0;
        var claimed = new HashSet<string>();
        foreach (ISaveable saveable in _saveables)
        {
            string id = saveable.SaveId;
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
