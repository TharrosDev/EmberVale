using System.Collections.Generic;
using Embervale.Combat;
using Embervale.Core;
using Embervale.Core.Services;
using Embervale.Entities;
using Embervale.Settings;
using Godot;

namespace Embervale.Player;

/// <summary>
/// Thins out whatever stands between the third-person camera and the player, so the view is never a
/// wall of bark, a doorframe or a companion's back.
///
/// <para><b>What blocks the view, and what does not.</b> Walls and terrain block the camera itself
/// (the wall spring in <see cref="PlayerCameraRig"/> keeps it in front of anything on
/// <see cref="CombatLayers.CameraBlocker"/>) and stay solid to the eye: this never touches them.
/// What is left is everything the camera is allowed to pass through and that can still sit in the way,
/// which is actors and placed props. Baked architecture and scatter are merged into shared meshes and
/// are never fadeable one by one, which is also why nothing here reaches for them.</para>
///
/// <para><b>How.</b> Twenty times a second one capsule is swept from the camera to just short of the
/// player's chest and every fadeable collider it overlaps is grouped by its owning entity (or, for a bare
/// prop, its parent). Each group's meshes have their <see cref="GeometryInstance3D.Transparency"/> eased
/// toward <see cref="CameraOcclusionMath.MaxTransparency"/> and back. That property is per instance and
/// uses the renderer's own dithering, so it needs no material duplicated, swapped or overridden, and the
/// original value is remembered and written back exactly.</para>
///
/// <para><b>Cost.</b> One <c>IntersectShape</c> per 50 ms (its result arrays are the only per-scan
/// allocation, and they are proportional to the hit count, capped at <see cref="MaxHits"/>); the
/// children of a collider are enumerated once, the first time it is seen, and cached. Per frame it is one
/// float step per faded group and a write only when a mesh's value actually moved. With nothing in the
/// way, the frame cost is a dictionary that is empty.</para>
/// </summary>
[GlobalClass]
public partial class CameraOcclusion : EntityComponent
{
    /// <summary>Seconds between scans. The fade itself steps every frame; only the question "what is in
    /// the way" is asked at this rate.</summary>
    private const float ScanInterval = 0.05f;

    private const int MaxHits = 12;

    /// <summary>Most meshes one group will fade, so an entity with a sprawling subtree cannot turn one
    /// hit into a hundred property writes.</summary>
    private const int MaxMeshesPerGroup = 32;

    /// <summary>How high above the player's origin (their feet) the view must be kept clear to.</summary>
    private const float TargetHeight = 1.2f;

    /// <summary>A bare prop (no entity) is only faded when its parent holds this many children or fewer.</summary>
    private const int MaxBareSiblings = 8;

    /// <summary>Remembered "nothing to fade here" verdicts before the cache is cleared and rebuilt.</summary>
    private const int MaxIgnored = 512;

    private sealed class FadeGroup
    {
        public GeometryInstance3D?[] Meshes = System.Array.Empty<GeometryInstance3D?>();
        public float[] Original = System.Array.Empty<float>();
        public float[] Applied = System.Array.Empty<float>();
        public float Fade;
        public bool Wanted;
        public int SeenScan;
    }

    private readonly Dictionary<ulong, FadeGroup> _groups = new();
    private readonly HashSet<ulong> _ignored = new();
    private readonly List<ulong> _release = new();

    private PlayerCameraRig? _rig;
    private SettingsService? _settings;
    private PhysicsShapeQueryParameters3D? _query;
    private CapsuleShape3D? _capsule;
    private float _scanClock;
    private int _scanId;

    protected override void OnInitialize()
    {
        _rig = Entity!.GetComponent<PlayerCameraRig>();
        _settings = ServiceLocator.Instance is { } locator && locator.TryGet(out SettingsService settings)
            ? settings
            : null;
    }

    protected override void OnTeardown()
    {
        RestoreAll();
        _query?.Dispose();
        _capsule?.Dispose();
        _query = null;
        _capsule = null;
    }

