using System;

namespace Embervale.Save;

/// <summary>Which file of a slot a load reads.</summary>
public enum SaveSource
{
    /// <summary>Neither generation can be loaded.</summary>
    None,

    /// <summary><c>save.json</c> (or the legacy flat file).</summary>
    Primary,

    /// <summary><c>save.json.bak</c>, the previous generation.</summary>
    Backup,
}

/// <summary>
/// The decisions behind a slot's one backup generation, as pure logic. Each slot keeps exactly one
/// previous save (<c>save.json.bak</c> with its <c>header.json.bak</c>), moved aside the moment a
/// new one replaces it. These rules say when that move happens, which file a load reads, and what
/// the slot browser calls the slot. Godot-free; the file work is <see cref="SaveManager"/>'s.
/// </summary>
public static class SaveBackup
{
    /// <summary>Appended to a file's name for its previous generation.</summary>
    public const string Suffix = ".bak";

    /// <summary>Appended to a file's name while it is being staged for an atomic write.</summary>
    public const string TempSuffix = ".tmp";

    /// <summary>
    /// Which generation a load reads. The backup is used only when the primary is unreadable or
    /// gone <b>and</b> the backup is sound. ⚠️ A primary written by a newer build is never bypassed:
    /// loading the older generation under it would quietly throw away what that build saved, and
    /// the next save would bury it.
    /// </summary>
    public static SaveSource Choose(SaveHealth primary, SaveHealth backup)
    {
        if (primary == SaveHealth.Ok)
        {
            return SaveSource.Primary;
        }

        return primary is SaveHealth.Corrupt or SaveHealth.Missing && backup == SaveHealth.Ok
            ? SaveSource.Backup
            : SaveSource.None;
    }

    /// <summary>What the slot browser reports: the health of whatever a load would actually read.
    /// A slot whose primary is damaged but whose backup loads is Ok (and flagged as recovered); a
    /// slot with no primary and a damaged backup is Corrupt, not Missing, because something is there.</summary>
    public static SaveHealth Effective(SaveHealth primary, SaveHealth backup)
    {
        if (Choose(primary, backup) != SaveSource.None)
        {
            return SaveHealth.Ok;
        }

        return primary == SaveHealth.Missing && backup != SaveHealth.Missing ? SaveHealth.Corrupt : primary;
    }

    /// <summary>
    /// Whether the save about to be replaced is worth keeping as the backup generation. Only a
    /// structurally sound one is: rotating a damaged primary into <c>.bak</c> would destroy the
    /// good generation that is the whole reason the backup exists.
    /// </summary>
    public static bool ShouldRotate(SaveHealth primary) => primary is SaveHealth.Ok or SaveHealth.Newer;

    /// <summary>True for a staging file a crash or a failed rename left behind.</summary>
    public static bool IsOrphanedTemp(string fileName) =>
        fileName.EndsWith(TempSuffix, StringComparison.OrdinalIgnoreCase);
}
