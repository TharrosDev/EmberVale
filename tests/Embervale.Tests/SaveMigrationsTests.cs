using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Embervale.Save;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// The save migration chain against golden envelopes of every format the game has written
/// (<c>Fixtures/saves</c>). The rule each test defends is the one in <see cref="SaveMigrations"/>:
/// a step changes what it documents and nothing else, so a format change never costs progress.
/// <c>v1_genuine_quick.json</c> is a real v1 quick save copied from a player profile, unedited.
/// </summary>
public class SaveMigrationsTests
{
    private static readonly string Fixtures =
        Path.Combine(FindRepositoryRoot(), "tests", "Embervale.Tests", "Fixtures", "saves");

    // The world the v2 -> v3 step is told about: a spawn point, and one property whose yard moved
    // from the v2 yard at (95, 90) to (-300, 410).
    private static readonly SaveMigrationLookups Lookups = new()
    {
        StartSpawn = static () => (120d, -35d),
        PropertyYard = static id => id == "property.fixture_yard" ? (-300d, 410d) : null,
    };

    [Theory]
    [InlineData("v1_handbuilt.json", 1)]
    [InlineData("v1_genuine_quick.json", 1)]
    [InlineData("v2_handbuilt.json", 2)]
    [InlineData("v3_handbuilt.json", 3)]
    public void EveryGoldenEnvelope_ValidatesAndMigratesToCurrent(string file, int version)
    {
        string json = Read(file);
        SaveEnvelope before = SaveEnvelope.Read(json);
        Assert.Equal(SaveEnvelopeFault.None, before.Fault);
        Assert.Equal(version, before.Version);
        Assert.True(before.NeedsMigration);
        Assert.Equal(string.Empty, before.StoredChecksum);

        var notes = new List<string>();
        string? migrated = SaveMigrations.MigrateText(json, Lookups, notes);
        Assert.NotNull(migrated);
        Assert.Equal(SaveEnvelope.CurrentVersion - version, notes.Count);

        SaveEnvelope after = SaveEnvelope.Read(migrated);
        Assert.Equal(SaveEnvelopeFault.None, after.Fault);
        Assert.Equal(SaveEnvelope.CurrentVersion, after.Version);
        Assert.False(after.NeedsMigration);
    }

    [Fact]
    public void V1_DropsTheTransformTravelNetAndMap_AndKeepsProgress()
    {
        JsonObject root = Migrated("v1_handbuilt.json");
        JsonObject header = root["header"]!.AsObject();
        JsonObject objects = root["objects"]!.AsObject();

        foreach (string key in new[] { "player_x", "player_y", "player_z", "player_yaw" })
        {
            Assert.False(header.ContainsKey(key));
        }

        Assert.Equal("region.ember_crown", (string?)header["region_id"]);
        Assert.Equal("Fixture", (string?)header["char_name"]);
        Assert.False(objects.ContainsKey("fasttravel"));
        Assert.False(objects.ContainsKey("map"));

        Assert.Equal(310, (int)objects["progression:player"]!["xp"]!);
        Assert.Equal("item.weapon.iron_sword", (string?)objects["inventory:player"]!["stacks"]![0]!["instance"]!["id"]);

        // The later steps ran too: the companion follows, the start cache sits beside the spawn.
        Assert.Equal(0, (int)objects["companions"]!["party"]![0]!["stance"]!);
        JsonNode cache = objects["spawns"]!["actors"]![0]!;
        Assert.Equal(125d, (double)cache["x"]!);
        Assert.Equal(-40d, (double)cache["z"]!);
    }

    [Fact]
    public void GenuineV1Save_KeepsEveryObjectTheStepsDoNotName()
    {
        JsonObject original = JsonNode.Parse(Read("v1_genuine_quick.json"))!["objects"]!.AsObject();
        JsonObject objects = Migrated("v1_genuine_quick.json")["objects"]!.AsObject();

        string[] expected = original.Select(e => e.Key).Where(k => k is not ("fasttravel" or "map")).OrderBy(k => k, StringComparer.Ordinal).ToArray();
        Assert.Equal(expected, objects.Select(e => e.Key).OrderBy(k => k, StringComparer.Ordinal).ToArray());

        // Everything but the three sections a step rewrites is the same text it was.
        foreach (string key in expected.Where(k => k is not ("companions" or "spawns" or "cell_persistence")))
        {
            Assert.Equal(original[key]!.ToJsonString(), objects[key]!.ToJsonString());
        }
    }

