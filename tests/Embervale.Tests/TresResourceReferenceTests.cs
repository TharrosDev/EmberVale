using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// A <c>.tres</c> that says <c>ExtResource("x")</c> without declaring <c>[ext_resource ... id="x"]</c> (or a
/// <c>SubResource("y")</c> with no <c>[sub_resource ... id="y"]</c>) fails to load in Godot with "Can't load
/// cached ext-resource id". The dialogue content tests parse by regex and never notice, so this pins it for
/// every authored resource under data/.
/// </summary>
public class TresResourceReferenceTests
{
    private static readonly Regex ExtUse = new(@"ExtResource\(""([^""]+)""\)", RegexOptions.Compiled);
    private static readonly Regex ExtDecl = new(@"^\[ext_resource [^\]]*\bid=""([^""]+)""", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex SubUse = new(@"SubResource\(""([^""]+)""\)", RegexOptions.Compiled);
    private static readonly Regex SubDecl = new(@"^\[sub_resource [^\]]*\bid=""([^""]+)""", RegexOptions.Compiled | RegexOptions.Multiline);

    [Fact]
    public void EveryResourceReferenceIsDeclared()
    {
        string data = Path.Combine(FindRepositoryRoot(), "data");
        var problems = Directory.EnumerateFiles(data, "*.tres", SearchOption.AllDirectories)
            .SelectMany(f =>
            {
                string t = File.ReadAllText(f);
                var ext = ExtDecl.Matches(t).Select(m => m.Groups[1].Value).ToHashSet();
                var sub = SubDecl.Matches(t).Select(m => m.Groups[1].Value).ToHashSet();
                return ExtUse.Matches(t).Select(m => m.Groups[1].Value).Distinct().Where(id => !ext.Contains(id))
                    .Select(id => $"{Path.GetRelativePath(data, f)}: ExtResource(\"{id}\") is not declared")
                    .Concat(SubUse.Matches(t).Select(m => m.Groups[1].Value).Distinct().Where(id => !sub.Contains(id))
                        .Select(id => $"{Path.GetRelativePath(data, f)}: SubResource(\"{id}\") is not declared"));
            })
            .ToList();
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems.Take(20)));
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
