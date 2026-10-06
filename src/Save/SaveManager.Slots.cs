using System;
using System.Collections.Generic;
using Embervale.Core.Diagnostics;
using Godot;

namespace Embervale.Save;

/// <summary>
/// The slot-facing half of <see cref="SaveManager"/> (ics-base seam): the slot rosters, a
/// non-throwing slot inspection for the browser, and the "may the game be saved right now" gate
/// with its block registry. The save-core lane finishes these bodies; the signatures are what the
/// save UI compiles against, so change a body freely and a signature never.
/// </summary>
public sealed partial class SaveManager
{
    /// <summary>The reserved quick-save slot (F5 / F9). Not part of <see cref="ManualSlots"/>.</summary>
    public const string QuickSlot = SaveSlots.Quick;

    /// <summary>The <see cref="CanSaveNow"/> reason while a save or load is already running.</summary>
    public const string ReasonBusy = "save.blocked.busy";

    /// <summary>The <see cref="CanSaveNow"/> reason when there is no world to save (the title screen).</summary>
    public const string ReasonNoWorld = "save.blocked.no_world";

    /// <summary>The <see cref="Embervale.Core.Events.SaveFailedEvent"/> reason for a save that was attempted and did
    /// not land (I/O, a saveable that threw). The log has the detail.</summary>
    public const string ReasonWriteFailed = "save.failed.write";

    /// <summary>The manual save slots the player may write to by hand, in display order
    /// (<see cref="SaveSlots.Manual"/>).</summary>
    public static IReadOnlyList<string> ManualSlots => SaveSlots.Manual;

    /// <summary>The rotating autosave ring, oldest overwritten (<see cref="SaveSlots.Auto"/>, the
    /// same ids as <see cref="AutosaveService.RingSlots"/>).</summary>
    public static IReadOnlyList<string> AutoSlots => SaveSlots.Auto;

    /// <summary>The build's save format version, for stamping and comparing headers.</summary>
    public static int CurrentFormatVersion => SaveFormatVersion;

    private readonly List<SaveBlock> _saveBlocks = new();

    /// <summary>What a slot id is by convention: <see cref="QuickSlot"/> is Quick, an
    /// <see cref="AutoSlots"/> member is Auto, everything else Manual.</summary>
    public static SaveKind KindOfSlot(string slot) => SaveSlots.KindOf(slot);

    /// <summary>
    /// Describes a slot for the browser without loading it, and <b>never throws</b>. Returns null
    /// only for a null or blank slot id. Otherwise the result always has <see cref="SaveSlotInfo.Slot"/>,
    /// <see cref="SaveSlotInfo.Kind"/> and <see cref="SaveSlotInfo.Health"/> set:
    /// <see cref="SaveHealth.Missing"/> for a slot with no save, <see cref="SaveHealth.Corrupt"/>
    /// for one that is unreadable, not an object, or has no valid version or objects section,
    /// <see cref="SaveHealth.Newer"/> for one written by a later format, and
    /// <see cref="SaveHealth.Ok"/> for one this build can load (an older format it can migrate
    /// included). Whatever header could be read is filled in even when the health is not Ok.
    /// </summary>
    public SaveSlotInfo? InspectSlot(string slot)
    {
        if (string.IsNullOrWhiteSpace(slot))
        {
            return null;
        }

        try
        {
            return InspectSlotCore(slot);
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not inspect save slot '{slot}': {ex.Message}");
            return new SaveSlotInfo { Slot = slot, Health = SaveHealth.Corrupt };
        }
    }

    private SaveSlotInfo InspectSlotCore(string slot)
    {
        if (!SaveExists(slot))
        {
            return new SaveSlotInfo { Slot = slot, Health = SaveHealth.Missing };
        }

        string path = FileAccess.FileExists(SlotSavePath(slot)) ? SlotSavePath(slot) : LegacySlotPath(slot);
        Godot.Collections.Dictionary? root = ReadJsonObjectQuietly(path);

        // A save that does not parse is not handed to ReadHeader (its fallback would parse it again,
        // loudly); the header mirror beside it may still say whose save this was.
        SaveSlotInfo? header = root != null
            ? ReadHeader(slot)
            : ReadJsonObjectQuietly(SlotHeaderPath(slot)) is { } mirror ? SaveSlotInfo.FromDictionary(mirror) : null;
        SaveSlotInfo info = header ?? new SaveSlotInfo();
        info.Slot = slot;
        info.Health = HealthOf(root, out int version);
        if (version > 0)
        {
            info.FormatVersion = version;
        }

        return info;
    }

