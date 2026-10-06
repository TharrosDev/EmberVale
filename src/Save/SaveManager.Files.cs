using Embervale.Core.Diagnostics;
using Godot;

namespace Embervale.Save;

/// <summary>
/// The file-level half of <see cref="SaveManager"/>: the backup generation's path, quiet reads,
/// and the cleanup of staged files. The decisions about which file to trust are not here; they are
/// pure and live in <see cref="SaveBackup"/> and <see cref="SaveEnvelope"/>.
/// </summary>
public sealed partial class SaveManager
{
    /// <summary>Toast title shown after a load that had to fall back to the backup generation.</summary>
    public const string RecoveredTitleKey = "save.recovered.title";

    /// <summary>Toast detail for <see cref="RecoveredTitleKey"/>.</summary>
    public const string RecoveredDetailKey = "save.recovered.detail";

    /// <summary>
    /// True when the most recent <see cref="LoadGame"/> succeeded by reading the slot's previous
    /// generation (<c>save.json.bak</c>) because its own save was damaged or gone. Reset at the
    /// start of every load. The same load also raises a toast (<see cref="RecoveredTitleKey"/>);
    /// before a load, <see cref="InspectSlot"/> answers the same question as
    /// <see cref="SaveSlotInfo.RecoveredFromBackup"/>.
    /// </summary>
    public bool LastLoadUsedBackup { get; private set; }

    /// <summary>The slot's one previous generation: the save that <c>save.json</c> replaced. It
    /// carries its own header inside its envelope; there is deliberately no header mirror for it
    /// (see docs/SAVE_FORMAT.md §1).</summary>
    private static string SlotBackupPath(string slot) => SlotSavePath(slot) + SaveBackup.Suffix;

    /// <summary>Whether a slot holds a previous generation (it may or may not be loadable;
    /// <see cref="InspectSlot"/> says which).</summary>
    public bool BackupExists(string slot) => FileAccess.FileExists(SlotBackupPath(slot));

    /// <summary>A file's text, or null when it is absent or will not open. Never logs: an absent
    /// or unreadable file is an answer here, and the caller decides whether it is an error.</summary>
    private static string? ReadText(string path) => ReadText(path, out _);

    private static string? ReadText(string path, out Error error)
    {
        error = Error.FileNotFound;
        if (!FileAccess.FileExists(path))
        {
            return null;
        }

        using FileAccess? file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (file == null)
        {
            error = FileAccess.GetOpenError();
            return null;
        }

        error = Error.Ok;
        return file.GetAsText();
    }

    /// <summary>Removes a staged file that will never be committed. A directory of that name is
    /// not a staged file and is left alone.</summary>
    private static void RemoveOrphan(string temp)
    {
        if (FileAccess.FileExists(temp) && DirAccess.RemoveAbsolute(temp) != Error.Ok)
        {
            Log.Warn($"Could not remove the staged file '{temp}'; it is not read and can be deleted by hand.");
        }
    }

    /// <summary>
    /// Removes every <c>*.tmp</c> a crash left in the save folder: beside the legacy flat saves and
    /// inside each slot. A staged file is by definition one that was never committed, so nothing
    /// reads it and nothing is lost. Returns how many were removed.
    /// </summary>
    private static int CleanOrphanedTemps()
    {
        if (!DirAccess.DirExistsAbsolute(SaveDirectory))
        {
            return 0;
        }

        int removed = RemoveTempsIn(SaveDirectory);
        using (DirAccess? root = DirAccess.Open(SaveDirectory))
        {
            if (root != null)
            {
                foreach (string slot in root.GetDirectories())
                {
                    removed += RemoveTempsIn(SlotDir(slot));
                }
            }
        }

        if (removed > 0)
        {
            Log.Info($"Removed {removed} staged save file(s) left by an interrupted write.");
        }

        return removed;
    }

    private static int RemoveTempsIn(string directory)
    {
        using DirAccess? dir = DirAccess.Open(directory);
        if (dir == null)
        {
            return 0;
        }

        int removed = 0;
        foreach (string file in dir.GetFiles())
        {
            if (SaveBackup.IsOrphanedTemp(file) && dir.Remove(file) == Error.Ok)
            {
                removed++;
            }
        }

        return removed;
    }
}
