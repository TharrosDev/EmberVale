using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Embervale.Save;

/// <summary>What <see cref="SaveFiles.Commit"/> is asked to put on disk for one save. Every path is
/// an absolute OS path.</summary>
public readonly record struct SaveCommit(
    string SavePath,
    string SaveJson,
    string HeaderPath,
    string HeaderJson,
    string? LegacyPath = null,
    bool KeepPrevious = false);

/// <summary>The outcome of one <see cref="SaveFiles.Commit"/>, with the lines to log about it.
/// Logging is left to the caller because the commit may have run on a worker thread.</summary>
public sealed class SaveCommitResult
{
    /// <summary>Whether <c>save.json</c> was committed. The save is good whenever this is true.</summary>
    public bool Saved { get; init; }

    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

/// <summary>
/// The file half of a save, on plain <see cref="System.IO"/> so it can run on a worker thread:
/// the atomic temp-then-rename write, and the save / header-mirror / legacy-file sequence that
/// <c>SaveManager.SaveGameCore</c> performs.
/// </summary>
public static class SaveFiles
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    /// <summary>Atomic write: stage to <c>&lt;path&gt;.tmp</c>, flush it to the device, then rename
    /// over the target, so a crash mid-write can never truncate a previously good file. On failure
    /// the previous file is untouched and <paramref name="error"/> says why.
    ///
    /// With <paramref name="keepPrevious"/> the file being replaced is moved to
    /// <c>&lt;path&gt;.bak</c> between the stage and the rename, so the slot keeps the generation
    /// before this one (<see cref="SaveBackup"/> decides when that is wanted). If the rename then
    /// fails the previous file is moved back.</summary>
    public static bool WriteAtomic(string path, byte[] bytes, out string error, bool keepPrevious = false)
    {
        string temp = path + SaveBackup.TempSuffix;
        string previous = path + SaveBackup.Suffix;
        bool keptPrevious = false;
        try
        {
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }

            if (keepPrevious && File.Exists(path))
            {
                File.Move(path, previous, true);
                keptPrevious = true;
            }

            File.Move(temp, path, true);
            error = string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException
                                       or ArgumentException or System.Security.SecurityException)
        {
            if (keptPrevious && !File.Exists(path))
            {
                try
                {
                    File.Move(previous, path);
                }
                catch (Exception restore) when (restore is IOException or UnauthorizedAccessException)
                {
                    // The previous generation stays under its .bak name, where a load still finds it.
                }
            }

            TryDelete(temp);
            error = $"Could not commit '{path}' ({ex.GetType().Name}: {ex.Message}); previous file preserved.";
            return false;
        }
    }

    public static bool WriteAtomic(string path, string text, out string error, bool keepPrevious = false) =>
        WriteAtomic(path, Utf8NoBom.GetBytes(text), out error, keepPrevious);

    /// <summary>
    /// Commits one save: <c>save.json</c> first (a failure there fails the save and touches nothing
    /// else), then the header mirror, then the legacy flat file's removal.
    ///
    /// ⚠️ A STALE MIRROR IS WORSE THAN A MISSING ONE, so a failed mirror write deletes it: the two
    /// writes are independent, and leaving the previous save's header beside the new
    /// <c>save.json</c> would answer every question about this save (region, position, character)
    /// with the last one's answers. A missing mirror only costs a slower read from the envelope.
    /// </summary>
    public static SaveCommitResult Commit(SaveCommit commit)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        try
        {
            string? directory = Path.GetDirectoryName(commit.SavePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            errors.Add($"Could not create the save directory for '{commit.SavePath}': {ex.Message}");
            return new SaveCommitResult { Saved = false, Errors = errors };
        }

        if (!WriteAtomic(commit.SavePath, commit.SaveJson, out string saveError, commit.KeepPrevious))
        {
            errors.Add(saveError);
            return new SaveCommitResult { Saved = false, Errors = errors };
        }

        if (!WriteAtomic(commit.HeaderPath, commit.HeaderJson, out string headerError))
        {
            if (TryDelete(commit.HeaderPath))
            {
                warnings.Add($"{headerError} Removed the header mirror so reads fall back to the header inside save.json.");
            }
            else
            {
                errors.Add($"{headerError} A STALE header mirror at '{commit.HeaderPath}' could not be removed; " +
                           "the slot browser and a load will read the previous save's region, position and " +
                           "character until it is deleted by hand.");
            }
        }

        // One-time migration: once the directory layout holds the save, drop the legacy flat file.
        if (!string.IsNullOrEmpty(commit.LegacyPath))
        {
            TryDelete(commit.LegacyPath);
        }

        return new SaveCommitResult { Saved = true, Errors = errors, Warnings = warnings };
    }

    /// <summary>Deletes a file. True when it is gone afterwards (including when it never existed).</summary>
    public static bool TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            return !File.Exists(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return false;
        }
    }
}
