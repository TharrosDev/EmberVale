using System;
using Embervale.Combat;
using Embervale.Core.Services;
using Embervale.Entities;
using Embervale.Settings;
using Embervale.Stats;
using Godot;

namespace Embervale.Player;

/// <summary>
/// Points the spellcasting aim node at whatever the crosshair converges on, so a bolt goes where the
/// reticle is instead of along the head's forward.
///
/// <para>In first person the convergence point lies on the pivot's own forward axis, so the node's
/// aim is unchanged from before the camera rig existed — that is the invariant that keeps this safe
/// for the shipping mode.</para>
/// </summary>
[GlobalClass]
public partial class AimController : EntityComponent
{
    /// <summary>How far the crosshair convergence ray reaches before falling back to a far point.</summary>
    private const float AimTraceDistance = 200f;

    /// <summary>A hit nearer than this to the trace start is treated as no hit: a wall at the
    /// player's nose would otherwise spin the aim sharply toward its own surface.</summary>
    private const float MinConvergence = 1.5f;

    /// <summary>Distances along the aim ray at which the assist looks for a target near the crosshair.</summary>
    private static readonly float[] AssistMarch = { 5f, 10f, 18f, 30f, 45f, 60f };

    private const int AssistOverlaps = 16;

    private PlayerCameraRig? _rig;
    private PlayerPhysicsQueries? _queries;
    private SettingsService? _settings;
    private LockOnComponent? _lockOn;
    private readonly WorldRay _sight = new();
    private readonly PhysicsShapeQueryParameters3D _probe = new()
    {
        Shape = new SphereShape3D(),
        CollisionMask = CombatLayers.Hurtbox,
        CollideWithAreas = true,
        CollideWithBodies = false,
    };

    /// <summary>The point the crosshair converges on, as of the last <see cref="Tick"/>. Bows shoot
    /// through this: the aim node's own position is the eye, not a point out along the aim.</summary>
    public Vector3 Focus { get; private set; }

    /// <summary>Harness seam: a value of 0 or more replaces the Aim Assist setting, which a headless
    /// probe cannot reach (no settings service). Negative, the default, reads the live setting.</summary>
    public float StrengthOverride { get; set; } = -1f;

    /// <summary>True once <see cref="Focus"/> holds a real convergence point.</summary>
    public bool HasFocus { get; private set; }

    /// <summary>Sets <see cref="Focus"/> directly. For harnesses with no camera; a live camera
    /// overwrites it on the next <see cref="Tick"/>.</summary>
    public void SetFocus(Vector3 point)
    {
        Focus = point;
        HasFocus = true;
    }

    /// <summary>The node spellcasting aims along, injected by <see cref="PlayerFactory"/>. It sits
    /// on the body but is re-aimed each frame at the point the crosshair converges on.</summary>
    public Node3D? AimNode { get; set; }

    protected override void OnInitialize()
    {
        _rig = Entity!.GetComponent<PlayerCameraRig>();
        _queries = Entity.GetComponent<PlayerPhysicsQueries>();
        _lockOn = Entity.GetComponent<LockOnComponent>();
        _settings = ServiceLocator.Instance is { } locator && locator.TryGet(out SettingsService settings)
            ? settings
            : null;
        _sight.Ignore(Entity.Body is CollisionObject3D body ? body.GetRid() : default);
    }

    protected override void OnTeardown()
    {
        _sight.Dispose();
        _probe.Dispose();
    }

    public void Tick()
    {
        if (AimNode == null || _queries == null || _rig?.Camera is not { } camera)
        {
            return;
        }

        // Cast from the pivot's depth, not the camera's: in third person the camera sits behind the
        // player, and a prop in that gap must not become the point the shot converges on. In first
        // person the pullback is zero, so this is the camera and nothing changes.
        Vector3 forward = -camera.GlobalTransform.Basis.Z;
        float pullback = _rig.Pullback;
        Vector3 from = FramingMath.AimTraceStart(camera.GlobalPosition, forward, pullback);
        Vector3? hit = _queries.Raycast(from, forward, AimTraceDistance) is { } found ? found.Point : null;
        Vector3 focus = FramingMath.AimConvergence(from, forward, hit, MinConvergence, AimTraceDistance);
        Focus = focus;
        HasFocus = true;

        Vector3 direction = CameraRigMath.AimDirection(AimNode.GlobalPosition, focus);
        if (Mathf.Abs(direction.Dot(Vector3.Up)) > 0.999f)
        {
            return; // straight up/down: LookAt has no valid basis, so keep the last aim
        }

        AimNode.LookAt(AimNode.GlobalPosition + direction, Vector3.Up);
    }

