#if EMBERVALE_TOOLING
using System;
using System.Collections.Generic;
using System.IO;
using Embervale.Bootstrap;
using Embervale.Core;
using Embervale.Core.Events;
using Embervale.Entities;
using Embervale.Save;
using Godot;
using FileAccess = Godot.FileAccess;

namespace Embervale.Debugging;

/// <summary>Native save regression fixtures for tools/save_audit_probe.gd. Every disk write uses
/// a unique probe slot under the required isolated EMBERVALE_USER_DIR. Expected refusal paths
/// deliberately emit SaveManager errors; assertions and the process exit decide the result.</summary>
public partial class SaveAuditProbe : RefCounted
{
    private readonly List<string> _issues = new();
    private readonly List<ISaveable> _registered = new();
    private SaveManager _manager = null!;
    private string _slot = string.Empty;
    private int _loadedEvents;
    private int _savedEvents;

    private sealed class Fixture : ISaveable
    {
        public Fixture(string id) { SaveId = id; }
        public string SaveId { get; }
        public int Value { get; set; } = 12;
        public bool ThrowOnSave { get; set; }
        public bool ThrowOnLoad { get; set; }
        public Action<Godot.Collections.Dictionary>? OnLoad { get; set; }
        public Godot.Collections.Dictionary Save() => ThrowOnSave
            ? throw new InvalidOperationException("Expected save-audit capture failure")
            : new Godot.Collections.Dictionary { ["value"] = Value };
        public void Load(Godot.Collections.Dictionary data)
        {
            if (ThrowOnLoad) { throw new InvalidOperationException("Expected save-audit restore failure"); }
            Value = data.TryGetValue("value", out Variant value) ? value.AsInt32() : 0;
            OnLoad?.Invoke(data);
        }
    }

    public string[] Run(Node parent)
    {
        string isolation = OS.GetEnvironment("EMBERVALE_USER_DIR");
        if (string.IsNullOrWhiteSpace(isolation) || !Path.IsPathFullyQualified(isolation))
        {
            return ["save audit requires an absolute, isolated EMBERVALE_USER_DIR"];
        }
        _manager = SaveManager.Instance;
        _slot = "save_audit_" + Guid.NewGuid().ToString("N");
        EventBus.Instance.Subscribe<GameLoadedEvent>(Loaded);
        EventBus.Instance.Subscribe<GameSavedEvent>(Saved);
        Func<Godot.Collections.Dictionary>? priorHeader = _manager.HeaderProvider;
        Action<SaveSlotInfo>? priorLocation = _manager.LocationApplier;
        try
        {
            CaptureAndHeaderChecks();
            RestoreChecks();
            SpawnChecks(parent);
            LegacyChecks();
        }
        catch (Exception ex)
        {
            _issues.Add("unexpected probe exception: " + ex);
        }
        finally
        {
            foreach (ISaveable fixture in _registered) { _manager.Unregister(fixture); }
            _registered.Clear();
            _manager.HeaderProvider = priorHeader;
            _manager.LocationApplier = priorLocation;
            EventBus.Instance.Unsubscribe<GameLoadedEvent>(Loaded);
            EventBus.Instance.Unsubscribe<GameSavedEvent>(Saved);
            _manager.DeleteSlot(_slot);
            _manager.DeleteSlot(_slot + "_legacy");
        }
        return _issues.ToArray();
    }

