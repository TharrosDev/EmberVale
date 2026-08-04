using System.Collections.Generic;
using Embervale.Crafting;
using Embervale.Entities;
using Embervale.Save;
using Godot;

namespace Embervale.Housing;

/// <summary>
/// Everything the player can set down (Phase 37C), and the builders that make each one.
///
/// <b>Why the id list and the builders live in one file.</b> The builders go into
/// <see cref="PersistentActorRegistry"/>, which is populated in <c>GameBootstrap.BuildWorld</c> —
/// code <c>--validate</c> never runs. So the validator cannot ask the registry what exists, and a
/// kit pointing at an unregistered template would be uncheckable: craftable, stackable, carryable,
/// and silently inert the moment it is placed. <see cref="Ids"/> is a plain Godot-free set the
/// validator *can* read, and <see cref="RegisterAll"/> registers exactly those ids and no others, so
/// the two cannot disagree.
///
/// Every model here is already imported and credited — the three station models the town hub uses,
/// and three props from <c>assets/models/props/</c>. Nothing new was sourced for 37C.
/// </summary>
public static class PlaceableTemplates
{
    public const string StationForge = "prop.station.forge";
    public const string StationWorkbench = "prop.station.workbench";
    public const string StationAlchemy = "prop.station.alchemy";
    public const string DecorBrazier = "prop.decor.brazier";
    public const string DecorCrate = "prop.decor.crate";
    public const string DecorBanner = "prop.decor.banner";

    /// <summary>
    /// Every placeable template id. <c>prop.*</c> is the namespace <c>docs/IDS.md</c> reserves for
    /// persistent non-character objects restored by <see cref="PersistentSpawnDirector"/> — kept
    /// distinct from the authored <c>station.*</c> ids the town hub's own stations carry, which are
    /// never routed through the director and must not start being tracked.
    /// </summary>
    public static readonly IReadOnlySet<string> Ids = new HashSet<string>
    {
        StationForge, StationWorkbench, StationAlchemy, DecorBrazier, DecorCrate, DecorBanner,
    };

    private const string ModelRoot = "res://assets/models/props/";

    /// <summary>Installs a builder per <see cref="Ids"/> entry. Called once by the bootstrap, after
    /// <c>PersistentActorRegistry.Clear()</c>.</summary>
    public static void RegisterAll()
    {
        foreach (string id in Ids)
        {
            string captured = id;
            PersistentActorRegistry.Register(captured, p => Build(captured, p)!);
        }
    }

    /// <summary>
    /// Builds one placeable, or null for an unknown id. Public because the <c>--validate</c> pass
    /// builds every one of them to prove the builder works — <c>PersistentSpawnDirector.Spawn</c>
    /// discards a host that is not an <c>IEntity</c> with nothing but a log line, so a broken builder
    /// would otherwise stay invisible until a player spent a kit on nothing.
    /// </summary>
    public static Node3D? Build(string templateId, Vector3 position) => templateId switch
    {
        StationForge => Station(
            CraftingStationType.Forge, "craft.station_forge", position,
            new Color(0.45f, 0.2f, 0.16f), "prp_station_forge.glb"),
        StationWorkbench => Station(
            CraftingStationType.Workbench, "craft.station_workbench", position,
            new Color(0.45f, 0.32f, 0.18f), "prp_station_workbench.glb"),
        StationAlchemy => Station(
            CraftingStationType.Alchemy, "craft.station_alchemy", position,
            new Color(0.2f, 0.42f, 0.4f), "prp_station_alchemy.glb"),
        DecorBrazier => Decor(
            "place.decor_brazier", position, "prp_brazier.glb",
            new Color(0.5f, 0.25f, 0.12f), new Vector3(0.7f, 1.2f, 0.7f)),
        DecorCrate => Decor(
            "place.decor_crate", position, "prp_crate.glb",
            new Color(0.45f, 0.33f, 0.19f), new Vector3(0.8f, 0.8f, 0.8f)),
        DecorBanner => Decor(
            "place.decor_banner", position, "prp_banner_guild.glb",
            new Color(0.35f, 0.12f, 0.16f), new Vector3(0.5f, 3f, 0.3f)),
        _ => null,
    };

    /// <summary>A placed crafting station — the same actor the town hub authors by hand, built by the
    /// factory the roadmap named. <paramref name="nameKey"/> is a <c>Loc</c> key: the station's name
    /// reaches the interaction prompt, and §6 admits no literals there.</summary>
    private static Node3D Station(
        CraftingStationType station, string nameKey, Vector3 position, Color color, string model) =>
        CraftingStationFactory.Create(
            station, Localization.Loc.T(nameKey), position, color, ModelRoot + model);

    /// <summary>A decoration: an entity with a model and a collider, and no interaction at all. It is
    /// deliberately not an <c>InteractableComponent</c> — the only verb a decoration has is Remove,
    /// and placement mode owns that.</summary>
    private static Node3D Decor(
        string nameKey, Vector3 position, string model, Color color, Vector3 size)
    {
        var entity = new Entity
        {
            Name = "Decor",
            DisplayName = Localization.Loc.T(nameKey),
            Position = position,
        };

        if (GD.Load<PackedScene>(ModelRoot + model)?.Instantiate() is Node3D visual)
        {
            visual.Name = "Mesh";
            entity.AddChild(visual);
        }
        else
        {
            entity.AddChild(new MeshInstance3D
            {
                Name = "Mesh",
                Mesh = new BoxMesh { Size = size },
                Position = new Vector3(0f, size.Y * 0.5f, 0f),
                MaterialOverride = new StandardMaterial3D { AlbedoColor = color },
            });
        }

        var collider = new StaticBody3D { Name = "Collider" };
        collider.AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = size },
            Position = new Vector3(0f, size.Y * 0.5f, 0f),
        });
        entity.AddChild(collider);

        return entity;
    }
}
