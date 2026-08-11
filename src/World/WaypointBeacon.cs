using Embervale.Core.Services;
using Embervale.UI;
using Godot;

namespace Embervale.World;

/// <summary>
/// The player's map waypoint, standing in the world (Phase 39.5A).
///
/// A mark you can only see by opening the map is a mark you have to keep opening the map to follow.
/// This is the other half: right-click anywhere on the map and a shaft of light stands there, so the
/// player walks toward a thing they can see rather than toward a memory of a pin. The compass strip
/// carries the same waypoint as a bearing, which is what makes it usable when the beacon is behind a
/// building or over a hill.
///
/// ⚠️ <b>It is drawn at ground level and made tall rather than placed on the terrain.</b> The map
/// hands over an X/Z with no Y — a top-down click cannot know a height — and the realm has no
/// heightmap to query (verticality is props and one 0.3 m dais, invariant 16). A 60 m shaft rising
/// from y=0 is correct everywhere the player can actually stand, and needs no raycast that could
/// miss, hit a rooftop, or fire before the cell it is over has streamed in.
///
/// ⚠️ <b>Unshaded and depth-tested off for the shaft.</b> A lit cylinder reads as an object in the
/// world — a strange glass pillar someone built — rather than as an overlay. It must not be mistaken
/// for something you can interact with, because it is not.
/// </summary>
[GlobalClass]
public partial class WaypointBeacon : Node3D
{
    /// <summary>How tall the shaft stands. Tall enough to clear the tallest building in the realm
    /// (the bell tower, ~11 m) several times over, so it is visible across a cell.</summary>
    private const float Height = 60f;

    private const float Radius = 0.35f;

    /// <summary>Matches the map's own waypoint colour, so the mark on the plot and the mark in the
    /// world are recognisably the same thing.</summary>
    private static readonly Color Tint = UiTheme.AccentHot;

    private MapService? _map;
    private int _shownRevision = -1;
    private float _sinceCheck;
    private float _spin;

    public override void _Ready()
    {
        Visible = false;
        AddChild(BuildShaft());
        AddChild(BuildRing());
    }

    /// <summary>The column of light itself.</summary>
    private static MeshInstance3D BuildShaft()
    {
        var mesh = new CylinderMesh
        {
            TopRadius = Radius * 0.35f,   // tapers, so it reads as light rather than as masonry
            BottomRadius = Radius,
            Height = Height,
            RadialSegments = 10,
            Rings = 1,
        };

        return new MeshInstance3D
        {
            Name = "Shaft",
            Mesh = mesh,
            Position = new Vector3(0f, Height * 0.5f, 0f),
            MaterialOverride = BeamMaterial(0.30f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
    }

    /// <summary>A ring on the ground, so the beacon marks a SPOT and not just a direction.</summary>
    private static MeshInstance3D BuildRing()
    {
        var mesh = new TorusMesh
        {
            InnerRadius = 1.5f,
            OuterRadius = 1.9f,
            RingSegments = 6,
            Rings = 20,
        };

        return new MeshInstance3D
        {
            Name = "Ring",
            Mesh = mesh,
            Position = new Vector3(0f, 0.06f, 0f),
            MaterialOverride = BeamMaterial(0.55f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
    }

    private static StandardMaterial3D BeamMaterial(float alpha) => new()
    {
        AlbedoColor = new Color(Tint, alpha),
        EmissionEnabled = true,
        Emission = Tint,
        EmissionEnergyMultiplier = 1.6f,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };

    public override void _Process(double delta)
    {
        // The ring turns slowly, which is the whole animation budget this needs: a static ring on the
        // ground reads as a decal someone painted, a turning one reads as a marker that is live.
        _spin += (float)delta * 0.6f;
        if (GetNodeOrNull<Node3D>("Ring") is { } ring)
        {
            ring.Rotation = new Vector3(0f, _spin, 0f);
        }

        _sinceCheck += (float)delta;
        if (_sinceCheck < 0.25f)
        {
            return;
        }

        _sinceCheck = 0f;
        _map ??= ServiceLocator.Instance is { } locator && locator.TryGet(out MapService map) ? map : null;

        if (_map is not { } service || _shownRevision == service.Revision)
        {
            return;
        }

        _shownRevision = service.Revision;
        Apply(service.Waypoint);
    }

    /// <summary>Moves the beacon to the waypoint, or hides it when there is none.</summary>
    private void Apply(Vector3? waypoint)
    {
        if (waypoint is not { } at)
        {
            Visible = false;
            return;
        }

        // The map's waypoint has no meaningful Y (it comes from a top-down click), so the beacon is
        // planted at ground level rather than at whatever height the click implied.
        GlobalPosition = new Vector3(at.X, 0f, at.Z);
        Visible = true;
    }
}