    [Fact]
    public void V2_AppliesTheWorldRebuildSpecifics()
    {
        JsonObject root = Migrated("v2_handbuilt.json");
        JsonObject header = root["header"]!.AsObject();
        JsonObject objects = root["objects"]!.AsObject();

        // The header transform goes; the region to restore into stays.
        Assert.False(header.ContainsKey("player_x"));
        Assert.False(header.ContainsKey("player_yaw"));
        Assert.Equal("region.ember_crown", (string?)header["region_id"]);

        // Map footprints and the waypoint go; discovery stays.
        JsonObject map = objects["map"]!.AsObject();
        Assert.False(map.ContainsKey("footprints"));
        Assert.False(map.ContainsKey("waypoint"));
        Assert.Equal("ember_crown.town_hub", (string?)map["pois"]![0]);
        Assert.Equal("region.ember_crown", (string?)map["regions"]![0]);

        // The live world event goes; its cooldowns stay.
        JsonObject events = objects["world_events"]!.AsObject();
        Assert.False(events.ContainsKey("active"));
        Assert.Equal(120d, (double)events["cooldowns"]!["event.caravan"]!);

        // Every companion follows; who is in the party and their loyalty are untouched.
        JsonArray party = objects["companions"]!["party"]!.AsArray();
        Assert.Equal(2, party.Count);
        Assert.All(party, member => Assert.Equal(0, (int)member!["stance"]!));
        Assert.Equal("companion.mira", (string?)party[1]!["id"]);
        Assert.Equal(30, (int)objects["companions"]!["loyalty"]!["companion.kael"]!);

        JsonArray actors = objects["spawns"]!["actors"]!.AsArray();

        // The start cache is re-seated at spawn + (5, -5), on the ground.
        Assert.Equal(125d, (double)actors[0]!["x"]!);
        Assert.Equal(0d, (double)actors[0]!["y"]!);
        Assert.Equal(-40d, (double)actors[0]!["z"]!);

        // place.* shift: a placed prop moves by exactly the distance its yard moved, (95, 90) to
        // (-300, 410) = (-395, +320), and keeps its height and facing.
        Assert.Equal(97d - 395d, (double)actors[1]!["x"]!);
        Assert.Equal(92.5d + 320d, (double)actors[1]!["z"]!);
        Assert.Equal(0.25d, (double)actors[1]!["y"]!);
        Assert.Equal(90d, (double)actors[1]!["yaw"]!);

        // A prop of a property that no longer exists, and an actor that is not a placement, stay put.
        Assert.Equal(10d, (double)actors[2]!["x"]!);
        Assert.Equal(11d, (double)actors[2]!["z"]!);
        Assert.Equal(3d, (double)actors[3]!["x"]!);
        Assert.Equal(4d, (double)actors[3]!["z"]!);

        // The travel net is KEPT from v2 on, and progress is whole.
        Assert.Equal("travel.ember_crown.waystone", (string?)objects["fasttravel"]!["nodes"]![0]!["id"]);
        Assert.Equal(3, (int)objects["inventory:player"]!["stacks"]![0]!["qty"]!);
        Assert.Equal("story.act1.started", (string?)objects["flags:player"]!["flags"]![0]);
    }

    [Fact]
    public void V2_WithNoKnownSpawn_SeatsTheCacheBesideTheOrigin()
    {
        JsonObject root = JsonNode.Parse(Read("v2_handbuilt.json"))!.AsObject();
        Assert.True(SaveMigrations.Migrate(root, new SaveMigrationLookups()));
        JsonNode cache = root["objects"]!["spawns"]!["actors"]![0]!;
        Assert.Equal(5d, (double)cache["x"]!);
        Assert.Equal(-5d, (double)cache["z"]!);

        // With no property data, nothing is shifted rather than shifted by a guess.
        Assert.Equal(97d, (double)root["objects"]!["spawns"]!["actors"]![1]!["x"]!);
    }

