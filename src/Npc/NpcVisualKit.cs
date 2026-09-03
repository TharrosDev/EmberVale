using System;
using System.Collections.Generic;
using Embervale.Entities;
using Godot;
using Embervale.Core;

namespace Embervale.Npc;

/// <summary>
/// Controlled, reusable visual profiles for scene-placed human NPCs. Profiles are keyed by the
/// authored Entity.TemplateId: profession and faction decide the outfit, never an unconstrained
/// random roll. The underlying skinned body and its animations remain untouched.
/// </summary>
public static class NpcVisualKit
{
    public const string KitPath = ModelAssets.NpcKit;
    private const float AttachmentScale = 0.78f;

    public enum Build
    {
        Slim,
        Standard,
        Broad,
    }

    public readonly record struct Piece(string Name, string Bone, Vector3 Offset, Vector3 RotationDegrees,
        Vector3 Scale);

    public sealed record Profile(string Id, Build BodyBuild, IReadOnlyList<Piece> Pieces);

    private static readonly Vector3 Zero = Vector3.Zero;
    private static readonly Vector3 One = Vector3.One;

    private static Piece Chest(string name, float x = 0f, float y = 0f, float z = 0f,
        float yaw = 0f, float scale = 1f) =>
        new(name, "Chest", new Vector3(x, y, z), new Vector3(0f, yaw, 0f), Vector3.One * scale);

    private static Piece Hips(string name, float x = 0f, float y = 0f, float z = 0f,
        float yaw = 0f, float scale = 1f) =>
        new(name, "Hips", new Vector3(x, y, z), new Vector3(0f, yaw, 0f), Vector3.One * scale);

    private static Profile P(string id, Build build, params Piece[] pieces) => new(id, build, pieces);

