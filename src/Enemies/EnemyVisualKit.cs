using System;
using System.Collections.Generic;
using Embervale.Animation;
using Embervale.Entities;
using Godot;
using Embervale.Core;

namespace Embervale.Enemies;

/// <summary>
/// Deterministic silhouette kit for enemies whose useful source rig is retained. Every profile is
/// keyed by gameplay identity; no random equipment or tint-only variation is permitted. Pieces are
/// rigid Blender-authored forms that follow animated bone deltas without inheriting pack-specific
/// bone-local axes.
/// </summary>
public static class EnemyVisualKit
{
    public const string KitPath = ModelAssets.EnemyIdentityKit;

    public readonly record struct Piece(string Name, string Bone, Vector3 Offset,
        Vector3 RotationDegrees, Vector3 Scale)
    {
        /// <summary>
        /// The canonical socket this piece's authored bone belongs to.
        ///
        /// ⚠️ The authored bone name is still passed to the presentation component as the PREFERRED
        /// bone, so nothing here moves. This mapping only supplies the fallback chain for a rig that
        /// does not have the exact bone — which is the half these profiles never had. Several of
        /// these sit on quadruped rigs carrying both a Spine and a Torso, and resolving them purely
        /// through the humanoid preference order would walk a carapace up the animal's back.
        /// </summary>
        public EquipmentSocket Socket => Bone switch
        {
            "Head" => EquipmentSocket.Head,
            "Back" => EquipmentSocket.BackPrimary,
            "Wrist.R" or "Hand.R" => EquipmentSocket.HandR,
            "Wrist.L" or "Hand.L" => EquipmentSocket.HandL,
            "Hips" => EquipmentSocket.Hips,
            _ => EquipmentSocket.Chest,
        };
    }

    public sealed record Profile(string Id, IReadOnlyList<Piece> Pieces, Color? BodyTint = null);

    private static readonly Vector3 One = Vector3.One;
    private static Piece At(string name, string bone, float scale = 1f,
        float x = 0f, float y = 0f, float z = 0f, float yaw = 0f) =>
        new(name, bone, new Vector3(x, y, z), new Vector3(0f, yaw, 0f), One * scale);
    private static Profile P(string id, params Piece[] pieces) => new(id, pieces);
    private static Profile PT(string id, Color tint, params Piece[] pieces) =>
        new(id, pieces, tint);

