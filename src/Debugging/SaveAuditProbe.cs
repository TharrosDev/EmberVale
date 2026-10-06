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
    private int _startedEvents;
    private readonly List<string> _failedReasons = new();

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
        EventBus.Instance.Subscribe<SaveStartedEvent>(Started);
        EventBus.Instance.Subscribe<SaveFailedEvent>(Failed);
        Func<Godot.Collections.Dictionary>? priorHeader = _manager.HeaderProvider;
        Action<SaveSlotInfo>? priorLocation = _manager.LocationApplier;
        try
        {
            CaptureAndHeaderChecks();
            IntegrityChecks();
            RestoreChecks();
            SpawnChecks(parent);
            LegacyChecks();
            DeleteChecks();
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
            EventBus.Instance.Unsubscribe<SaveStartedEvent>(Started);
            EventBus.Instance.Unsubscribe<SaveFailedEvent>(Failed);
            _manager.ClearSaveBlocks();
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
        Check(_manager.SaveGame(_slot) && _startedEvents == 1 && _savedEvents == 1 && _failedReasons.Count == 0,
            "a clean save did not publish exactly one SaveStartedEvent and one GameSavedEvent");
        SaveSlotInfo? header = _manager.ReadHeader(_slot);
        Check(header is { FormatVersion: 4, Kind: SaveKind.Manual } && header.GameBuild.Length > 0 &&
              header.Checksum.StartsWith(SaveChecksum.Prefix, StringComparison.Ordinal),
            "header did not record the format version, save kind, game build and content checksum");
        Check(header is { RaceId: "race.umbral", CharacterName: "Audit Wanderer", HasLocation: true },
            "gameplay header lost character race/name or transform");
        Check(header is { Appearance: "appearance.audit_one;appearance.audit_two", Background: "Audit background" },
            "gameplay header lost creator appearance/background");
        string good = ReadSave();
        int savedBefore = _savedEvents;
        fixture.ThrowOnSave = true;
        Check(!_manager.SaveGame(_slot), "throwing Save() reported success");
        Check(_startedEvents == 2 && _failedReasons.Count == 1 && _failedReasons[0] == SaveManager.ReasonWriteFailed,
            "a failed capture did not publish SaveStartedEvent followed by SaveFailedEvent(save.failed.write)");
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

    /// <summary>Checksum, the one backup generation, fallback selection and save blocks.</summary>
    private void IntegrityChecks()
    {
        var fixture = new Fixture("audit.state") { Value = 31 };
        Register(fixture);
        string backupPath = _manager.SlotPath(_slot) + SaveBackup.Suffix;
        Check(_manager.SaveGame(_slot), "generation one could not be saved");
        fixture.Value = 32;
        Check(_manager.SaveGame(_slot) && FileAccess.FileExists(backupPath),
            "replacing a save did not keep the previous generation as save.json.bak");
        Check(!FileAccess.FileExists(_manager.SlotPath(_slot) + SaveBackup.TempSuffix),
            "a committed save left its staged .tmp file behind");

        SaveSlotInfo? healthy = _manager.InspectSlot(_slot);
        Check(healthy is { Health: SaveHealth.Ok, PrimaryHealth: SaveHealth.Ok, RecoveredFromBackup: false, HasBackup: true, FormatVersion: 4 },
            "InspectSlot did not report a healthy save with a loadable backup");
        Check(healthy != null && healthy.Checksum.Length > 0 && _manager.ReadHeader(_slot)?.Checksum == healthy.Checksum &&
              SaveChecksum.ComputeForEnvelope(ReadSave()) == healthy.Checksum,
            "envelope, header mirror and recomputed content checksum disagree");

        // One changed value with the stored checksum left alone: the file still parses and still
        // has the right shape, so only the checksum can notice.
        string good = ReadSave();
        string tampered = good.Replace("\"value\": 32", "\"value\": 99");
        Check(tampered != good, "probe could not tamper with the saved value (serialization changed shape)");
        WriteRaw(_manager.SlotPath(_slot), tampered);
        Check(_manager.InspectSlot(_slot) is { Health: SaveHealth.Ok, PrimaryHealth: SaveHealth.Corrupt, RecoveredFromBackup: true },
            "a tampered save passed its integrity check, or its backup was not offered");
        int loadedBefore = _loadedEvents;
        Check(_manager.LoadGame(_slot) && fixture.Value == 31 && _manager.LastLoadUsedBackup && _loadedEvents == loadedBefore + 1,
            "a damaged save did not fall back to its backup generation and say so");

        // Saving over a damaged primary must not move it on top of the good backup.
        string backupBefore = FileAccess.GetFileAsString(backupPath);
        fixture.Value = 33;
        Check(_manager.SaveGame(_slot) && FileAccess.GetFileAsString(backupPath) == backupBefore,
            "a damaged save was rotated over the good backup generation");
        Check(_manager.LoadGame(_slot) && fixture.Value == 33 && !_manager.LastLoadUsedBackup,
            "a healthy save was not loaded from its own file");

        // Interrupted between moving the old save aside and committing the new one: only the backup.
        DirAccess.RemoveAbsolute(_manager.SlotPath(_slot));
        Check(_manager.SaveExists(_slot) && _manager.InspectSlot(_slot) is { Health: SaveHealth.Ok, PrimaryHealth: SaveHealth.Missing, RecoveredFromBackup: true } &&
              _manager.LoadGame(_slot) && fixture.Value == 31 && _manager.LastLoadUsedBackup,
            "a slot holding only its backup generation was not loadable");

        // No backup to fall back to: the checksum refusal is the outcome, and nothing live changes.
        DirAccess.RemoveAbsolute(backupPath);
        WriteRaw(_manager.SlotPath(_slot), tampered);
        loadedBefore = _loadedEvents;
        Check(_manager.InspectSlot(_slot) is { Health: SaveHealth.Corrupt, RecoveredFromBackup: false, HasBackup: false } &&
              !_manager.LoadGame(_slot) && fixture.Value == 31 && _loadedEvents == loadedBefore,
            "a tampered save with no backup was loaded or changed live state");

        // Both generations unreadable.
        WriteRaw(_manager.SlotPath(_slot), "not json {");
        WriteRaw(backupPath, "[1, 2, 3]");
        Check(_manager.InspectSlot(_slot) is { Health: SaveHealth.Corrupt } && !_manager.LoadGame(_slot) &&
              fixture.Value == 31 && _loadedEvents == loadedBefore,
            "two unreadable generations were not refused cleanly");
        DirAccess.RemoveAbsolute(backupPath);

        // A newer save is never bypassed for an older backup.
        WriteEnvelope(4, new Godot.Collections.Dictionary { [fixture.SaveId] = fixture.Save() });
        WriteRaw(backupPath, ReadSave());
        WriteEnvelope(SaveManager.CurrentFormatVersion + 1, new Godot.Collections.Dictionary());
        Check(_manager.InspectSlot(_slot) is { Health: SaveHealth.Newer, RecoveredFromBackup: false } && !_manager.LoadGame(_slot),
            "a save from a newer build was bypassed in favour of its older backup");
        DirAccess.RemoveAbsolute(backupPath);

        // A live block refuses the save with the block's own reason and never starts it.
        WriteEnvelope(4, new Godot.Collections.Dictionary { [fixture.SaveId] = fixture.Save() });
        string before = ReadSave();
        int startedBefore = _startedEvents;
        _failedReasons.Clear();
        IDisposable block = _manager.PushSaveBlock("audit.block");
        Check(!_manager.CanSaveNow(out string reason) && reason == "audit.block" && !_manager.SaveGame(_slot) &&
              !_manager.SaveGame(_slot, isAutosave: true) && ReadSave() == before && _startedEvents == startedBefore &&
              _failedReasons.Count == 2 && _failedReasons[0] == "audit.block",
            "a save block did not refuse manual and automatic saves with its own reason");
        block.Dispose();
        block.Dispose();
        Check(_manager.CanSaveNow(out _) && _manager.SaveGame(_slot), "releasing the save block did not allow saving again");
        DirAccess.RemoveAbsolute(backupPath);
        Unregister(fixture);
    }

    private void RestoreChecks()
    {
        var fixture = new Fixture("audit.state");
        Register(fixture);
        int loadedBefore = _loadedEvents;
        WriteEnvelope(4.5d, new Godot.Collections.Dictionary { [fixture.SaveId] = fixture.Save() });
        Check(!_manager.LoadGame(_slot) && fixture.Value == 12 && _loadedEvents == loadedBefore,
            "fractional version was accepted or changed live state");
        WriteEnvelope(4, new Godot.Collections.Dictionary { [fixture.SaveId] = "corrupt" });
        Check(!_manager.LoadGame(_slot) && fixture.Value == 12 && _loadedEvents == loadedBefore,
            "malformed object state was treated as a missing entry and reset live state");

        WriteEnvelope(4, new Godot.Collections.Dictionary());
        Check(_manager.LoadGame(_slot) && fixture.Value == 0, "missing state did not replace live state with empty");
        bool nestedSave = true;
        fixture.OnLoad = _ => nestedSave = _manager.SaveGame(_slot);
        WriteEnvelope(4, new Godot.Collections.Dictionary { [fixture.SaveId] = new Godot.Collections.Dictionary { ["value"] = 17 } });
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

        // v3 -> v4: a set-piece entry keyed by its scene path reaches the piece under its stable id,
        // and the v3 header transform (written against the current world) is kept and applied.
        var piece = new Fixture(SetPieceSaveIds.Build("res://scenes/regions/audit/cell.tscn", "AuditRaid"));
        Register(piece);
        int appliedV3 = 0;
        _manager.LocationApplier = _ => appliedV3++;
        WriteEnvelope(3, new Godot.Collections.Dictionary
            {
                [fixture.SaveId] = new Godot.Collections.Dictionary { ["value"] = 23 },
                ["setpiece:res://scenes/regions/audit/cell.tscn#AuditRaid"] = new Godot.Collections.Dictionary { ["value"] = 24 },
            },
            new Godot.Collections.Dictionary { ["player_x"] = 1f, ["player_y"] = 2f, ["player_z"] = 3f, ["player_yaw"] = 0f });
        Check(piece.SaveId == "setpiece:audit/cell#AuditRaid" && _manager.LoadGame(_slot) && fixture.Value == 23 &&
              piece.Value == 24 && appliedV3 == 1,
            "v3 migration did not carry a scene-path set-piece entry to its stable id, or dropped a valid transform");
        Unregister(piece);

        _manager.LocationApplier = _ => throw new InvalidOperationException("Expected save-audit location failure");
        WriteEnvelope(4, new Godot.Collections.Dictionary { [fixture.SaveId] = fixture.Save() },
            new Godot.Collections.Dictionary { ["player_x"] = 1f, ["player_y"] = 2f, ["player_z"] = 3f, ["player_yaw"] = 0f });
        loadedBefore = _loadedEvents;
        Check(!_manager.LoadGame(_slot) && _loadedEvents == loadedBefore,
            "location restore failure escaped or published GameLoadedEvent");
        _manager.LocationApplier = null;
        Unregister(fixture);

        var child = new Fixture("audit.child") { ThrowOnLoad = true };
        var spawner = new Fixture("audit.spawner") { OnLoad = _ => Register(child) };
        Register(spawner);
        WriteEnvelope(4, new Godot.Collections.Dictionary { [spawner.SaveId] = spawner.Save(), [child.SaveId] = child.Save() });
        loadedBefore = _loadedEvents;
        Check(!_manager.LoadGame(_slot) && _loadedEvents == loadedBefore,
            "failure in a saveable registered during restoration was reported as a successful load");
        Unregister(child);
        Unregister(spawner);

        var victim = new Fixture("audit.victim") { ThrowOnLoad = true };
        var remover = new Fixture("audit.remover") { OnLoad = _ => Unregister(victim) };
        Register(remover);
        Register(victim);
        WriteEnvelope(4, new Godot.Collections.Dictionary { [remover.SaveId] = remover.Save() });
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

            WriteEnvelope(4, new Godot.Collections.Dictionary { [director.SaveId] = new Godot.Collections.Dictionary
            {
                ["actors"] = new Godot.Collections.Array { new Godot.Collections.Dictionary { ["pid"] = "audit.missing", ["tid"] = "audit.unknown_template" } },
            } });
            int loadedBefore = _loadedEvents;
            Check(_manager.LoadGame(_slot) && _loadedEvents == loadedBefore + 1 && director.TrackedIds.Count == 0,
                "a persistent actor whose template no longer exists failed the whole load instead of being skipped");
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

    /// <summary>Deleting a slot removes every file it owns and says so; an absent slot is not a failure.</summary>
    private void DeleteChecks()
    {
        string path = _manager.SlotPath(_slot);
        string directory = path.Substring(0, path.Length - "/save.json".Length);
        WriteRaw(path + SaveBackup.Suffix, "{}");
        WriteRaw(path + SaveBackup.TempSuffix, "{}");
        Check(_manager.DeleteSlot(_slot, out IReadOnlyList<string> failures) && failures.Count == 0 &&
              !DirAccess.DirExistsAbsolute(directory) && !_manager.SaveExists(_slot),
            "deleting a slot left its backup, staged file or directory behind");
        Check(!_manager.DeleteSlot(_slot, out failures) && failures.Count == 0 &&
              _manager.InspectSlot(_slot) is { Health: SaveHealth.Missing },
            "deleting an absent slot reported a failure, or the slot still inspects as present");
    }

    private void Register(ISaveable fixture) { _registered.Add(fixture); _manager.Register(fixture); }
    private void Unregister(ISaveable fixture) { _manager.Unregister(fixture); _registered.Remove(fixture); }
    private void Loaded(GameLoadedEvent _) => _loadedEvents++;
    private void Saved(GameSavedEvent _) => _savedEvents++;
    private void Started(SaveStartedEvent _) => _startedEvents++;
    private void Failed(SaveFailedEvent e) => _failedReasons.Add(e.ReasonKey);
    private void Check(bool condition, string issue) { if (!condition) { _issues.Add(issue); } }
    private string ReadSave() => FileAccess.GetFileAsString(_manager.SlotPath(_slot));
    private static void WriteRaw(string path, string contents)
    {
        using FileAccess file = FileAccess.Open(path, FileAccess.ModeFlags.Write);
        file.StoreString(contents);
        file.Flush();
    }

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