    private static readonly Dictionary<string, Profile> Profiles = new(StringComparer.Ordinal)
    {
        // Ember Crown village: the four most important silhouettes are deliberately unique.
        ["npc.kael"] = P("kael", Build.Slim,
            Chest("ShoulderCape", -0.02f, 0.01f, 0.01f), Chest("Pauldron", 0.02f, 0.01f, 0f),
            Hips("BeltPouches"), Hips("Knife", 0.04f, 0f, 0f)),
        ["npc.elder"] = P("village_elder", Build.Standard,
            Chest("MerchantMantle"), Chest("GuildTabardOchre", 0f, -0.01f, 0f, scale: 1.02f),
            Hips("ScrollCase", -0.02f, 0f, 0f)),
        ["npc.innkeeper"] = P("innkeeper", Build.Broad,
            Chest("WorkApron", 0f, 0f, 0.01f), Hips("Keys", 0.02f, 0f, 0f),
            Hips("Mug", -0.02f, 0f, 0f), Hips("BeltPouches")),
        ["npc.vendor_goods"] = P("general_goods", Build.Standard,
            Chest("MerchantMantle"), Chest("Satchel"), Hips("CoinPouch")),
        ["npc.vendor_smith"] = P("smith", Build.Broad,
            Chest("WorkApron"), Hips("Hammer"), Hips("BeltPouches")),
        ["npc.vendor_alch"] = P("apothecary", Build.Slim,
            Chest("MerchantMantle"), Hips("ScrollCase"), Hips("BeltPouches")),
        ["npc.trainer_smith"] = P("smith_apprentice", Build.Standard,
            Chest("OuterVest"), Hips("Hammer"), Hips("BeltPouches")),
        ["npc.stablemaster"] = P("stablemaster", Build.Broad,
            Chest("WorkApron"), Hips("RopeCoil"), Hips("BeltPouches")),
        ["npc.traveller"] = P("wayfarer", Build.Standard,
            Chest("ShoulderCape"), Chest("Satchel"), Hips("ScrollCase")),

        // Crossway and the Dawnwardens.
        ["npc.road_warden"] = P("road_warden", Build.Broad,
            Chest("GuildTabardBlue"), Chest("Pauldron"), Hips("BeltPouches")),
        ["npc.search_warden"] = P("search_warden", Build.Standard,
            Chest("GuildTabardBlue"), Chest("Pauldron", -0.02f), Hips("Keys")),
        ["npc.gate_hand"] = P("gate_hand", Build.Standard,
            Chest("OuterVest"), Hips("BeltPouches")),
        ["npc.impound_clerk"] = P("impound_clerk", Build.Slim,
            Chest("GuildTabardBlue"), Hips("Ledger"), Hips("Keys")),
        ["npc.mercenary"] = P("wren_halloway", Build.Standard,
            Chest("OuterVest"), Chest("ShoulderCape", 0.02f), Chest("Satchel"), Hips("Knife")),
        ["npc.dawnwarden_captain"] = P("dawnwarden_captain", Build.Broad,
            Chest("GuildTabardBlue"), Chest("ShoulderCape"), Chest("Pauldron"), Hips("BeltPouches")),
        ["npc.dawnwarden_armourer"] = P("dawnwarden_armourer", Build.Standard,
            Chest("GuildTabardBlue"), Chest("WorkApron", 0f, 0f, 0.015f), Hips("Hammer")),
        ["npc.dawnwarden_serjeant"] = P("dawnwarden_serjeant", Build.Broad,
            Chest("GuildTabardBlue"), Chest("Pauldron", -0.02f), Hips("BeltPouches")),

        // Embermarket professions. Shared bodies now communicate different work at a glance.
        ["npc.corvin"] = P("provisioner", Build.Standard,
            Chest("OuterVest"), Chest("Satchel"), Hips("CoinPouch")),
        ["npc.ash_dunmore"] = P("collier", Build.Broad,
            Chest("WorkApron"), Hips("RopeCoil"), Hips("BeltPouches")),
        ["npc.gilda"] = P("ironmonger", Build.Broad,
            Chest("WorkApron"), Hips("Hammer"), Hips("Keys")),
        ["npc.halvard"] = P("joiner", Build.Standard,
            Chest("WorkApron"), Hips("Hammer"), Hips("BeltPouches")),
        ["npc.hana"] = P("netmender", Build.Slim,
            Chest("WorkApron"), Hips("RopeCoil"), Hips("Knife")),
        ["npc.mirelle"] = P("cloth_merchant", Build.Slim,
            Chest("MerchantMantle"), Chest("Satchel"), Hips("CoinPouch")),
        ["npc.nessa"] = P("brightcut", Build.Standard,
            Chest("MerchantMantle"), Hips("Ledger"), Hips("CoinPouch")),
        ["npc.odo"] = P("greenhand", Build.Standard,
            Chest("WorkApron"), Chest("Satchel"), Hips("BeltPouches")),
        ["npc.perrin"] = P("tanner", Build.Broad,
            Chest("OuterVest"), Hips("Knife"), Hips("BeltPouches")),
        ["npc.sable"] = P("weaver", Build.Slim,
            Chest("WorkApron"), Chest("Satchel"), Hips("BeltPouches")),
        ["npc.tam"] = P("quillfellow", Build.Standard,
            Chest("MerchantMantle"), Hips("Ledger"), Hips("CoinPouch")),
        ["npc.quill"] = P("curioseller", Build.Slim,
            Chest("ShoulderCape"), Chest("Satchel"), Hips("ScrollCase")),
        ["npc.sera"] = P("long_road", Build.Standard,
            Chest("ShoulderCape"), Chest("Satchel"), Chest("Quiver")),
        ["npc.halder"] = P("market_clerk", Build.Standard,
            Chest("MerchantMantle"), Hips("Ledger"), Hips("Keys")),

        // Veiled Archive.
        ["npc.archive_keeper"] = P("archive_keeper", Build.Slim,
            Chest("GuildTabardArchive"), Chest("MerchantMantle"), Hips("ScrollCase")),
        ["npc.archive_reader"] = P("archive_reader", Build.Standard,
            Chest("GuildTabardArchive"), Hips("Ledger"), Hips("ScrollCase")),
        ["npc.archive_steward"] = P("archive_steward", Build.Broad,
            Chest("GuildTabardArchive"), Chest("ShoulderCape"), Hips("Keys")),

        // Iron Syndicate.
        ["npc.syndicate_broker"] = P("syndicate_broker", Build.Broad,
            Chest("GuildTabardRust"), Chest("MerchantMantle"), Hips("Ledger"), Hips("CoinPouch")),
        ["npc.syndicate_fixer"] = P("syndicate_fixer", Build.Broad,
            Chest("GuildTabardRust"), Hips("Knife"), Hips("BeltPouches")),
        ["npc.sedge"] = P("dockside_sedge", Build.Standard,
            Chest("OuterVest"), Hips("Knife")),
        ["npc.coyle"] = P("dockside_coyle", Build.Broad,
            Chest("MerchantMantle"), Chest("Satchel"), Hips("CoinPouch")),

        // Ash Hunters.
        ["npc.hunter_master"] = P("hunter_master", Build.Broad,
            Chest("GuildTabardAsh"), Chest("ShoulderCape"), Chest("Quiver"), Hips("Knife")),
        ["npc.hunter_skinner"] = P("hunter_skinner", Build.Broad,
            Chest("GuildTabardAsh"), Chest("WorkApron"), Hips("Knife")),
        ["npc.hunter_tracker"] = P("hunter_tracker", Build.Slim,
            Chest("GuildTabardAsh"), Chest("Quiver"), Hips("Knife")),

        // Emberbound.
        ["npc.emberbound_hierarch"] = P("emberbound_hierarch", Build.Standard,
            Chest("GuildTabardEmber"), Chest("ShoulderCape"), Hips("ScrollCase")),
        ["npc.emberbound_warder"] = P("emberbound_warder", Build.Broad,
            Chest("GuildTabardEmber"), Chest("Pauldron"), Hips("BeltPouches")),
        ["npc.emberbound_seeker"] = P("emberbound_seeker", Build.Slim,
            Chest("GuildTabardEmber"), Hips("ScrollCase")),

        // Frostfang civilian and clan roles.
        ["npc.clan_chief"] = P("clan_chief", Build.Broad,
            Chest("GuildTabardAsh"), Chest("ShoulderCape"), Chest("Pauldron"), Hips("BeltPouches")),
        ["npc.clan_quartermaster"] = P("clan_quartermaster", Build.Broad,
            Chest("MerchantMantle"), Chest("Satchel"), Hips("CoinPouch")),
        ["npc.clan_beast_tamer"] = P("clan_beast_tamer", Build.Standard,
            Chest("OuterVest"), Hips("RopeCoil"), Hips("Knife")),
        ["npc.clan_hearthkeeper"] = P("clan_hearthkeeper", Build.Broad,
            Chest("WorkApron"), Hips("Keys"), Hips("Mug")),
        ["npc.clan_exile"] = P("clan_exile", Build.Slim,
            Chest("ShoulderCape"), Hips("Knife")),

        // Mine and Landing workers/traders.
        ["npc.bregan"] = P("mine_foreman", Build.Broad,
            Chest("WorkApron"), Hips("Hammer"), Hips("Keys")),
        ["npc.marta"] = P("mine_clerk", Build.Slim,
            Chest("OuterVest"), Hips("Ledger"), Hips("Keys")),
        ["npc.odger"] = P("landing_chandler", Build.Broad,
            Chest("WorkApron"), Hips("RopeCoil"), Hips("BeltPouches")),
        ["npc.wenna"] = P("landing_netmender", Build.Standard,
            Chest("WorkApron"), Hips("RopeCoil"), Hips("Knife")),
    };

