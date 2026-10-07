using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// The models an enemy archetype names for its hand. <c>ContentValidator</c> is the real gate, but
/// it needs Godot; a held weapon whose file was renamed or deleted only shows as an empty hand, so
/// the path is checked here against the files on disk.
/// </summary>
public class EnemyHeldWeaponContentTests
{
    private static readonly string Root = FindRepositoryRoot();

    [Fact]
    public void EveryHeldWeaponModelExists()
    {
        foreach (string file in Directory.GetFiles(Path.Combine(Root, "data/enemies"), "*.tres"))
        {
            Match match = Regex.Match(File.ReadAllText(file), @"^HeldWeaponPath = ""res://([^""]+)""", RegexOptions.Multiline);
            if (match.Success)
            {
                Assert.True(File.Exists(Path.Combine(Root, match.Groups[1].Value)),
                    $"{Path.GetFileName(file)}: held weapon model '{match.Groups[1].Value}' is not on disk");
            }
        }
    }

    [Theory]
    [InlineData("IronKing.tres")]
    [InlineData("ClanShaman.tres")]
    public void AnEnemyThatFoughtWithAVisibleWeaponStillHoldsOne(string archetype)
    {
        string source = File.ReadAllText(Path.Combine(Root, "data/enemies", archetype));
        Assert.Matches(new Regex(@"^HeldWeaponPath = ""res://assets/models/weapons/wpn_\w+\.glb""", RegexOptions.Multiline), source);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "project.godot")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
