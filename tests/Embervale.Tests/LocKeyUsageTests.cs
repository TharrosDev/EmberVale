using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// A <c>Loc.T("key")</c> whose key is not in <c>data/locale/strings.csv</c> renders the raw key on screen, and
/// nothing else notices: the compiler is happy and <c>--validate</c> only checks authored data. It happened when
/// one branch deleted a key it found unreferenced while another began using it. This pins every literal key in
/// <c>src/</c> to the catalogue. Keys built by concatenation (a trailing dot, e.g. <c>"magic.status." + id</c>) are
/// not literals and are skipped.
/// </summary>
public class LocKeyUsageTests
{
    private static readonly Regex Use = new(@"Loc\.(?:T|TF)\(\s*""([a-z0-9_.]+)""", RegexOptions.Compiled);

    [Fact]
    public void EveryLiteralLocKeyInSourceIsInTheCatalogue()
    {
        string root = FindRepositoryRoot();
        var keys = File.ReadLines(Path.Combine(root, "data", "locale", "strings.csv"))
            .Where(l => l.Length > 0 && l[0] != '#')
            .Select(l => l.Split(',')[0])
            .ToHashSet();

        var missing = Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .SelectMany(f => Use.Matches(File.ReadAllText(f))
                .Select(m => m.Groups[1].Value)
                .Where(k => !k.EndsWith('.') && !keys.Contains(k))
                .Select(k => $"{Path.GetRelativePath(root, f)}: \"{k}\""))
            .Distinct()
            .ToList();

        Assert.True(missing.Count == 0, "Loc keys used in code but absent from strings.csv:" + Environment.NewLine + string.Join(Environment.NewLine, missing.Take(20)));
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "project.godot")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("project.godot not found");
    }
}