    public static Profile? Resolve(string templateId) =>
        Profiles.TryGetValue(templateId, out Profile? profile) ? profile : null;

    public static IReadOnlyCollection<string> TemplateIds => Profiles.Keys;

    /// <summary>Attaches one profile to a live retargeted humanoid. Missing kit/bones degrade to the
    /// original body; gameplay and animation never depend on cosmetic attachments.</summary>
    public static void Attach(IEntity entity, Skeleton3D skeleton)
    {
        if (skeleton.HasMeta("modular_npc_kit") || Resolve(entity.TemplateId) is not { } profile)
        {
            return;
        }

        PackedScene? packed = GD.Load<PackedScene>(KitPath);
        if (packed?.Instantiate() is not Node3D source)
        {
            GD.PushWarning($"NPC visual kit could not load '{KitPath}'.");
            return;
        }

        skeleton.SetMeta("modular_npc_kit", profile.Id);
        skeleton.SetMeta("modular_npc_build", profile.BodyBuild.ToString());

        float width = profile.BodyBuild switch
        {
            Build.Slim => 0.94f,
            Build.Broad => 1.07f,
            _ => 1f,
        };

        foreach (Piece spec in profile.Pieces)
        {
            int bone = skeleton.FindBone(spec.Bone);
            Node3D? visual = FindNode(source, spec.Name);
            if (bone < 0 || visual == null)
            {
                GD.PushWarning($"NPC kit profile '{profile.Id}' cannot attach {spec.Name} to {spec.Bone}.");
                continue;
            }

            var follower = new NpcKitFollower
            {
                Name = $"Kit_{spec.Name}",
                Skeleton = skeleton,
                BoneIndex = bone,
                Offset = spec.Offset,
                AuthoredRotation = spec.RotationDegrees * (Mathf.Pi / 180f),
                VisualScale = new Vector3(spec.Scale.X * width, spec.Scale.Y, spec.Scale.Z * width) *
                    AttachmentScale,
            };
            skeleton.AddChild(follower);
            visual.Owner = null;
            visual.Reparent(follower, keepGlobalTransform: false);
            visual.Transform = Transform3D.Identity;
            visual.Name = spec.Name;
        }

        source.Free();
    }

