using Godot;

namespace Embervale.World;

/// <summary>
/// Builds one cell's ground for the offline world package: rendered terrain and static collision.
///
/// ⚠️ <b>THIS IS THE GROUND NOW, NOT A SKIN OVER IT (the 2026-08-29 geography overhaul).</b> Every
/// cell used to carry a 60×0.5×60 <c>BoxMesh</c> floor with a matching <c>BoxShape3D</c>, and this
/// node laid a 4 cm visual wobble 1.2 cm above it. Those slabs are deleted: the terrain mesh built
/// from the region's <see cref="WorldHeightfield"/> carries the collision, and because the collider
/// is parented into the cell's <c>NavigationRegion3D</c> before <see cref="CellNavBaker"/>'s
/// deferred bake, the navmesh follows the elevation with no extra wiring.
///
/// ⚠️ <b>THE COLLIDER IS BUILT IN <see cref="Attach"/>, NOT IN <c>_Ready</c>.</b> The baker defers one
/// idle turn precisely so runtime geometry is final before it parses colliders; a collider created
/// in this node's <c>_Ready</c> would still make that window, but the streamer attaches this before
/// the cell enters the tree, so building eagerly removes the ordering question entirely.
///
/// It creates no persistent state.
/// </summary>
public sealed partial class WorldCellPresentation : Node3D
{
    private const string ShaderPath = "res://assets/shaders/world/world_surface.gdshader";
    private MeshInstance3D? _surface;
    private StaticBody3D? _collider;

    /// <summary>
    /// Adds the terrain to <paramref name="cellRoot"/>. <paramref name="field"/> is the cell's
    /// clipped view (see <see cref="WorldTerrainMeshBuilder.ViewFor"/>). The rendered surface hangs off this node;
    /// the collider is parented to the cell's <c>Nav</c> region when it has one, because
    /// <c>geometry_parsed_geometry_type = 1</c> means the bake only sees static colliders that are
    /// descendants of the <see cref="NavigationRegion3D"/>.
    /// </summary>
    public static void Attach(
        Node3D cellRoot,
        WorldEnvironmentProfileResource? region,
        WorldCellPresentationResource? cell,
        WorldHeightfield? field,
        Vector3 worldOrigin,
        WorldTerrainData? prebuilt = null)
    {
        if (region == null || cell == null || field == null)
        {
            return;
        }

        // Prefer the worker's result. The fallback recomputes inline and is what tests, tools and
        // any cell that outran its own job get - it is the same arithmetic either way.
        WorldTerrainData data = prebuilt ?? WorldTerrainMeshBuilder.BuildData(field, cell, worldOrigin);
        ArrayMesh topology = WorldTerrainMeshBuilder.Assemble(data);

        var presentation = new WorldCellPresentation { Name = "WorldPresentation" };
        presentation._surface = BuildSurface(region, cell, topology);
        presentation.AddChild(presentation._surface);
        cellRoot.AddChild(presentation);

        var collider = new StaticBody3D
        {
            Name = "TerrainCollider",

            // World, plus CameraBlocker: the ground is one of the things the third-person camera
            // must not pass through. Actors sit on World too (CharacterEntity defaults to it), so
            // sweeping World alone is what let a companion walking behind the player yank the
            // camera in — see CombatLayers.CameraBlocker.
            CollisionLayer = Combat.CombatLayers.World | Combat.CombatLayers.CameraBlocker,
        };
        collider.AddChild(new CollisionShape3D
        {
            Name = "Shape",
            Shape = WorldTerrainMeshBuilder.AssembleCollision(data),
        });
        presentation._collider = collider;
        (cellRoot.GetNodeOrNull<NavigationRegion3D>("Nav") ?? (Node)cellRoot).AddChild(collider);
    }

    public override void _ExitTree()
    {
        if (_surface != null)
        {
            Material? material = _surface.MaterialOverride;
            Mesh? mesh = _surface.Mesh;
            _surface.MaterialOverride = null;
            _surface.Mesh = null;
            material?.Dispose();
            mesh?.Dispose();
            _surface = null;
        }

        if (_collider != null)
        {
            if (_collider.GetNodeOrNull<CollisionShape3D>("Shape") is { } shapeNode)
            {
                Shape3D? shape = shapeNode.Shape;
                shapeNode.Shape = null;
                shape?.Dispose();
            }
            _collider = null;
        }
    }