    private static readonly Dictionary<string, Profile> Profiles = new(StringComparer.Ordinal)
    {
        // The head piece is the boar's OWN: snout, paired tusks and brow, authored against the
        // retained cattle rig (`tools/build_enemy_identity_assets.py`, "Thornback: the retained
        // cattle rig gains a low snout, paired tusks and a thorned back ridge"). It was built and
        // then never wired here — `BoarHead` was the only one of the kit's 40 pieces no profile
        // referenced — so the Head slot borrowed `AshMawJaws` and the archetype read as the bull it
        // is built from, straight horns and all. The carapace stays: it is bulk, not identity.
        ["enemy.thornback_boar"] = P("thornback_boar",
            At("AshMawCarapace", "Torso", 0.92f), At("BoarHead", "Head", 0.90f),
            At("BoarThornback", "Back", 0.82f, z: 0.05f)),

        ["enemy.barrow_wight"] = P("barrow_wight",
            At("WightBurialArmor", "Chest", 0.58f), At("WightCrown", "Head", 0.62f, z: 0.08f)),
        ["enemy.grave_shade"] = P("grave_shade",
            At("ShadeVeil", "Torso", 1.0f), At("ShadeHalo", "Head", 1.0f, z: 0.08f)),
        ["enemy.bone_knight"] = P("bone_knight", At("BoneKnightArmor", "Chest", 0.82f)),
        ["enemy.hollow_husk"] = P("hollow_husk", At("CultAshMark", "Chest", 0.72f)),

        ["enemy.clan_shaman"] = P("clan_shaman",
            At("ShamanFurs", "Chest", 0.62f), At("ShamanMask", "Head", 0.66f),
            At("ShamanTotem", "Chest", 0.68f, x: 0.40f)),
        ["enemy.hollow_necromancer"] = P("hollow_necromancer",
            At("NecroRibs", "Chest", 0.62f), At("NecroCowl", "Head", 0.66f),
            At("NecroFocus", "Wrist.L", 0.54f)),
        ["enemy.clan_raider"] = P("clan_raider", At("ClanRaiderArmor", "Chest", 0.80f)),
        ["enemy.clan_beast_tamer"] = P("clan_beast_tamer",
            At("ShamanFurs", "Chest", 0.76f), At("BanditMask", "Head", 0.72f)),

        ["enemy.soldier"] = P("fallen_soldier",
            At("SoldierHarness", "Chest", 0.60f), At("SoldierKettle", "Head", 0.65f, z: 0.04f)),
        ["enemy.bandit"] = P("road_bandit",
            At("BanditMantle", "Chest", 0.60f), At("BanditMask", "Head", 0.64f)),
        ["enemy.syndicate_enforcer"] = P("syndicate_enforcer",
            At("EnforcerArmor", "Chest", 0.60f), At("EnforcerMask", "Head", 0.64f)),
        ["enemy.cultist"] = P("ashen_cultist", At("CultAshMark", "Chest", 0.78f)),
        ["enemy.cinder_thrall"] = P("cinder_thrall",
            At("CultAshMark", "Chest", 0.82f), At("NecroCowl", "Head", 0.75f)),
        ["enemy.arcane_echo"] = P("arcane_echo", At("ArcaneEchoRings", "Torso", 1.0f)),

        ["enemy.dire_wolf"] = P("dire_wolf",
            At("DireWolfMane", "Torso", 1.05f), At("DireWolfFangs", "Head", 1.0f)),
        ["enemy.frost_stalker"] = P("frost_stalker",
            At("FrostStalkerRidge", "Back", 0.82f), At("FrostStalkerMask", "Head", 0.82f)),
        ["enemy.wild_dragon"] = PT("wild_dragon", new Color("596143"),
            At("WildDragonCrown", "Head", 1.8f), At("WildDragonDorsal", "Torso", 1.9f)),
        ["enemy.ash_dragon"] = PT("ash_dragon", new Color("351e1b"),
            At("AshDragonCrown", "Head", 2.0f), At("AshDragonChains", "Torso", 2.0f)),
        ["enemy.frost_drake"] = PT("frost_drake", new Color("6f8791"),
            At("FrostDragonCrest", "Head", 1.35f), At("FrostDragonDorsal", "Torso", 1.45f)),
        ["enemy.ancient_dragon"] = PT("ancient_dragon", new Color("433a32"),
            At("AncientDragonCrown", "Head", 2.0f)),

        ["enemy.iron_king"] = P("iron_king",
            At("IronKingPlate", "Chest", 0.64f), At("IronKingCrown", "Head", 0.74f, z: 0.07f),
            At("IronKingChains", "Chest", 0.68f), At("IronKingBack", "Chest", 0.62f, y: 0.06f),
            At("IronKingWeapon", "Wrist.R", 0.78f, x: 0.08f, y: -0.42f, z: -0.48f)),
    };

    public static Profile? Resolve(string templateId) =>
        Profiles.TryGetValue(templateId, out Profile? profile) ? profile : null;

    public static void Attach(IEntity entity, EquipmentPresentationComponent presentation)
    {
        if (presentation.Skeleton is not { } skeleton ||
            skeleton.HasMeta("embervale_enemy_identity") ||
            Resolve(entity.TemplateId) is not { } profile)
        {
            return;
        }

        if (GD.Load<PackedScene>(KitPath)?.Instantiate() is not Node3D source)
        {
            GD.PushWarning($"Enemy visual kit could not load '{KitPath}'.");
            return;
        }

        skeleton.SetMeta("embervale_enemy_identity", profile.Id);
        foreach (Piece spec in profile.Pieces)
        {
            if (FindNode(source, spec.Name) is not { } visual)
            {
                GD.PushWarning($"Enemy kit profile '{profile.Id}' has no piece named {spec.Name}.");
                continue;
            }

            // Body-aligned, explicitly, and with the authored bone preferred — the two things that
            // make this refactor a no-op on screen. See Piece.Socket.
            presentation.Attach(
                spec.Socket, visual, $"Identity_{spec.Name}", spec.Offset, spec.RotationDegrees,
                spec.Scale, SocketSpace.BodyAligned, spec.Bone);
        }
        source.Free();
        if (profile.BodyTint is { } tint)
        {
            TintBody(skeleton.GetParent(), tint);
        }
    }

    private static void TintBody(Node? node, Color tint)
    {
        if (node is MeshInstance3D mesh)
        {
            mesh.MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = tint,
                Roughness = 0.82f,
                Metallic = 0.08f,
            };
        }
        if (node == null)
        {
            return;
        }
        foreach (Node child in node.GetChildren())
        {
            TintBody(child, tint);
        }
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