    private static Node3D? FindNode(Node node, string name)
    {
        if (node is Node3D node3D && node.Name == name)
        {
            return node3D;
        }

        foreach (Node child in node.GetChildren())
        {
            if (FindNode(child, name) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}

/// <summary>Follows a bone's animated delta while keeping repository-authored rigid equipment in
/// character/model axes. This avoids treating differing retargeted bone-local axes as garment axes.</summary>
internal sealed partial class NpcKitFollower : Node3D
{
    public required Skeleton3D Skeleton { get; init; }
    public int BoneIndex { get; init; }
    public Vector3 Offset { get; init; }
    public Vector3 AuthoredRotation { get; init; }
    public Vector3 VisualScale { get; init; } = Vector3.One;

    public override void _Ready()
    {
        TopLevel = true;
        Follow();
    }

    public override void _Process(double delta) => Follow();

    private void Follow()
    {
        Transform3D rest = Skeleton.GetBoneGlobalRest(BoneIndex);
        Transform3D pose = Skeleton.GetBoneGlobalPose(BoneIndex);
        Basis skeletonBasis = Skeleton.GlobalTransform.Basis.Orthonormalized();
        Basis delta = (pose.Basis * rest.Basis.Inverse()).Orthonormalized();
        Basis authored = Basis.FromEuler(AuthoredRotation);
        Basis finalBasis = (skeletonBasis * delta * authored).Scaled(VisualScale);
        Vector3 origin = (Skeleton.GlobalTransform * pose).Origin + skeletonBasis * (delta * Offset);
        GlobalTransform = new Transform3D(finalBasis, origin);
    }
}
