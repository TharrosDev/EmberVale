using System;
using System.Globalization;
using System.IO;

namespace Embervale.Bootstrap;

/// <summary>
/// The stale-binary check. Nothing that launches the game recompiles C#, so an edit to a
/// <c>.cs</c> file followed by a run exercises the previous build and looks verified. At boot in a
/// tooling build run from the project, <see cref="ApplicationRoot"/> compares the assembly's write
/// time with the newest source file's and, when the source is newer, logs one
/// <c>STALE_BINARY</c> line and sets <see cref="IsStale"/>. <c>--strict-build</c> turns that into
/// exit 1 before any gate runs.
///
/// <para>One directory walk, a few milliseconds; never run in a shipping build or an export, where
/// there is no source tree to compare against and <see cref="IsStale"/> stays false.</para>
/// </summary>
public static class BuildFreshness
{
    /// <summary>The command-line argument that makes a stale binary fatal.</summary>
    public const string StrictArgument = "--strict-build";

    /// <summary>The first word of the warning line, for a caller scanning the log.</summary>
    public const string Marker = "STALE_BINARY";

    /// <summary>True when a source file is newer than the running assembly. False until
    /// <see cref="Check"/> has run, and always false where the check does not apply.</summary>
    public static bool IsStale { get; private set; }

    /// <summary>The <c>STALE_BINARY ...</c> line when <see cref="IsStale"/>, otherwise empty.</summary>
    public static string Detail { get; private set; } = string.Empty;

    /// <summary>Runs the comparison for a project checkout and records the result. Returns
    /// <see cref="IsStale"/>.</summary>
    /// <param name="projectRoot">The directory holding <c>project.godot</c>.</param>
    /// <param name="assemblyPath">The loaded <c>Embervale.dll</c>.</param>
    public static bool Check(string projectRoot, string assemblyPath)
    {
        IsStale = Evaluate(
            assemblyPath, Path.Combine(projectRoot, "src"), Path.Combine(projectRoot, "Embervale.csproj"),
            out string detail);
        Detail = detail;
        return IsStale;
    }

    /// <summary>
    /// Pure comparison: true when the newest <c>*.cs</c> under <paramref name="sourceDirectory"/>,
    /// or <paramref name="projectFile"/>, was written after <paramref name="assemblyPath"/>. A
    /// missing assembly or source directory is "cannot tell" and returns false: this warns about a
    /// known stale build, it does not guess.
    /// </summary>
    public static bool Evaluate(string assemblyPath, string sourceDirectory, string projectFile, out string detail)
    {
        detail = string.Empty;
        if (!File.Exists(assemblyPath) || !Directory.Exists(sourceDirectory))
        {
            return false;
        }

        DateTime built = File.GetLastWriteTimeUtc(assemblyPath);
        DateTime newest = DateTime.MinValue;
        string newestPath = string.Empty;
        if (File.Exists(projectFile))
        {
            newest = File.GetLastWriteTimeUtc(projectFile);
            newestPath = projectFile;
        }

        // DirectoryInfo hands back the write time with each entry, so this is one walk and no
        // per-file stat.
        foreach (FileInfo file in new DirectoryInfo(sourceDirectory).EnumerateFiles("*.cs", SearchOption.AllDirectories))
        {
            if (file.LastWriteTimeUtc > newest)
            {
                newest = file.LastWriteTimeUtc;
                newestPath = file.FullName;
            }
        }

        if (newest <= built)
        {
            return false;
        }

        detail = $"{Marker} {Path.GetFileName(assemblyPath)} was built {Stamp(built)} but " +
                 $"{newestPath.Replace('\\', '/')} changed {Stamp(newest)}: this run is NOT your latest code. " +
                 "Run `dotnet build Embervale.sln` first.";
        return true;
    }

    private static string Stamp(DateTime utc) =>
        utc.ToString("yyyy-MM-dd HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
