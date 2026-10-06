using Embervale.Core.Pooling;
using Godot;

namespace Embervale.Magic;

/// <summary>
/// A short-lived expanding, fading sphere that visualises an area-of-effect burst.
/// Purely cosmetic and self-reclaiming — it has no gameplay role (the damage is already
/// resolved by <see cref="SpellResolver"/>), it just makes a burst legible at a glance.
///
/// <para><b>Pooled.</b> A flash lives for a third of a second and a fight makes a great many of
/// them: one per cast, one per burst, one per broken ward, and a whole line of them per tick of a
/// breath. Each used to be two nodes, a material and a freshly generated sphere mesh, built and
/// freed. Spawn through <see cref="Spawn"/>; the node goes back to the pool when it fades, and the
/// sphere is one shared mesh. The pool belongs to the session (<see cref="OpenPool"/> /
/// <see cref="ClosePool"/>, called by <c>SpellVfxDirector</c>); with no pool open a flash is
/// simply built and freed as before, so a probe or a bare scene needs no setup.</para>
/// </summary>
public partial class SpellFlash : Node3D
{
    /// <summary>The radius the flash grows to (matched to the spell's burst radius).</summary>
    public float Radius { get; set; } = 2f;

    public Color FlashColor { get; set; } = Colors.White;

    private const float SeedRadius = 0.1f;
    private const double LifeSeconds = 0.3d;

    private static SphereMesh? _sharedSphere;
    private static NodePool<SpellFlash>? _pool;

    private readonly MeshInstance3D _mesh;
    private readonly StandardMaterial3D _material;
    private double _age;

    /// <summary>
    /// Builds its mesh and material here rather than in <c>_Ready</c>: a pooled node's
    /// <c>_Ready</c> runs once in its life, and <see cref="Arm"/> has to be able to restyle it on
    /// every reuse whether or not it has reached the tree yet.
    /// </summary>
    public SpellFlash()
    {
        _material = new StandardMaterial3D
        {
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            EmissionEnabled = true,
        };

        if (_sharedSphere == null || !IsInstanceValid(_sharedSphere))
        {
            _sharedSphere = new SphereMesh { Radius = SeedRadius, Height = SeedRadius * 2f };
        }

        _mesh = new MeshInstance3D
        {
            Mesh = _sharedSphere,
            MaterialOverride = _material,
        };
        AddChild(_mesh);
    }

    /// <summary>Opens the session's flash pool. Idempotent.</summary>
    public static void OpenPool() => _pool ??= new NodePool<SpellFlash>(() => new SpellFlash());

    /// <summary>Frees every parked flash and closes the pool. A flash still fading in the world
    /// finds no pool when it finishes and frees itself.</summary>
    public static void ClosePool()
    {
        _pool?.Clear();
        _pool = null;
    }

    /// <summary>Puts a flash of <paramref name="radius"/> and <paramref name="color"/> into the
    /// world under <paramref name="parent"/> at <paramref name="globalPosition"/>.</summary>
    public static void Spawn(Node parent, Vector3 globalPosition, float radius, Color color)
    {
        SpellFlash flash = _pool?.Get() ?? new SpellFlash();
        flash.Radius = radius;
        flash.FlashColor = color;
        parent.AddChild(flash);
        flash.GlobalPosition = globalPosition;
        flash.Arm();
    }

    /// <summary>Resets everything a previous life left behind: age, size and tint.</summary>
    private void Arm()
    {
        _age = 0d;
        _mesh.Scale = Vector3.One;
        _material.Emission = FlashColor;
        _material.AlbedoColor = new Color(FlashColor.R, FlashColor.G, FlashColor.B, 0.5f);
    }

    public override void _Ready() => Arm();

    public override void _Process(double delta)
    {
        _age += delta;
        float t = (float)(_age / LifeSeconds);
        if (t >= 1f)
        {
            if (_pool != null)
            {
                _pool.Return(this);
            }
            else
            {
                QueueFree();
            }

            return;
        }

        float scale = Mathf.Lerp(1f, Radius / SeedRadius, t);
        _mesh.Scale = Vector3.One * scale;
        _material.AlbedoColor = new Color(FlashColor.R, FlashColor.G, FlashColor.B, 0.5f * (1f - t));
    }
}
