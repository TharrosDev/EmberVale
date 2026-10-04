using System;
using System.IO;
using System.Linq;
using Embervale.Debugging;
using Embervale.Enemies;
using Xunit;

namespace Embervale.Tests;

/// <summary>The scene-text parser behind the BossSummon / SetPiece validator arms, and the set-piece ranges.</summary>
public class SceneStoryHooksTests
{
    private const string Scene = """
        [gd_scene format=3]

        [ext_resource type="Script" path="res://src/Entities/Entity.cs" id="1_entity"]
        [ext_resource type="Script" path="res://src/Enemies/SetPieceSpawnComponent.cs" id="2_setpiece"]
        [ext_resource type="Script" path="res://src/Enemies/BossSummonComponent.cs" id="3_summon"]

        ; a comment

        [sub_resource type="BoxShape3D" id="Shape"]
        size = Vector3(1, 1, 1)

        [node name="Cell" type="Node3D"]

        [node name="Raid" type="Node3D" parent="."]
        script = ExtResource("2_setpiece")
        TriggerFlagId = "flag.beat.bell_rung"
        RequiredFlagId = "flag.main.opening_done"
        TemplateIds = PackedStringArray("enemy.goblin", "enemy.bandit")
        CountPerWave = 4
        Waves = 3
        WaveDelaySeconds = 12.5
        TriggerSize = Vector3(30, 6, 18.5)
        ClearedFlagId = "flag.beat.raid_cleared"

        [node name="Brazier" type="Node3D" parent="." instance=ExtResource("9_brazier")]

        [node name="Summon" type="Node" parent="Brazier"]
        script = ExtResource("3_summon")
        RequiredQuestId = ""
        RequiredFlagId = "flag.arc.frostfang_ready"
        """;

    [Fact]
    public void ParsesNodesScriptsAndProperties()
    {
        var nodes = SceneTextParser.Nodes(Scene);

        SceneNodeText raid = nodes.Single(n => n.Name == "Raid");
        Assert.EndsWith("/SetPieceSpawnComponent.cs", raid.ScriptPath);
        Assert.Equal("flag.beat.bell_rung", raid.Str("TriggerFlagId"));
        Assert.Equal(new[] { "enemy.goblin", "enemy.bandit" }, raid.StrList("TemplateIds", Array.Empty<string>()));
        Assert.Equal(4, raid.Int("CountPerWave", 0));
        Assert.Equal(3, raid.Int("Waves", 0));
        Assert.Equal(12.5f, raid.Float("WaveDelaySeconds", 0f));
        Assert.Equal((30f, 6f, 18.5f), raid.Vector3("TriggerSize", (0f, 0f, 0f)));

        SceneNodeText summon = nodes.Single(n => n.Name == "Summon");
        Assert.EndsWith("/BossSummonComponent.cs", summon.ScriptPath);
        Assert.Equal(string.Empty, summon.Str("RequiredQuestId", "quest.warband.heart")); // explicitly empty beats the default
        Assert.Equal("flag.arc.frostfang_ready", summon.Str("RequiredFlagId"));
        Assert.Equal("quest.warband.heart", summon.Str("Missing", "quest.warband.heart")); // omitted: the default applies
    }

    [Fact]
    public void NodesWithoutAScriptHaveNoScriptPath()
    {
        var nodes = SceneTextParser.Nodes(Scene);

        Assert.Equal(string.Empty, nodes.Single(n => n.Name == "Cell").ScriptPath);
        Assert.Equal(string.Empty, nodes.Single(n => n.Name == "Brazier").ScriptPath);
    }

    [Fact]
    public void SubResourceLinesAreNotNodeProperties()
    {
        var nodes = SceneTextParser.Nodes(Scene);

        Assert.DoesNotContain(nodes, n => n.Props.ContainsKey("size"));
    }

    [Fact]
    public void ReadsARealShippedScene()
    {
        string root = RepositoryRoot();
        string text = File.ReadAllText(Path.Combine(root, "scenes", "regions", "ashen_wilds", "beast_lair.tscn"));

        SceneNodeText summon = SceneTextParser.Nodes(text).Single(n => n.ScriptPath.EndsWith("/BossSummonComponent.cs"));

        Assert.Equal("Summon", summon.Name);
        Assert.StartsWith("enemy.", summon.Str("BossTemplateId", "enemy.x"));
    }

    // --- set-piece ranges: a negative case in each direction (invariant 8) --------

    private static string[] Problems(
        int waves = 2, int count = 3, float delay = 10f, bool flag = true, float size = 20f, float radius = 8f, int templates = 1) =>
        SetPieceWaves.ConfigProblems(waves, count, delay, flag, size, size, size, radius, templates).ToArray();

    [Fact]
    public void AValidPieceHasNoProblems()
    {
        Assert.Empty(Problems());
        Assert.Empty(Problems(waves: 1, count: 1, delay: 0f, radius: 1f));
        Assert.Empty(Problems(waves: 10, count: 6, delay: 600f, radius: 60f));
    }

    [Theory]
    [InlineData(0, "Waves")]
    [InlineData(-1, "Waves")]
    [InlineData(11, "Waves")]
    public void WavesOutOfRangeAtEitherEnd(int waves, string expected) =>
        Assert.Contains(Problems(waves: waves), p => p.Contains(expected));

    [Theory]
    [InlineData(0, "CountPerWave")]
    [InlineData(13, "CountPerWave")]
    public void CountOutOfRangeAtEitherEnd(int count, string expected) =>
        Assert.Contains(Problems(count: count), p => p.Contains(expected));

    [Fact]
    public void TotalSpawnsAreCapped()
    {
        Assert.Contains(Problems(waves: 10, count: 7), p => p.Contains("spawns"));
        Assert.DoesNotContain(Problems(waves: 10, count: 6), p => p.Contains("spawns"));
    }

    [Theory]
    [InlineData(-0.1f)]
    [InlineData(600.1f)]
    [InlineData(float.NaN)]
    public void WaveDelayOutOfRange(float delay) =>
        Assert.Contains(Problems(delay: delay), p => p.Contains("WaveDelaySeconds"));

    [Theory]
    [InlineData(0f)]
    [InlineData(-3f)]
    [InlineData(200.5f)]
    public void AreaTriggerNeedsASaneBox(float size)
    {
        Assert.Contains(Problems(flag: false, size: size), p => p.Contains("TriggerSize"));

        // The same box is irrelevant to a flag-triggered piece.
        Assert.DoesNotContain(Problems(flag: true, size: size), p => p.Contains("TriggerSize"));
    }

    [Theory]
    [InlineData(0.5f)]
    [InlineData(60.5f)]
    public void SpawnRadiusOutOfRangeAtEitherEnd(float radius) =>
        Assert.Contains(Problems(radius: radius), p => p.Contains("SpawnRadius"));

    [Fact]
    public void ATemplateIsRequired() =>
        Assert.Contains(Problems(templates: 0), p => p.Contains("TemplateIds"));

    private static string RepositoryRoot()
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