    private void CaptureAndHeaderChecks()
    {
        var fixture = new Fixture("audit.state");
        Register(fixture);
        _manager.HeaderProvider = () => new Godot.Collections.Dictionary
        {
            ["race_id"] = "race.umbral", ["char_name"] = "Audit Wanderer",
            ["appearance"] = "appearance.audit_one;appearance.audit_two", ["background"] = "Audit background",
            ["region_id"] = "region.frostfang", ["player_x"] = 10f, ["player_y"] = 2f,
            ["player_z"] = 14f, ["player_yaw"] = 0.7f,
        };
        Check(_manager.SaveGame(_slot), "baseline capture failed");
        SaveSlotInfo? header = _manager.ReadHeader(_slot);
        Check(header is { RaceId: "race.umbral", CharacterName: "Audit Wanderer", HasLocation: true },
            "gameplay header lost character race/name or transform");
        Check(header is { Appearance: "appearance.audit_one;appearance.audit_two", Background: "Audit background" },
            "gameplay header lost creator appearance/background");
        string good = ReadSave();
        int savedBefore = _savedEvents;
        fixture.ThrowOnSave = true;
        Check(!_manager.SaveGame(_slot), "throwing Save() reported success");
        Check(ReadSave() == good && _savedEvents == savedBefore,
            "failed capture overwrote the good save or published GameSavedEvent");
        fixture.ThrowOnSave = false;

        var duplicate = new Fixture(fixture.SaveId);
        Register(duplicate);
        Check(!_manager.SaveGame(_slot), "duplicate SaveId reported success");
        Check(ReadSave() == good && _savedEvents == savedBefore, "duplicate SaveId overwrote progress");
        Unregister(duplicate);

        string temp = _manager.SlotPath(_slot) + ".tmp";
        DirAccess.MakeDirRecursiveAbsolute(temp); // force staging failure without changing the good file
        try
        {
            Check(!_manager.SaveGame(_slot), "unwritable staging target reported success");
            Check(ReadSave() == good && _savedEvents == savedBefore, "staging failure overwrote progress");
        }
        finally { DirAccess.RemoveAbsolute(temp); }

        var noLocation = new SaveSlotInfo { HasLocation = false };
        Check(!SaveSlotInfo.FromDictionary(noLocation.ToDictionary()).HasLocation,
            "serializing an absent location invented a transform at the world origin");
        _manager.HeaderProvider = () => new Godot.Collections.Dictionary();
        Check(_manager.SaveGame(_slot) && _manager.ReadHeader(_slot) is { HasLocation: false },
            "a save without a live player invented a location");
        Unregister(fixture);
    }

    private void RestoreChecks()
    {
        var fixture = new Fixture("audit.state");
        Register(fixture);
        int loadedBefore = _loadedEvents;
        WriteEnvelope(3.5d, new Godot.Collections.Dictionary { [fixture.SaveId] = fixture.Save() });
        Check(!_manager.LoadGame(_slot) && fixture.Value == 12 && _loadedEvents == loadedBefore,
            "fractional version was accepted or changed live state");
        WriteEnvelope(3, new Godot.Collections.Dictionary { [fixture.SaveId] = "corrupt" });
        Check(!_manager.LoadGame(_slot) && fixture.Value == 12 && _loadedEvents == loadedBefore,
            "malformed object state was treated as a missing entry and reset live state");

        WriteEnvelope(3, new Godot.Collections.Dictionary());
        Check(_manager.LoadGame(_slot) && fixture.Value == 0, "missing state did not replace live state with empty");
        bool nestedSave = true;
        fixture.OnLoad = _ => nestedSave = _manager.SaveGame(_slot);
        WriteEnvelope(3, new Godot.Collections.Dictionary { [fixture.SaveId] = new Godot.Collections.Dictionary { ["value"] = 17 } });
        string beforeLoad = ReadSave();
        Check(_manager.LoadGame(_slot) && fixture.Value == 17 && !nestedSave && ReadSave() == beforeLoad,
            "a restore callback could overwrite its in-flight source save");
        fixture.OnLoad = null;

        foreach (int version in new[] { 1, 2 })
        {
            int applied = 0;
            _manager.LocationApplier = _ => applied++;
            WriteEnvelope(version, new Godot.Collections.Dictionary { [fixture.SaveId] = new Godot.Collections.Dictionary { ["value"] = 21 } },
                new Godot.Collections.Dictionary { ["player_x"] = 1f, ["player_y"] = 2f, ["player_z"] = 3f, ["player_yaw"] = 0f });
            Check(_manager.LoadGame(_slot) && fixture.Value == 21 && applied == 0,
                $"v{version} migration lost non-spatial progress or restored obsolete coordinates");
        }

        _manager.LocationApplier = _ => throw new InvalidOperationException("Expected save-audit location failure");
        WriteEnvelope(3, new Godot.Collections.Dictionary { [fixture.SaveId] = fixture.Save() },
            new Godot.Collections.Dictionary { ["player_x"] = 1f, ["player_y"] = 2f, ["player_z"] = 3f, ["player_yaw"] = 0f });
        loadedBefore = _loadedEvents;
        Check(!_manager.LoadGame(_slot) && _loadedEvents == loadedBefore,
            "location restore failure escaped or published GameLoadedEvent");
        _manager.LocationApplier = null;
        Unregister(fixture);

        var child = new Fixture("audit.child") { ThrowOnLoad = true };
        var spawner = new Fixture("audit.spawner") { OnLoad = _ => Register(child) };
        Register(spawner);
        WriteEnvelope(3, new Godot.Collections.Dictionary { [spawner.SaveId] = spawner.Save(), [child.SaveId] = child.Save() });
        loadedBefore = _loadedEvents;
        Check(!_manager.LoadGame(_slot) && _loadedEvents == loadedBefore,
            "failure in a saveable registered during restoration was reported as a successful load");
        Unregister(child);
        Unregister(spawner);

        var victim = new Fixture("audit.victim") { ThrowOnLoad = true };
        var remover = new Fixture("audit.remover") { OnLoad = _ => Unregister(victim) };
        Register(remover);
        Register(victim);
        WriteEnvelope(3, new Godot.Collections.Dictionary { [remover.SaveId] = remover.Save() });
        Check(_manager.LoadGame(_slot), "a saveable unregistered by an earlier restore was still invoked from the stale snapshot");
        Unregister(remover);
    }

