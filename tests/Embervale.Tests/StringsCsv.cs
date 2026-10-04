using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace Embervale.Tests;

/// <summary>The raw <c>data/locale/strings.csv</c> catalogue (key to the rest of the line, quotes kept),
/// for tests that check a card or toast key has text.</summary>
internal static class StringsCsv
{
    public static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "project.godot")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }

    public static Dictionary<string, string> Rows()
    {
        var rows = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string line in File.ReadAllLines(Path.Combine(RepositoryRoot(), "data/locale/strings.csv")))
        {
            int comma = line.IndexOf(',');
            if (line.Length > 0 && line[0] != '#' && comma > 0)
            {
                rows[line[..comma]] = line[(comma + 1)..];
            }
        }

        return rows;
    }
}