    /// <summary>
    /// The aim point after the bow's soft pull toward a hostile near the crosshair, scaled by the
    /// player's Aim Assist setting (0 changes nothing). A locked target is preferred and gets a wider
    /// cone; a target behind cover is never pulled toward. Bends the arrow only, never the camera.
    /// </summary>
    /// <param name="origin">Where the arrow leaves.</param>
    /// <param name="focus">Where the crosshair converges.</param>
    /// <param name="charge">The draw, 0..1: a steadier shot is pulled harder.</param>
    public Vector3 AssistedFocus(Vector3 origin, Vector3 focus, float charge)
    {
        float strength = StrengthOverride >= 0f
            ? Math.Clamp(StrengthOverride, 0f, 1f)
            : CombatComfort.From(_settings?.Current).AimAssist;
        Vector3 toFocus = focus - origin;
        if (strength <= 0f || Entity?.Body is not { } body || toFocus.LengthSquared() < 0.01f)
        {
            return focus;
        }

        Vector3 aim = toFocus.Normalized();
        int team = Entity.GetComponent<CombatComponent>()?.Team ?? 0;
        IEntity? locked = _lockOn?.Target;
        PhysicsDirectSpaceState3D space = body.GetWorld3D().DirectSpaceState;

        (Vector3 Point, float Score, bool Locked)? best = null;
        foreach (Hurtbox box in HostileHurtboxes(space, origin, aim, strength, team))
        {
            Vector3 point = box.GlobalPosition;
            Vector3 to = point - origin;
            float distance = to.Length();
            if (distance < 0.5f)
            {
                continue;
            }

            bool isLocked = locked != null && ReferenceEquals(box.OwnerEntity, locked);
            float angle = aim.AngleTo(to / distance);
            if (angle >= AimAssistMath.ConeRadians(strength, isLocked))
            {
                continue;
            }

            // Lock-on first, then the smallest angle; a weak point nudges ties toward itself.
            float score = angle - (isLocked ? 1f : 0f) - (0.001f * box.DamageMultiplier);
            if ((best == null || score < best.Value.Score) && !Covered(space, origin, point))
            {
                best = (point, score, isLocked);
            }
        }

        if (best is not { } pick)
        {
            return focus;
        }

        // Aim at the target's own distance: an arc solved through a wall far behind it would carry the
        // arrow high over the very body it was pulled toward.
        Vector3 toTarget = pick.Point - origin;
        Vector3 pulled = AimAssistMath.Pull(aim, toTarget.Normalized(), strength, charge, pick.Locked);
        return origin + (pulled * toTarget.Length());
    }

    /// <summary>Hurtboxes of living hostiles inside the assist cone, found by marching probe spheres
    /// (growing with distance, like the cone) along the aim ray.</summary>
    private System.Collections.Generic.IEnumerable<Hurtbox> HostileHurtboxes(
        PhysicsDirectSpaceState3D space, Vector3 origin, Vector3 aim, float strength, int team)
    {
        float tan = MathF.Tan(AimAssistMath.ConeRadians(strength, locked: true));
        var seen = new System.Collections.Generic.HashSet<ulong>();

        foreach (float d in AssistMarch)
        {
            ((SphereShape3D)_probe.Shape).Radius = Math.Max(0.6f, d * tan) + 0.6f;
            _probe.Transform = new Transform3D(Basis.Identity, origin + (aim * d));

            foreach (Godot.Collections.Dictionary hit in space.IntersectShape(_probe, AssistOverlaps))
            {
                if (hit["collider"].AsGodotObject() is Hurtbox { Combat: { } combat } box &&
                    combat.Team != team &&
                    box.OwnerEntity?.GetComponent<StatsComponent>() is { IsAlive: true } &&
                    seen.Add(box.GetInstanceId()))
                {
                    yield return box;
                }
            }
        }
    }

    /// <summary>True when solid world geometry lies between the two points. People do not count.</summary>
    private bool Covered(PhysicsDirectSpaceState3D space, Vector3 from, Vector3 to)
    {
        Vector3 back = (to - from).Normalized() * 0.3f;
        return _sight.FirstSolid(space, from, to - back) != null;
    }
}
