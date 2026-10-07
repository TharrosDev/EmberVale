using Godot;

namespace Embervale.Combat;

/// <summary>
/// How big a body is, read off the body itself, for everything that has to sit on it or look at it:
/// the lock-on point, the floating plate, status marks, the framing camera, the add ring.
///
/// <para><b>Two heights, on purpose.</b> A large enemy's collision capsule is smaller than the model
/// drawn over it (<see cref="Enemies.EnemyArchetypeResource.VisualHeight"/>): the capsule decides how
/// far it reaches, the model how big it looks. So what must be <i>inside</i> the body (the lock-on
/// point, a line-of-sight ray) reads the capsule, and what must clear its <i>head</i> (a plate, a
/// mark) reads the visual height, which falls back to the capsule for every body that does not
/// declare one.</para>
/// </summary>
public static class BodyMetrics
{
    /// <summary>Node metadata a factory sets on a body whose model stands taller than its capsule:
    /// metres from the body's origin to the top of the model.</summary>
    public static readonly StringName VisualHeightMeta = "visual_height";

    /// <summary>The lock-on height used for a body with no collision shape to read.</summary>
    public const float DefaultAimHeight = 1f;

    /// <summary>The body's own collision shape: its first direct <see cref="CollisionShape3D"/>
    /// child. Hurtboxes and hitboxes hold theirs one level down, so they are never mistaken for it.</summary>
    public static CollisionShape3D? Collider(Node3D body)
    {
        int children = body.GetChildCount();
        for (int i = 0; i < children; i++)
        {
            if (body.GetChild(i) is CollisionShape3D { Shape: not null } shape)
            {
                return shape;
            }
        }

        return null;
    }

    /// <summary>Metres from the body's origin to the top of its capsule, or
    /// <paramref name="fallback"/> when it has none.</summary>
    public static float CapsuleTop(Node3D body, float fallback)
    {
        return Collider(body) is { Shape: CapsuleShape3D capsule } shape
            ? shape.Position.Y + (capsule.Height * 0.5f)
            : fallback;
    }

    /// <summary>The capsule's radius, or <paramref name="fallback"/> when the body has none.</summary>
    public static float CapsuleRadius(Node3D body, float fallback)
    {
        return Collider(body) is { Shape: CapsuleShape3D capsule } ? capsule.Radius : fallback;
    }

    /// <summary>Metres from the body's origin to the top of what is drawn: the declared visual
    /// height when the factory set one, otherwise the capsule's top, otherwise
    /// <paramref name="fallback"/>.</summary>
    public static float VisualHeight(Node3D body, float fallback)
    {
        float declared = DeclaredVisualHeight(body);
        return declared > 0f ? declared : CapsuleTop(body, fallback);
    }

    /// <summary>The visual height a factory declared for this body, or 0 when it declared none
    /// (the capsule is the body).</summary>
    public static float DeclaredVisualHeight(Node3D body)
    {
        if (!body.HasMeta(VisualHeightMeta))
        {
            return 0f;
        }

        float declared = body.GetMeta(VisualHeightMeta).AsSingle();
        return float.IsFinite(declared) && declared > 0f ? declared : 0f;
    }

    /// <summary>
    /// The point a lock-on holds and a sight ray ends on: the middle of the body's collision shape.
    /// For a body standing on its origin that is half its capsule height, so a five-metre boss is
    /// held at the waist rather than the ankle and a wolf at the shoulder rather than above its
    /// back; for a body whose origin is its centre (the practice dummies) it is the origin.
    /// </summary>
    public static Vector3 AimPoint(Node3D body)
    {
        float height = Collider(body) is { } shape ? shape.Position.Y : DefaultAimHeight;
        return body.GlobalPosition + (Vector3.Up * height);
    }
}
