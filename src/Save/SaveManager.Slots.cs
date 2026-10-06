using System;
using System.Collections.Generic;
using Embervale.Core.Diagnostics;
using Godot;

namespace Embervale.Save;

/// <summary>
/// The slot-facing half of <see cref="SaveManager"/> (ics-base seam): the slot rosters, a
/// non-throwing slot inspection for the browser, and the "may the game be saved right now" gate
/// with its block registry. The signatures are what the save UI compiles against, so change a
/// body freely and a signature never.
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
    /// <see cref="SaveSlotInfo.Kind"/> and <see cref="SaveSlotInfo.Health"/> set.
    ///
    /// This is the full validation a load performs, stopped short of applying anything: parse,
    /// shape, version and checksum (<see cref="SaveEnvelope"/>), for the save and for its backup
    /// generation. <see cref="SaveSlotInfo.Health"/> is the health of <b>whatever a load would
    /// read</b>: <see cref="SaveHealth.Missing"/> for a slot with nothing in it,
    /// <see cref="SaveHealth.Corrupt"/> for one that is unreadable, misshapen, fails its checksum,
    /// or has no valid version, <see cref="SaveHealth.Newer"/> for one written by a later format,
    /// and <see cref="SaveHealth.Ok"/> for one this build can load (an older format it can migrate
    /// included).
    ///
    /// ⚠️ When the save itself is damaged or gone but its backup loads, the answer is
    /// <see cref="SaveHealth.Ok"/> with <see cref="SaveSlotInfo.RecoveredFromBackup"/> set, and every
    /// header field describes the <b>backup</b>, because that is the character, region and playtime
    /// the player will get. <see cref="SaveSlotInfo.PrimaryHealth"/> keeps the raw finding. The
    /// header always comes from the envelope that would be loaded, never from the
    /// <c>header.json</c> mirror, so it cannot be stale; the mirror is consulted only to name a
    /// slot whose every envelope is unreadable.
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
            return new SaveSlotInfo { Slot = slot, Health = SaveHealth.Corrupt, PrimaryHealth = SaveHealth.Corrupt };
        }
    }

    private SaveSlotInfo InspectSlotCore(string slot)
    {
        string path = FileAccess.FileExists(SlotSavePath(slot)) ? SlotSavePath(slot) : LegacySlotPath(slot);
        SaveEnvelope primary = SaveEnvelope.Read(ReadText(path));
        SaveEnvelope backup = SaveEnvelope.Read(ReadText(SlotBackupPath(slot)));
        bool recovered = SaveBackup.Choose(primary.Health, backup.Health) == SaveSource.Backup;
        SaveEnvelope described = recovered ? backup : primary;

        SaveSlotInfo info = HeaderOf(described.HeaderJson)
                            ?? (recovered ? null : HeaderOf(ReadText(SlotHeaderPath(slot))))
                            ?? new SaveSlotInfo();
        info.Slot = slot;
        info.PrimaryHealth = primary.Health;
        info.Health = SaveBackup.Effective(primary.Health, backup.Health);
        info.RecoveredFromBackup = recovered;
        info.HasBackup = backup.Health == SaveHealth.Ok;
        if (info.TimestampUnix == 0d)
        {
            info.TimestampUnix = described.Timestamp;
        }

        if (described.Version > 0)
        {
            info.FormatVersion = described.Version;
        }

        if (described.StoredChecksum.Length > 0)
        {
            info.Checksum = described.StoredChecksum;
        }

        return info;
    }

    /// <summary>A header from its JSON text, or null when there is none or it does not parse. Uses a
    /// <see cref="Json"/> instance rather than <c>Json.ParseString</c> so a bad header is a null
    /// result and not an engine error line: a corrupt slot is something the browser shows, not
    /// something it logs.</summary>
    private static SaveSlotInfo? HeaderOf(string? headerJson)
    {
        if (string.IsNullOrEmpty(headerJson))
        {
            return null;
        }

        var json = new Json();
        return json.Parse(headerJson) == Error.Ok && SaveRead.AsSection(json.Data) is { } data
            ? SaveSlotInfo.FromDictionary(data)
            : null;
    }

    /// <summary>
    /// Whether a save may be started right now. On false, <paramref name="reasonKey"/> is a locale
    /// key saying why: the newest live <see cref="PushSaveBlock"/> reason, else
    /// <see cref="ReasonBusy"/>, else <see cref="ReasonNoWorld"/>. On true it is empty.
    /// <see cref="SaveGame(string)"/> enforces the first two itself (a blocked or busy save is
    /// refused with <see cref="Embervale.Core.Events.SaveFailedEvent"/>); it does not enforce the
    /// third, because the native probes save with no world. A menu asks this first so it can grey
    /// the button out and say why instead of offering a save that will be refused.
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
