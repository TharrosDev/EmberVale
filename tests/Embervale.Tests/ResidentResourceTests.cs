using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// The guard on <c>ResidentResources</c>: a project C# <c>Resource</c> is never loaded by path with a
/// bare <c>GD.Load</c> / <c>ResourceLoader.Load</c>. A wrapper nobody holds can be collected while
/// the native object is still in Godot's cache, and the next load of that path is the
/// <c>gchandle.is_released()</c> FATAL the <c>--lifecycle</c> gate kept hitting. A scan of the
/// source, because the failure is a timing window no unit test can open.
/// </summary>
public class ResidentResourceTests
{
    private static readonly Regex Load = new(@"\b(?:GD|ResourceLoader)\.Load<(\w+)>\(", RegexOptions.Compiled);
    private static readonly Regex ClassDeclaration = new(@"\bclass\s+(\w+)", RegexOptions.Compiled);

    [Fact]
    public void ProjectResourcesAreLoadedThroughResidentResources()
    {
        string src = Path.Combine(Root, "src");
        List<string> files = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories).ToList();

        HashSet<string> projectTypes = files
            .SelectMany(f => ClassDeclaration.Matches(File.ReadAllText(f)).Select(m => m.Groups[1].Value))
            .ToHashSet(StringComparer.Ordinal);
        Assert.Contains("AttributeSet", projectTypes); // a scan that finds nothing would pass everything

        var offenders = new List<string>();
        foreach (string file in files.Where(f => Path.GetFileName(f) != "ResidentResources.cs"))
        {
            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                // CacheMode.Ignore never touches the cache, so it cannot hand back a dying wrapper.
                if (lines[i].Contains("CacheMode.Ignore", StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (Match match in Load.Matches(lines[i]))
                {
                    if (projectTypes.Contains(match.Groups[1].Value))
                    {
                        offenders.Add($"{Path.GetRelativePath(Root, file)}:{i + 1} loads {match.Groups[1].Value}");
                    }
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "Load these through ResidentResources.Load<T> instead: " + string.Join("; ", offenders));
    }

    private static string Root
    {
        get
        {
            string? directory = AppContext.BaseDirectory;
            while (directory != null && !File.Exists(Path.Combine(directory, "Embervale.sln")))
            {
                directory = Directory.GetParent(directory)?.FullName;
            }

            return directory ?? throw new DirectoryNotFoundException("Could not find Embervale.sln");
        }
    }
}