    [Fact]
    public void V3_RewritesSetPieceKeysAndKeepsTheirStateAndTheTransform()
    {
        JsonObject root = Migrated("v3_handbuilt.json");
        JsonObject header = root["header"]!.AsObject();
        JsonObject objects = root["objects"]!.AsObject();

        // A v3 transform was written against the current world: it survives.
        Assert.Equal(-210.5d, (double)header["player_x"]!);
        Assert.Equal(1320d, (double)header["player_z"]!);

        Assert.All(objects, entry => Assert.DoesNotContain("res://", entry.Key, StringComparison.Ordinal));
        Assert.True((bool)objects["setpiece:ember_crown/citadel#RampSentries"]!["fired"]!);
        Assert.False((bool)objects["setpiece:ember_crown/citadel#RampSentries"]!["cleared"]!);
        Assert.True((bool)objects["setpiece:ember_crown/town_hub#SquareRaid"]!["cleared"]!);

        // The cell ledger is keyed by the same ids and is rewritten with them.
        JsonObject ledger = objects["cell_persistence"]!["state"]!.AsObject();
        Assert.All(ledger, entry => Assert.DoesNotContain("res://", entry.Key, StringComparison.Ordinal));
        Assert.True((bool)ledger["setpiece:frostfang_reach/clan_hold#HoldRaid"]!["fired"]!);
        Assert.True(ledger.ContainsKey("inventory:ember_crown.cottage_chest"));
        Assert.Equal("ember_crown.bandit_a", (string?)objects["cell_persistence"]!["removed"]![0]);

        // Nothing else moved: the v3 start cache is where v3 put it.
        Assert.Equal(125d, (double)objects["spawns"]!["actors"]![0]!["x"]!);
        Assert.Equal(2, objects["flags:player"]!["flags"]!.AsArray().Count);
    }

    [Fact]
    public void V3_WhenBothFormsOfOnePieceExist_TheStableEntryWins()
    {
        JsonObject root = JsonNode.Parse("""
            {
              "version": 3,
              "objects": {
                "setpiece:res://scenes/regions/ember_crown/citadel.tscn#RampSentries": { "fired": false },
                "setpiece:ember_crown/citadel#RampSentries": { "fired": true }
              }
            }
            """)!.AsObject();

        Assert.Equal(1, SaveMigrations.MigrateV3ToV4(root));
        JsonObject objects = root["objects"]!.AsObject();
        Assert.Single(objects);
        Assert.True((bool)objects["setpiece:ember_crown/citadel#RampSentries"]!["fired"]!);
    }

    [Fact]
    public void ACurrentDocument_IsLeftExactlyAsItIs()
    {
        const string current = """{"version":4,"header":{"player_x":1.5},"objects":{"map":{"footprints":[1],"waypoint":{}},"setpiece:a/b#C":{"fired":true}}}""";
        var notes = new List<string>();
        Assert.Equal(current, SaveMigrations.MigrateText(current, Lookups, notes));
        Assert.Empty(notes);
    }

    [Theory]
    [InlineData("""{"objects":{}}""")]                    // no version
    [InlineData("""{"version":0,"objects":{}}""")]        // below the first format
    [InlineData("""{"version":-3,"objects":{}}""")]
    [InlineData("""{"version":5,"objects":{}}""")]        // newer than this build
    [InlineData("""{"version":2.5,"objects":{}}""")]      // fractional
    [InlineData("""{"version":"2","objects":{}}""")]      // not a number
    [InlineData("""[1,2,3]""")]                           // not an object
    [InlineData("""not json""")]
    public void AnUnmigratableDocument_IsRefused_NeverBestEfforted(string json)
    {
        Assert.Null(SaveMigrations.MigrateText(json, Lookups));
    }

    [Fact]
    public void Migration_PreservesNonAsciiTextAndNumberTokens()
    {
        const string v3 = """{"version":3,"header":{"char_name":"Ásta \"the Ember\" Þórsdóttir"},"objects":{"stats:player":{"hp":12.0,"gold":7,"ratio":0.1}}}""";
        string migrated = SaveMigrations.MigrateText(v3, Lookups)!;
        Assert.Contains("Ásta \\\"the Ember\\\" Þórsdóttir", migrated, StringComparison.Ordinal);
        Assert.Contains("\"hp\":12.0", migrated, StringComparison.Ordinal);
        Assert.Contains("\"gold\":7", migrated, StringComparison.Ordinal);
        Assert.Contains("\"ratio\":0.1", migrated, StringComparison.Ordinal);
    }

    private static JsonObject Migrated(string file)
    {
        string? migrated = SaveMigrations.MigrateText(Read(file), Lookups);
        Assert.NotNull(migrated);
        return JsonNode.Parse(migrated!)!.AsObject();
    }

    private static string Read(string file) => File.ReadAllText(Path.Combine(Fixtures, file));

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
