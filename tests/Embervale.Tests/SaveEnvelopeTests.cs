using System;
using Embervale.Save;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// The envelope validation a load and the slot browser share, and the pure decisions around it:
/// the content checksum, the one backup generation, and the set-piece key. Every refusal here is a
/// save the game must not feed to live components; every acceptance is one it must not turn away.
/// </summary>
public class SaveEnvelopeTests
{
    private const string Objects = """{"inventory:player":{"stacks":[{"qty":3,"instance":{"id":"item.potion.health"}}]},"flags:player":{"flags":["a","b"]}}""";

    private static string Envelope(string version = "4", string? checksum = null, string objects = Objects) =>
        "{\"version\":" + version + (checksum == null ? string.Empty : ",\"checksum\":" + checksum) +
        ",\"timestamp\":1759000000.5,\"header\":{\"char_name\":\"Fixture\",\"level\":9},\"objects\":" + objects + "}";

    private static string Sum(string objects = Objects) => SaveChecksum.ComputeForEnvelope(Envelope(objects: objects))!;

    // --- Shape and version -------------------------------------------------------------------

    [Fact]
    public void AHealthyEnvelope_IsLoadable_AndDescribesItself()
    {
        SaveEnvelope envelope = SaveEnvelope.Read(Envelope(checksum: "\"" + Sum() + "\""));
        Assert.Equal(SaveEnvelopeFault.None, envelope.Fault);
        Assert.Equal(SaveHealth.Ok, envelope.Health);
        Assert.True(envelope.IsLoadable);
        Assert.False(envelope.NeedsMigration);
        Assert.Equal(4, envelope.Version);
        Assert.Equal(2, envelope.ObjectCount);
        Assert.Equal(1759000000.5, envelope.Timestamp);
        Assert.Equal("""{"char_name":"Fixture","level":9}""", envelope.HeaderJson);
        Assert.Equal(envelope.ComputedChecksum, envelope.StoredChecksum);
    }