    private void SpawnChecks(Node parent)
    {
        const string template = "audit.actor";
        PersistentActorRegistry.Register(template, _ => new Entity());
        var host = new WorldHost();
        parent.AddChild(host);
        var director = new PersistentSpawnDirector();
        host.AddChild(director);
        try
        {
            Check(director.Spawn(template, template + "#1", Vector3.Zero) != null, "spawn fixture could not be built");
            IEntity? fresh = director.Spawn(template, string.Empty, Vector3.Zero);
            Check(fresh?.PersistentId == template + "#2" && director.TrackedIds.Count == 2,
                "automatic spawn identity collided with a restored identity");
            director.Load(new Godot.Collections.Dictionary());
            Check(director.TrackedIds.Count == 0 && director.Save()["actors"].AsGodotArray().Count == 0,
                "empty spawn manifest retained actors from the abandoned timeline");

            WriteEnvelope(3, new Godot.Collections.Dictionary { [director.SaveId] = new Godot.Collections.Dictionary
            {
                ["actors"] = new Godot.Collections.Array { new Godot.Collections.Dictionary { ["pid"] = "audit.missing", ["tid"] = "audit.unknown_template" } },
            } });
            int loadedBefore = _loadedEvents;
            Check(!_manager.LoadGame(_slot) && _loadedEvents == loadedBefore,
                "unrecreatable persistent actor silently disappeared during a successful load");
        }
        finally { host.Free(); }
    }

    private void LegacyChecks()
    {
        string legacySlot = _slot + "_legacy";
        string path = UserDataPaths.Resolve("saves") + "/" + legacySlot + ".json";
        using (FileAccess file = FileAccess.Open(path, FileAccess.ModeFlags.Write))
        {
            file.StoreString(Json.Stringify(new Godot.Collections.Dictionary
            {
                ["version"] = 3, ["header"] = new Godot.Collections.Dictionary { ["timestamp"] = 5d },
                ["objects"] = new Godot.Collections.Dictionary(),
            }));
            file.Flush();
        }
        bool found = false;
        foreach (SaveSlotInfo info in _manager.ListSlots()) { found |= info.Slot == legacySlot; }
        Check(found, "legacy flat save was hidden from Continue and the slot browser");
    }

    private void Register(ISaveable fixture) { _registered.Add(fixture); _manager.Register(fixture); }
    private void Unregister(ISaveable fixture) { _manager.Unregister(fixture); _registered.Remove(fixture); }
    private void Loaded(GameLoadedEvent _) => _loadedEvents++;
    private void Saved(GameSavedEvent _) => _savedEvents++;
    private void Check(bool condition, string issue) { if (!condition) { _issues.Add(issue); } }
    private string ReadSave() => FileAccess.GetFileAsString(_manager.SlotPath(_slot));
    private void WriteEnvelope(double version, Godot.Collections.Dictionary objects, Godot.Collections.Dictionary? header = null)
    {
        using FileAccess file = FileAccess.Open(_manager.SlotPath(_slot), FileAccess.ModeFlags.Write);
        file.StoreString(Json.Stringify(new Godot.Collections.Dictionary
        {
            ["version"] = version, ["header"] = header ?? new Godot.Collections.Dictionary(), ["objects"] = objects,
        }));
        file.Flush();
    }
}
#endif