    private static SaveHealth HealthOf(Godot.Collections.Dictionary? root, out int version)
    {
        version = 0;
        if (root == null || !root.TryGetValue("version", out Variant versionVar) ||
            versionVar.VariantType is not (Variant.Type.Int or Variant.Type.Float))
        {
            return SaveHealth.Corrupt;
        }

        version = versionVar.AsInt32();
        if (version <= 0)
        {
            return SaveHealth.Corrupt;
        }

        if (version > SaveFormatVersion)
        {
            return SaveHealth.Newer;
        }

        return root.TryGetValue("objects", out Variant objects) && objects.VariantType == Variant.Type.Dictionary
            ? SaveHealth.Ok
            : SaveHealth.Corrupt;
    }

    /// <summary>Like <c>ReadJsonObject</c>, but a file that is not JSON is a null result rather than
    /// an engine error line: a corrupt slot is something the browser shows, not something it logs.</summary>
    private static Godot.Collections.Dictionary? ReadJsonObjectQuietly(string path)
    {
        using FileAccess? file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (file == null)
        {
            return null;
        }

        var json = new Json();
        if (json.Parse(file.GetAsText()) != Error.Ok)
        {
            return null;
        }

        Variant parsed = json.Data;
        return parsed.VariantType == Variant.Type.Dictionary ? parsed.AsGodotDictionary() : null;
    }

    /// <summary>
    /// Whether a save may be started right now. On false, <paramref name="reasonKey"/> is a locale
    /// key saying why: the newest live <see cref="PushSaveBlock"/> reason, else
    /// <see cref="ReasonBusy"/>, else <see cref="ReasonNoWorld"/>. On true it is empty.
    /// ⚠️ This is the question; <see cref="SaveGame(string)"/> does not ask it yet (the save-core
    /// lane wires the refusal in), so a caller that must respect a block asks first.
    /// </summary>
    public bool CanSaveNow(out string reasonKey)
    {
        if (_saveBlocks.Count > 0)
        {
            reasonKey = _saveBlocks[^1].ReasonKey;
            return false;
        }

        if (_operationInProgress)
        {
            reasonKey = ReasonBusy;
            return false;
        }

        if (HeaderProvider == null)
        {
            reasonKey = ReasonNoWorld;
            return false;
        }

        reasonKey = string.Empty;
        return true;
    }

    /// <summary>
    /// Blocks saving until the returned token is disposed (a boss fight, a conversation, a
    /// cutscene). <paramref name="reasonKey"/> is the locale key <see cref="CanSaveNow"/> reports
    /// while it is the newest block. Blocks nest and may be released in any order; disposing a token
    /// twice is harmless. Hold the token in a field and dispose it in the same place the thing that
    /// took it ends, teardown included.
    /// </summary>
    public IDisposable PushSaveBlock(string reasonKey)
    {
        var block = new SaveBlock(this, reasonKey);
        _saveBlocks.Add(block);
        return block;
    }

    /// <summary>The reason keys of every live block, oldest first (diagnostics and tests).</summary>
    public IReadOnlyList<string> ActiveSaveBlocks
    {
        get
        {
            var reasons = new List<string>(_saveBlocks.Count);
            foreach (SaveBlock block in _saveBlocks)
            {
                reasons.Add(block.ReasonKey);
            }

            return reasons;
        }
    }

    /// <summary>Drops every live block. For session teardown only: a block belongs to something in
    /// the world, and a world that is gone cannot release it.</summary>
    public void ClearSaveBlocks() => _saveBlocks.Clear();

    private sealed class SaveBlock : IDisposable
    {
        private SaveManager? _owner;

        public SaveBlock(SaveManager owner, string reasonKey)
        {
            _owner = owner;
            ReasonKey = reasonKey;
        }

        public string ReasonKey { get; }

        public void Dispose()
        {
            _owner?._saveBlocks.Remove(this);
            _owner = null;
        }
    }
}