    public override void _Process(double delta)
    {
        // Not playing (loading, game over, a world teardown): the camera and the meshes may be freed
        // any moment, so touch nothing but put back what was changed.
        if (GameManager.Instance is { IsPlaying: false } || Entity?.Body is not { } body || !GodotObject.IsInstanceValid(body))
        {
            RestoreAll();
            return;
        }

        float dt = (float)delta;
        bool run = _rig?.Camera is { } camera && GodotObject.IsInstanceValid(camera) &&
            CameraOcclusionMath.Applies(
                _settings?.Current.ObstructionFade ?? true, _rig.IsFirstPerson, _rig.Pullback);

        if (!run)
        {
            // Everything is on its way back; a fade that was in flight finishes rather than snapping.
            foreach (FadeGroup group in _groups.Values)
            {
                group.Wanted = false;
            }
        }
        else
        {
            _scanClock += dt;
            if (_scanClock >= ScanInterval)
            {
                _scanClock = 0f;
                Scan(body, _rig!.Camera!.GlobalPosition);
            }
        }

        Step(dt);
    }

    /// <summary>Asks what stands between the camera and the player and marks those groups as wanted.
    /// Groups the scan no longer sees are marked unwanted and fade back in <see cref="Step"/>.</summary>
    private void Scan(Node3D body, Vector3 cameraPosition)
    {
        _scanId++;
        if (CameraOcclusionMath.TrySegment(
                cameraPosition, body.GlobalPosition + (Vector3.Up * TargetHeight),
                out Vector3 centre, out float height, out Vector3 axis) &&
            body is CollisionObject3D self)
        {
            PhysicsShapeQueryParameters3D query = Query(self);
            _capsule!.Height = height;
            query.Transform = new Transform3D(CameraOcclusionMath.CapsuleBasis(axis), centre);

            foreach (Godot.Collections.Dictionary hit in
                body.GetWorld3D().DirectSpaceState.IntersectShape(query, MaxHits))
            {
                if (hit["collider"].AsGodotObject() is CollisionObject3D collider)
                {
                    Mark(collider);
                }
            }
        }

        foreach (FadeGroup group in _groups.Values)
        {
            group.Wanted = group.SeenScan == _scanId;
        }
    }

    private PhysicsShapeQueryParameters3D Query(CollisionObject3D self)
    {
        if (_query == null)
        {
            _capsule = new CapsuleShape3D { Radius = CameraOcclusionMath.ProbeRadius };

            // Bodies only, and only what can stand in the view: the camera-blocker layer is left out
            // of the mask so terrain and walls never spend one of the hits.
            _query = new PhysicsShapeQueryParameters3D
            {
                Shape = _capsule,
                CollideWithAreas = false,
                CollideWithBodies = true,
                CollisionMask = CombatLayers.WorldStatic | CombatLayers.WorldDynamic | CombatLayers.Enemy |
                    CombatLayers.Npc,
                Exclude = new Godot.Collections.Array<Rid> { self.GetRid() },
            };
        }

        return _query;
    }

    /// <summary>Marks the group a collider belongs to as seen this scan, building it the first time.</summary>
    private void Mark(CollisionObject3D collider)
    {
        ulong colliderId = collider.GetInstanceId();
        if (_ignored.Contains(colliderId) || !CameraOcclusionMath.IsFadeableLayer(collider.CollisionLayer))
        {
            return;
        }

        IEntity? owner = EntityNode.FindOwner(collider);
        if (ReferenceEquals(owner, Entity))
        {
            return;
        }

        Node? root = owner?.Body ?? collider.GetParent();
        if (root == null)
        {
            return;
        }

        ulong key = root.GetInstanceId();
        if (!_groups.TryGetValue(key, out FadeGroup? group))
        {
            group = Build(root, owner != null);
            if (group == null)
            {
                Ignore(colliderId);
                return;
            }

            _groups[key] = group;
        }

        group.SeenScan = _scanId;
    }

