using System.Linq;
using Embervale.Animation;
using Embervale.Npc;
using Xunit;

namespace Embervale.Tests.Npc;

public sealed class NpcVisualKitTests
{
    private static readonly string[] PlacedHumanTemplates =
    {
        "npc.kael", "npc.elder", "npc.innkeeper", "npc.vendor_goods", "npc.vendor_smith",
        "npc.vendor_alch", "npc.trainer_smith", "npc.stablemaster", "npc.traveller",
        "npc.road_warden", "npc.search_warden", "npc.gate_hand", "npc.impound_clerk",
        "npc.mercenary", "npc.dawnwarden_captain", "npc.dawnwarden_armourer",
        "npc.dawnwarden_serjeant", "npc.corvin", "npc.ash_dunmore", "npc.gilda", "npc.halvard",
        "npc.hana", "npc.mirelle", "npc.nessa", "npc.odo", "npc.perrin", "npc.sable",
        "npc.tam", "npc.quill", "npc.sera", "npc.halder", "npc.archive_keeper",
        "npc.archive_reader", "npc.archive_steward", "npc.syndicate_broker",
        "npc.syndicate_fixer", "npc.sedge", "npc.coyle", "npc.hunter_master",
        "npc.hunter_skinner", "npc.hunter_tracker", "npc.emberbound_hierarch",
        "npc.emberbound_warder", "npc.emberbound_seeker", "npc.clan_chief",
        "npc.clan_quartermaster", "npc.clan_beast_tamer", "npc.clan_hearthkeeper",
        "npc.clan_exile", "npc.bregan", "npc.marta", "npc.odger", "npc.wenna",
    };

    [Fact]
    public void EveryPlacedHumanHasControlledProfile()
    {
        foreach (string template in PlacedHumanTemplates)
        {
            Assert.NotNull(NpcVisualKit.Resolve(template));
        }
    }

    [Fact]
    public void ProfilesStayInsideAttachmentBudget()
    {
        foreach (string template in NpcVisualKit.TemplateIds)
        {
            NpcVisualKit.Profile profile = Assert.IsType<NpcVisualKit.Profile>(NpcVisualKit.Resolve(template));
            Assert.InRange(profile.Pieces.Count, 1, 4);
            Assert.Equal(profile.Pieces.Count, profile.Pieces.Select(piece => piece.Name).Distinct().Count());
            // The kit speaks the canonical socket vocabulary now (EquipmentSockets), not raw bone
            // names. These three are the only sockets an NPC outfit is authored against.
            Assert.All(profile.Pieces, piece => Assert.Contains(
                piece.Socket,
                new[] { EquipmentSocket.Chest, EquipmentSocket.Head, EquipmentSocket.Hips }));
        }
    }

    [Theory]
    [InlineData("npc.dawnwarden_captain", "GuildTabardBlue")]
    [InlineData("npc.syndicate_broker", "GuildTabardRust")]
    [InlineData("npc.archive_keeper", "GuildTabardArchive")]
    [InlineData("npc.emberbound_hierarch", "GuildTabardEmber")]
    [InlineData("npc.hunter_master", "GuildTabardAsh")]
    public void GuildProfilesCarryFactionSilhouette(string template, string tabard)
    {
        Assert.Contains(NpcVisualKit.Resolve(template)!.Pieces, piece => piece.Name == tabard);
    }

    [Fact]
    public void ImportantNpcSilhouettesAreDistinct()
    {
        string[][] silhouettes = { Pieces("npc.kael"), Pieces("npc.elder"), Pieces("npc.innkeeper"),
            Pieces("npc.vendor_goods") };
        Assert.Equal(silhouettes.Length, silhouettes.Select(items => string.Join("|", items)).Distinct().Count());
    }

    private static string[] Pieces(string template) =>
        NpcVisualKit.Resolve(template)!.Pieces.Select(piece => piece.Name).Order().ToArray();
}
