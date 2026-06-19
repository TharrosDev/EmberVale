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

        var objects = new Godot.Collections.Dictionary();
        foreach (ISaveable saveable in _saveables)
        {
            objects[saveable.SaveId] = saveable.Save();
        }

        var root = new Godot.Collections.Dictionary
        {
            ["version"] = SaveFormatVersion,
            ["timestamp"] = Time.GetUnixTimeFromSystem(),
            ["objects"] = objects,
        };

        string json = Json.Stringify(root, "\t");
        using FileAccess? file = FileAccess.Open(SlotPath(slot), FileAccess.ModeFlags.Write);
        if (file == null)
        {
            Log.Error($"Could not open save slot '{slot}': {FileAccess.GetOpenError()}");
            return false;
        }

        file.StoreString(json);
        Log.Info($"Saved {_saveables.Count} object(s) to slot '{slot}'.");
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
        if (!root.TryGetValue("objects", out Variant objectsVariant))
        {
            Log.Error($"Save slot '{slot}' has no 'objects' section.");
            return false;
        }

        var objects = objectsVariant.AsGodotDictionary();
        int restored = 0;
        foreach (ISaveable saveable in _saveables)
        {
            if (objects.TryGetValue(saveable.SaveId, out Variant state) &&
                state.VariantType == Variant.Type.Dictionary)
            {
                saveable.Load(state.AsGodotDictionary());
                restored++;
            }
        }

        Log.Info($"Loaded slot '{slot}'; restored {restored} object(s).");
        EventBus.Instance?.Publish(new GameLoadedEvent(slot));
        return true;
    }
}