    private static MeshInstance3D BuildSurface(
        WorldEnvironmentProfileResource region, WorldCellPresentationResource cell,
        ArrayMesh topology)
    {
        // The world-generation visualiser replaces the whole terrain material with a flat unlit
        // ramp so a field can be READ off the ground rather than guessed at through six blended
        // layers, three noise octaves and a sun. Turn it on with `worldgen <field>` in the F1
        // console and reload the region; `worldgen none` puts the realm back.
        if (WorldGenerationDebug.Mode != WorldGenerationDebugMode.None)
        {
            return new MeshInstance3D
            {
                Name = "SurfaceSkin",
                Mesh = topology,
                MaterialOverride = new StandardMaterial3D
                {
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    VertexColorUseAsAlbedo = true,
                },
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
        }

        Shader? shader = GD.Load<Shader>(ShaderPath);
        var material = new ShaderMaterial { Shader = shader };
        WorldBiomeProfileResource biome = cell.Biome ?? region.Biome ?? FallbackBiome(region);

        // ⚠️ SIX SLOTS, ALWAYS, AND A NULL SLOT FALLS BACK TO Ground. The shader indexes fixed
        // positions; a biome that leaves Cap or Shore unauthored (most lowland ones do) must still
        // hand it six valid entries or the layer reads whatever was in the buffer last.
        WorldTerrainLayerResource?[] slots =
        {
            biome.Ground, biome.Sparse, biome.Rock, biome.Cap, biome.Road, biome.Shore,
        };
        var low = new Vector3[slots.Length];
        var high = new Vector3[slots.Length];
        var parameters = new Vector4[slots.Length];
        var specular = new float[slots.Length];
        for (int i = 0; i < slots.Length; i++)
        {
            WorldTerrainLayerResource resolved =
                slots[i] ?? biome.Ground ?? new WorldTerrainLayerResource();
            Color lowLinear = resolved.Low.SrgbToLinear();
            Color highLinear = resolved.High.SrgbToLinear();
            low[i] = new Vector3(lowLinear.R, lowLinear.G, lowLinear.B);
            high[i] = new Vector3(highLinear.R, highLinear.G, highLinear.B);
            parameters[i] = new Vector4(
                resolved.Grain, resolved.Breakup, resolved.Relief, resolved.Roughness);
            specular[i] = resolved.Specular;
        }

        material.SetShaderParameter("layer_low", low);
        material.SetShaderParameter("layer_high", high);
        material.SetShaderParameter("layer_params", parameters);
        material.SetShaderParameter("layer_specular", specular);
        material.SetShaderParameter("slope_band", biome.SlopeBand);
        material.SetShaderParameter("height_band", biome.HeightBand);
        material.SetShaderParameter("cap_slope_shed", biome.CapSlopeShed);
        material.SetShaderParameter("sparse_coverage", biome.SparseCoverage);
        material.SetShaderParameter("macro_scale", biome.MacroScale);
        material.SetShaderParameter("macro_strength", biome.MacroStrength);
        material.SetShaderParameter("shore_level", biome.ShoreLevel);
        material.SetShaderParameter("shore_band", biome.ShoreBand);
        material.SetShaderParameter("strata_scale", biome.StrataScale);
        material.SetShaderParameter("strata_strength", biome.StrataStrength);
        material.SetShaderParameter("terrain_seed", (float)region.TerrainSeed);
        // Retained so authored cells that still carry them load cleanly. Nothing reads them: the
        // per-cell tint was a flat wash over a rectangle and the generated environment replaced it.
        material.SetShaderParameter("tint", cell.Tint);
        material.SetShaderParameter("tint_strength", cell.TintStrength);

        return new MeshInstance3D
        {
            Name = "SurfaceSkin",
            Mesh = topology,
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
        };
    }

    /// <summary>
    /// The pre-biome look, rebuilt as six layers so a region with no authored biome still renders.
    /// ⚠️ It is a compatibility path and it looks like the compatibility path: flat tones with no
    /// grain worth the name. If a region reaches a screenshot through here, the fix is to author a
    /// <see cref="WorldBiomeProfileResource"/>, not to tune these numbers.
    /// </summary>
    private static WorldBiomeProfileResource FallbackBiome(WorldEnvironmentProfileResource region)
    {
        static WorldTerrainLayerResource Layer(Color low, Color high, float grain, float roughness) =>
            new()
            {
                Low = low, High = high, Grain = grain, Breakup = 0.6f, Relief = 0.2f,
                Roughness = roughness, Specular = 0.35f,
            };

        return new WorldBiomeProfileResource
        {
            Id = "biome.fallback",
            Ground = Layer(region.SurfaceColor, region.SecondaryColor, 6f, region.SurfaceRoughness),
            Sparse = Layer(region.SecondaryColor, region.SurfaceColor, 9f, region.SurfaceRoughness),
            Rock = Layer(region.DetailColor, region.DetailColor * 1.4f, 3.5f, region.DetailRoughness),
            Cap = Layer(region.DetailColor * 1.2f, region.SecondaryColor, 5f, region.DetailRoughness),
            Road = Layer(region.RoadColor * 0.85f, region.RoadColor, 2.5f, region.RoadRoughness),
            Shore = Layer(region.SurfaceColor * 0.7f, region.SecondaryColor * 0.8f, 3f, 0.55f),
            SlopeBand = new Vector2(region.SlopeBlendStart, region.SlopeBlendEnd),
            HeightBand = new Vector2(region.HeightBlendStart, region.HeightBlendEnd),
            MacroScale = 90f,
            MacroStrength = 0.18f,
        };
    }
}
