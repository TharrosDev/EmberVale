using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Embervale.Tests;

public sealed class WorldEventContentTests
{
    private static readonly string Root = FindRepositoryRoot();

    [Fact]
    public void EveryWorldEventHasAKnownLocaleNameKeyAndNoDeadDisplayText()
    {
        HashSet<string> keys = File.ReadLines(Path.Combine(Root, "data/locale/strings.csv"))
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .Select(line => line.Split(',', 2)[0])
            .ToHashSet(StringComparer.Ordinal);

        foreach (string eventFile in Directory.EnumerateFiles(Path.Combine(Root, "data/world_events"), "*.tres"))
        {
            string source = File.ReadAllText(eventFile);
            Match nameKey = Regex.Match(source, @"^NameKey = ""([^""]+)""", RegexOptions.Multiline);

            Assert.True(nameKey.Success, $"{Path.GetFileName(eventFile)} has no NameKey");
            Assert.Contains(nameKey.Groups[1].Value, keys);
            Assert.DoesNotContain("DisplayName =", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Description =", source, StringComparison.Ordinal);
        }
    }

    private static string FindRepositoryRoot()
    {
        string? directory = AppContext.BaseDirectory;
        while (directory != null && !File.Exists(Path.Combine(directory, "Embervale.sln")))
        {
            directory = Directory.GetParent(directory)?.FullName;
        }

        return directory ?? throw new DirectoryNotFoundException("Could not find Embervale.sln");
    }
}