    private void Ignore(ulong id)
    {
        if (_ignored.Count >= MaxIgnored)
        {
            _ignored.Clear();
        }

        _ignored.Add(id);
    }

    /// <summary>Collects the fadeable meshes under <paramref name="root"/>. An entity's whole subtree
    /// counts; a bare prop's only its own direct meshes, because its parent may be a cell root holding
    /// everything around it.</summary>
    private static FadeGroup? Build(Node root, bool ownedByEntity)
    {
        // A bare prop's parent may be a cell root holding everything around it: a crowded parent is
        // not "this prop's model", and fading its direct meshes would thin a whole street.
        if (!ownedByEntity && root.GetChildCount() > MaxBareSiblings)
        {
            return null;
        }

        var found = new List<GeometryInstance3D>();
        Gather(root, ownedByEntity, ownedByEntity ? 6 : 1, found);
        if (found.Count == 0)
        {
            return null;
        }

        var group = new FadeGroup
        {
            Meshes = found.ToArray(),
            Original = new float[found.Count],
            Applied = new float[found.Count],
        };
        for (int i = 0; i < found.Count; i++)
        {
            group.Original[i] = found[i].Transparency;
            group.Applied[i] = group.Original[i];
        }

        return group;
    }

    private static void Gather(Node node, bool ownedByEntity, int depth, List<GeometryInstance3D> into)
    {
        foreach (Node child in node.GetChildren())
        {
            if (into.Count >= MaxMeshesPerGroup)
            {
                return;
            }

            if (child is MeshInstance3D { Visible: true, Mesh: not null } mesh &&
                CameraOcclusionMath.IsFadeableMesh((mesh.GlobalTransform * mesh.GetAabb()).GetLongestAxisSize(), ownedByEntity))
            {
                into.Add(mesh);
            }

            if (depth > 1 && child is not CollisionObject3D)
            {
                Gather(child, ownedByEntity, depth - 1, into);
            }
        }
    }

    /// <summary>Advances every group's fade, writes the meshes that moved, and releases the finished.</summary>
    private void Step(float dt)
    {
        if (_groups.Count == 0)
        {
            return;
        }

        _release.Clear();
        foreach (KeyValuePair<ulong, FadeGroup> pair in _groups)
        {
            FadeGroup group = pair.Value;
            group.Fade = CameraOcclusionMath.StepFade(group.Fade, group.Wanted, dt);

            bool anyLive = false;
            for (int i = 0; i < group.Meshes.Length; i++)
            {
                GeometryInstance3D? mesh = group.Meshes[i];
                if (mesh == null || !GodotObject.IsInstanceValid(mesh))
                {
                    // Freed mid-fade: nothing left to restore on it.
                    group.Meshes[i] = null;
                    continue;
                }

                anyLive = true;
                // Exactly the authored value at zero fade; the easing is only trusted in between.
                float value = group.Fade <= 0f
                    ? group.Original[i]
                    : CameraOcclusionMath.TransparencyAt(group.Original[i], group.Fade);
                if (value != group.Applied[i] && (group.Fade <= 0f || Mathf.Abs(value - group.Applied[i]) > 0.002f))
                {
                    group.Applied[i] = value;
                    mesh.Transparency = value;
                }
            }

            if (!anyLive || CameraOcclusionMath.IsReleased(group.Fade, group.Wanted))
            {
                _release.Add(pair.Key);
            }
        }

        foreach (ulong key in _release)
        {
            _groups.Remove(key);
        }
    }

    /// <summary>Puts every touched mesh back to its authored transparency and forgets it. Safe on freed
    /// meshes.</summary>
    private void RestoreAll()
    {
        if (_groups.Count == 0)
        {
            return;
        }

        foreach (FadeGroup group in _groups.Values)
        {
            for (int i = 0; i < group.Meshes.Length; i++)
            {
                if (group.Meshes[i] is { } mesh && GodotObject.IsInstanceValid(mesh))
                {
                    mesh.Transparency = group.Original[i];
                }
            }
        }

        _groups.Clear();
    }
}