    [Fact]
    public void AnAbsentFile_IsMissing_NotCorrupt()
    {
        SaveEnvelope envelope = SaveEnvelope.Read(null);
        Assert.Equal(SaveEnvelopeFault.Missing, envelope.Fault);
        Assert.Equal(SaveHealth.Missing, envelope.Health);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json {")]
    [InlineData("[1,2,3]")]
    [InlineData("42")]
    [InlineData("{\"version\":4,\"objects\":{")] // a truncated write
    public void TextThatIsNotAnObject_IsCorrupt(string json)
    {
        SaveEnvelope envelope = SaveEnvelope.Read(json);
        Assert.Equal(SaveEnvelopeFault.NotAnObject, envelope.Fault);
        Assert.Equal(SaveHealth.Corrupt, envelope.Health);
    }

    [Theory]
    [InlineData("""{"objects":{}}""")]
    [InlineData("""{"version":"4","objects":{}}""")]
    [InlineData("""{"version":null,"objects":{}}""")]
    [InlineData("""{"version":true,"objects":{}}""")]
    public void NoNumericVersion_IsRefused_AsNotOneOfOurs(string json)
    {
        Assert.Equal(SaveEnvelopeFault.NoVersion, SaveEnvelope.Read(json).Fault);
    }

    [Theory]
    [InlineData("3.5")]
    [InlineData("4.000001")]
    [InlineData("1e99")]
    [InlineData("-1e99")]
    public void AFractionalOrOutOfRangeVersion_IsRefused_NeverTruncated(string version)
    {
        SaveEnvelope envelope = SaveEnvelope.Read(Envelope(version));
        Assert.Equal(SaveEnvelopeFault.InvalidVersion, envelope.Fault);
        Assert.Equal(SaveHealth.Corrupt, envelope.Health);
        Assert.Equal(0, envelope.Version);
    }

    [Theory]
    [InlineData("4.0", 4)] // a whole number written as a float is the same version
    [InlineData("3", 3)]
    [InlineData("1", 1)]
    public void AWholeVersionUpToCurrent_IsLoadable(string version, int expected)
    {
        SaveEnvelope envelope = SaveEnvelope.Read(Envelope(version));
        Assert.Equal(SaveEnvelopeFault.None, envelope.Fault);
        Assert.Equal(expected, envelope.Version);
        Assert.Equal(expected < SaveEnvelope.CurrentVersion, envelope.NeedsMigration);
    }

    [Theory]
    [InlineData("5")]
    [InlineData("9000")]
    public void ANewerVersion_IsRefusedAsNewer_AndStillNamesItsOwner(string version)
    {
        SaveEnvelope envelope = SaveEnvelope.Read(Envelope(version));
        Assert.Equal(SaveEnvelopeFault.Newer, envelope.Fault);
        Assert.Equal(SaveHealth.Newer, envelope.Health);
        Assert.False(envelope.IsLoadable);
        Assert.Contains("Fixture", envelope.HeaderJson, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-2")]
    public void AVersionBelowTheFirstFormat_IsRefused(string version)
    {
        SaveEnvelope envelope = SaveEnvelope.Read(Envelope(version));
        Assert.Equal(SaveEnvelopeFault.TooOld, envelope.Fault);
        Assert.Equal(SaveHealth.Corrupt, envelope.Health);
    }

    [Theory]
    [InlineData("""{"version":4}""")]
    [InlineData("""{"version":4,"objects":[]}""")]
    [InlineData("""{"version":4,"objects":"gone"}""")]
    public void NoObjectsSection_IsRefused(string json)
    {
        Assert.Equal(SaveEnvelopeFault.NoObjects, SaveEnvelope.Read(json).Fault);
    }

    [Fact]
    public void AnEntryThatIsNotAnObject_IsRefusedByName_NotMistakenForAnAbsentSystem()
    {
        SaveEnvelope envelope = SaveEnvelope.Read("""{"version":4,"objects":{"map":{},"audit.state":"corrupt"}}""");
        Assert.Equal(SaveEnvelopeFault.BadEntry, envelope.Fault);
        Assert.Equal("audit.state", envelope.FaultDetail);
    }

    [Fact]
    public void TrailingCommasAndComments_AreTolerated_LikeTheEnginesParser()
    {
        SaveEnvelope envelope = SaveEnvelope.Read("""
            {
              // hand-edited
              "version": 4,
              "objects": { "map": {}, },
            }
            """);
        Assert.Equal(SaveEnvelopeFault.None, envelope.Fault);
    }

    // --- Checksum ----------------------------------------------------------------------------

    [Fact]
    public void Checksum_RoundTrips_ThroughTheEnvelope()
    {
        string sum = Sum();
        Assert.StartsWith(SaveChecksum.Prefix, sum, StringComparison.Ordinal);
        Assert.Equal(SaveChecksum.Prefix.Length + 64, sum.Length);

        SaveEnvelope envelope = SaveEnvelope.Read(Envelope(checksum: "\"" + sum + "\""));
        Assert.Equal(SaveEnvelopeFault.None, envelope.Fault);
        Assert.Equal(sum, envelope.StoredChecksum);
        Assert.Equal(sum, envelope.ComputedChecksum);
    }

    [Fact]
    public void Checksum_IgnoresLayoutAndKeyOrder()
    {
        const string reordered = """
            {
                "flags:player":     { "flags": [ "a", "b" ] },
                "inventory:player": { "stacks": [ { "instance": { "id": "item.potion.health" }, "qty": 3 } ] }
            }
            """;
        Assert.Equal(Sum(), Sum(reordered));
    }

    [Theory]
    [InlineData("""{"inventory:player":{"stacks":[{"qty":4,"instance":{"id":"item.potion.health"}}]},"flags:player":{"flags":["a","b"]}}""")] // a value
    [InlineData("""{"inventory:player":{"stacks":[{"qty":3,"instance":{"id":"item.potion.mana"}}]},"flags:player":{"flags":["a","b"]}}""")] // a string
    [InlineData("""{"inventory:player":{"stacks":[{"qty":3,"instance":{"id":"item.potion.health"}}]},"flags:player":{"flags":["b","a"]}}""")] // array order
    [InlineData("""{"inventory:player":{"stacks":[{"qty":3,"instance":{"id":"item.potion.health"}}]},"flags:npc":{"flags":["a","b"]}}""")] // a key
    [InlineData("""{"inventory:player":{"stacks":[{"qty":3,"instance":{"id":"item.potion.health"}}]},"flags:player":{"flags":["ab"]}}""")] // a boundary
    [InlineData("""{"inventory:player":{"stacks":[{"qty":3,"instance":{"id":"item.potion.health"}}]}}""")] // a dropped entry
    [InlineData("""{"inventory:player":{"stacks":[{"qty":3.5,"instance":{"id":"item.potion.health"}}]},"flags:player":{"flags":["a","b"]}}""")] // a fraction
    [InlineData("""{"inventory:player":{"stacks":[{"qty":"3","instance":{"id":"item.potion.health"}}]},"flags:player":{"flags":["a","b"]}}""")] // a type
    public void Checksum_ChangesWithAnyChangeOfContent(string changed)
    {
        Assert.NotEqual(Sum(), Sum(changed));
    }

    [Theory]
    [InlineData("3.0")]
    [InlineData("3.00")]
    [InlineData("3e0")]
    [InlineData("0.3e1")]
    public void Checksum_HashesANumberByValue_SoARewriteThatChangesNothingKeepsIt(string three)
    {
        // The engine's parser may hand an integer back as a float; a save read and rewritten by a
        // tool must not fail its integrity check over content nobody changed.
        Assert.Equal(Sum(), Sum(Objects.Replace("\"qty\":3", "\"qty\":" + three, StringComparison.Ordinal)));
    }

    [Fact]
    public void Checksum_KeepsFractionsAndLargeNumbersApart()
    {
        static string Of(string number) => Sum("{\"stats:player\":{\"v\":" + number + "}}");
        Assert.NotEqual(Of("0.1"), Of("0.100000000000001"));
        Assert.Equal(Of("0.1"), Of("0.10"));
        Assert.Equal(Of("0.1"), Of("0.10000000000000002")); // one unit in the last place: the same number
        Assert.Equal(Of("-1.41963624954224"), Of("-1.4196362495422400"));
        Assert.NotEqual(Of("9007199254740993"), Of("9007199254740992"));
        Assert.NotEqual(Of("1"), Of("-1"));
        Assert.NotEqual(Of("1e99999"), Of("-1e99999"));
    }

    [Fact]
    public void ATamperedSave_FailsItsIntegrityCheck()
    {
        string tampered = Envelope(checksum: "\"" + Sum() + "\"").Replace("\"qty\":3", "\"qty\":99", StringComparison.Ordinal);
        SaveEnvelope envelope = SaveEnvelope.Read(tampered);
        Assert.Equal(SaveEnvelopeFault.ChecksumMismatch, envelope.Fault);
        Assert.Equal(SaveHealth.Corrupt, envelope.Health);
        Assert.NotEqual(envelope.StoredChecksum, envelope.ComputedChecksum);
    }

    [Theory]
    [InlineData("\"md5:abc\"")] // an algorithm this build cannot verify
    [InlineData("12345")]       // damaged, not absent
    [InlineData("null")]
    public void AChecksumThatCannotBeVerified_IsAMismatch(string checksum)
    {
        Assert.Equal(SaveEnvelopeFault.ChecksumMismatch, SaveEnvelope.Read(Envelope(checksum: checksum)).Fault);
    }

    [Theory]
    [InlineData(null)]   // every save written before format 4
    [InlineData("\"\"")] // an empty one says nothing
    public void AnAbsentChecksum_IsAccepted(string? checksum)
    {
        SaveEnvelope envelope = SaveEnvelope.Read(Envelope(checksum: checksum));
        Assert.Equal(SaveEnvelopeFault.None, envelope.Fault);
        Assert.Equal(string.Empty, envelope.StoredChecksum);
        Assert.Equal(Sum(), envelope.ComputedChecksum);
    }

    [Fact]
    public void TheHeader_IsNotPartOfTheChecksum()
    {
        // The header mirror carries the checksum too, so it cannot also be covered by it; and a
        // tool that restamps a header (the save-reload gate does) must not invalidate the content.
        string original = Envelope(checksum: "\"" + Sum() + "\"");
        string restamped = original.Replace("\"level\":9", "\"level\":50", StringComparison.Ordinal);
        Assert.NotEqual(original, restamped);
        Assert.Equal(SaveEnvelopeFault.None, SaveEnvelope.Read(restamped).Fault);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("""{"version":4}""")]
    public void ComputeForEnvelope_IsNullWhenThereIsNothingToHash(string? json)
    {
        Assert.Null(SaveChecksum.ComputeForEnvelope(json));
    }

    // --- Backup generation -------------------------------------------------------------------

    [Theory]
    [InlineData(SaveHealth.Ok, SaveHealth.Ok, SaveSource.Primary)]
    [InlineData(SaveHealth.Ok, SaveHealth.Corrupt, SaveSource.Primary)]
    [InlineData(SaveHealth.Ok, SaveHealth.Missing, SaveSource.Primary)]
    [InlineData(SaveHealth.Corrupt, SaveHealth.Ok, SaveSource.Backup)]
    [InlineData(SaveHealth.Missing, SaveHealth.Ok, SaveSource.Backup)]
    [InlineData(SaveHealth.Corrupt, SaveHealth.Corrupt, SaveSource.None)]
    [InlineData(SaveHealth.Corrupt, SaveHealth.Missing, SaveSource.None)]
    [InlineData(SaveHealth.Corrupt, SaveHealth.Newer, SaveSource.None)]
    [InlineData(SaveHealth.Missing, SaveHealth.Missing, SaveSource.None)]
    [InlineData(SaveHealth.Newer, SaveHealth.Ok, SaveSource.None)] // a newer save is never bypassed
    [InlineData(SaveHealth.Newer, SaveHealth.Missing, SaveSource.None)]
    public void Backup_IsChosenOnlyWhenThePrimaryIsUnreadableAndTheBackupIsSound(
        SaveHealth primary, SaveHealth backup, SaveSource expected)
    {
        Assert.Equal(expected, SaveBackup.Choose(primary, backup));
    }

    [Theory]
    [InlineData(SaveHealth.Ok, SaveHealth.Missing, SaveHealth.Ok)]
    [InlineData(SaveHealth.Corrupt, SaveHealth.Ok, SaveHealth.Ok)]       // recovered
    [InlineData(SaveHealth.Missing, SaveHealth.Ok, SaveHealth.Ok)]       // an interrupted save
    [InlineData(SaveHealth.Corrupt, SaveHealth.Corrupt, SaveHealth.Corrupt)]
    [InlineData(SaveHealth.Missing, SaveHealth.Corrupt, SaveHealth.Corrupt)] // something is there
    [InlineData(SaveHealth.Missing, SaveHealth.Missing, SaveHealth.Missing)]
    [InlineData(SaveHealth.Newer, SaveHealth.Ok, SaveHealth.Newer)]
    public void Effective_IsTheHealthOfWhatALoadWouldRead(SaveHealth primary, SaveHealth backup, SaveHealth expected)
    {
        Assert.Equal(expected, SaveBackup.Effective(primary, backup));
    }

    [Theory]
    [InlineData(SaveHealth.Ok, true)]
    [InlineData(SaveHealth.Newer, true)]
    [InlineData(SaveHealth.Corrupt, false)] // never move a damaged save over the good generation
    [InlineData(SaveHealth.Missing, false)]
    public void OnlyASoundSave_IsKeptAsTheBackupGeneration(SaveHealth primary, bool rotate)
    {
        Assert.Equal(rotate, SaveBackup.ShouldRotate(primary));
    }

    [Theory]
    [InlineData("save.json.tmp", true)]
    [InlineData("header.json.tmp", true)]
    [InlineData("screenshot.png.TMP", true)]
    [InlineData("save.json", false)]
    [InlineData("save.json.bak", false)]
    [InlineData("tmp", false)]
    public void OnlyStagedFiles_AreOrphans(string file, bool orphan)
    {
        Assert.Equal(orphan, SaveBackup.IsOrphanedTemp(file));
    }

    // --- Set-piece keys ----------------------------------------------------------------------

    [Theory]
    [InlineData("res://scenes/regions/ember_crown/citadel.tscn", "RampSentries", "setpiece:ember_crown/citadel#RampSentries")]
    [InlineData("res://scenes/regions/frostfang_reach/clan_hold.tscn", "Encounters/HoldRaid", "setpiece:frostfang_reach/clan_hold#Encounters/HoldRaid")]
    [InlineData("res://data/world_bake/cells/region_ember_crown/ember_crown_town_hub.scn", "SquareRaid", "setpiece:ember_crown/town_hub#SquareRaid")] // baked == authored
    [InlineData("res://data/world_bake/cells/region_sunspire/sunspire_library.scn", "Wardens", "setpiece:sunspire/library#Wardens")]
    [InlineData("res://data/world_bake/cells/region_ember_crown/odd_name.scn", "Raid", "setpiece:ember_crown/odd_name#Raid")]
    [InlineData("res://scenes/prototypes/arena.tscn", "Raid", "setpiece:scenes/prototypes/arena#Raid")]
    [InlineData("", "Loose", "setpiece:#Loose")]
    [InlineData(null, "Loose", "setpiece:#Loose")]
    public void SetPieceKey_IsBuiltFromTheCellSlugAndNodePath(string? scene, string node, string expected)
    {
        Assert.Equal(expected, SetPieceSaveIds.Build(scene, node));
    }

    [Theory]
    [InlineData("setpiece:res://scenes/regions/ember_crown/citadel.tscn#RampSentries", "setpiece:ember_crown/citadel#RampSentries")]
    [InlineData("setpiece:res://scenes/regions/ember_crown/town_hub.tscn#A/B", "setpiece:ember_crown/town_hub#A/B")]
    [InlineData("setpiece:res://data/world_bake/cells/region_frostfang_reach/frostfang_reach_clan_hold.scn#HoldRaid", "setpiece:frostfang_reach/clan_hold#HoldRaid")]
    [InlineData("setpiece:ember_crown/citadel#RampSentries", "setpiece:ember_crown/citadel#RampSentries")] // already stable
    [InlineData("inventory:player", "inventory:player")]                                                   // not a set piece
    [InlineData("map", "map")]
    public void Normalize_RewritesOnlyTheLegacyForm_AndIsIdempotent(string id, string expected)
    {
        Assert.Equal(expected, SetPieceSaveIds.Normalize(id));
        Assert.Equal(expected, SetPieceSaveIds.Normalize(SetPieceSaveIds.Normalize(id)));
        Assert.False(SetPieceSaveIds.IsLegacy(expected));
    }

    [Fact]
    public void TheStableKey_OfALivePiece_IsWhatTheMigrationProducesFromItsOldKey()
    {
        // The component builds its key from the same scene path and node path the old key embedded,
        // so a migrated entry is claimed by the piece that wrote it.
        const string scene = "res://scenes/regions/ember_crown/town_hub.tscn";
        Assert.Equal(SetPieceSaveIds.Build(scene, "SquareRaid"), SetPieceSaveIds.Normalize($"setpiece:{scene}#SquareRaid"));
    }
}
