using System.Collections.Generic;
using Embervale.Core.Events;
using Embervale.Combat;
using Embervale.Entities;
using Embervale.Magic.Vfx;
using Embervale.Player;
using Embervale.UI;
using Godot;

namespace Embervale.Magic;

/// <summary>
/// World-space visuals for status effects (Phase 30I): while a status afflicts this entity, a small
/// looping school-tinted particle swirl hangs on the body — burning reads orange, chill ice-blue,
/// regrowth green — so an affliction is legible at a glance without reading the HUD. Purely
/// cosmetic: driven entirely by <see cref="StatusEffectAppliedEvent"/>/<see cref="StatusEffectRemovedEvent"/>;
/// the gameplay (ticks/modifiers) stays in <see cref="StatusEffectsComponent"/>.
///
/// <para><b>Two layers.</b> The plain marker built here (a <c>StatusVfx*</c> node under the body) is
/// what exists on every display, and what the headless gates assert. Where the spell effect layer is
/// live, <see cref="SpellVfx.StatusAura"/> draws the status over it from the same blocks the spells
/// use (embers, mist, an ice shell, roots, a sigil), and a marker that the richer picture replaces
/// outright (the swirl, the two shells) is hidden for as long as that picture lasts.</para>
///
/// <para><b>The local player</b> gets no marker (see <see cref="OnApplied"/>), but does get the
/// richer picture whenever the camera is outside their head: a ward or a skin of bark is the
/// player's own doing and should be seen in third person. It is taken down when the view goes
/// inside and put back when it comes out.</para>
/// </summary>
[GlobalClass]
public partial class StatusEffectVfxComponent : EntityComponent
{
    /// <summary>Height above the entity origin the swirl centres on.</summary>
    [Export] public float SwirlHeight { get; set; } = 1.1f;

    /// <summary>Seconds between checks of whether the player's view went inside or came out.</summary>
    private const double ViewCheckSeconds = 0.2d;

    private readonly Dictionary<string, Node3D> _active = new();

    // The richer picture of each status, where one is being drawn. Handles only: the effect nodes
    // belong to the effect layer, and a rig whose nodes are gone ignores every call.
    private readonly Dictionary<string, VfxRig> _auras = new();

    // Markers hidden because an aura replaces them, to be shown again if the aura is lost.
    private readonly HashSet<string> _concealed = new();

    // What the local player is wearing, so it can be drawn again when the view leaves their head.
    private readonly Dictionary<string, (StatusVfxShape Shape, DamageType School)> _worn = new();

    private double _viewCheck;

    /// <summary>Height above the entity origin a mark or glyph floats at.</summary>
    [Export] public float HeadHeight { get; set; } = 2.25f;

    protected override void OnInitialize()
    {
        EventBus.Instance?.Subscribe<StatusEffectAppliedEvent>(OnApplied);
        EventBus.Instance?.Subscribe<StatusEffectRemovedEvent>(OnRemoved);
        EventBus.Instance?.Subscribe<GameLoadingEvent>(OnGameLoading);
        SetProcess(false);
    }

    protected override void OnTeardown()
    {
        EventBus.Instance?.Unsubscribe<StatusEffectAppliedEvent>(OnApplied);
        EventBus.Instance?.Unsubscribe<StatusEffectRemovedEvent>(OnRemoved);
        EventBus.Instance?.Unsubscribe<GameLoadingEvent>(OnGameLoading);
        foreach (VfxRig rig in _auras.Values)
        {
            rig.Stop();
        }

        _auras.Clear();
        _concealed.Clear();
        _worn.Clear();
    }

    /// <summary>Only ever runs for the local player while a status is on them: watches for the view
    /// going inside their head (auras down) or coming back out (auras up).</summary>
    public override void _Process(double delta)
    {
        if (_worn.Count == 0)
        {
            SetProcess(false);
            return;
        }

        _viewCheck -= delta;
        if (_viewCheck > 0d)
        {
            return;
        }

        _viewCheck = ViewCheckSeconds;
        RefreshWorn();
    }

    /// <summary>A load frees every effect node: forget the rigs and show the markers they hid.</summary>
    private void OnGameLoading(GameLoadingEvent e)
    {
        _auras.Clear();
        foreach (string id in _concealed)
        {
            if (_active.TryGetValue(id, out Node3D? marker) && IsInstanceValid(marker))
            {
                marker.Visible = true;
                if (marker is GpuParticles3D particles)
                {
                    particles.Emitting = true;
                }
            }
        }

        _concealed.Clear();
    }

    private void OnApplied(StatusEffectAppliedEvent e)
    {
        if (!ReferenceEquals(e.Target, Entity) || _active.ContainsKey(e.EffectId) ||
            _worn.ContainsKey(e.EffectId) || StatusEffectDatabase.Get(e.EffectId) is not { } effect)
        {
            return;
        }

        StatusVfxShape shape = StatusVfxShapes.Pick(effect.Controls, effect.AbsorbAmount > 0f);

        // The camera lives inside the local player's first-person body. A billboard orbiting that
        // body inevitably crosses the near plane and becomes a screen-sized translucent square —
        // especially obvious when several effects are active. The HUD already carries the local
        // player's effect chips; world swirls are for actors the player is looking at.
        //
        // What the player does get is the effect layer's picture, which is not parented to the body,
        // fades near the lens, and is only drawn while the camera is outside their head.
        if (Entity?.Body is PlayerCharacter)
        {
            _worn[e.EffectId] = (shape, effect.School);
            _viewCheck = ViewCheckSeconds;
            SetProcess(true);
            RefreshWorn();
            return;
        }

        Color tint = SpellSchools.Color(effect.School);
        Node3D visual = shape == StatusVfxShape.Swirl ? BuildSwirl(tint) : BuildShape(shape, tint);
        Entity!.Body.AddChild(visual);
        visual.Position = shape == StatusVfxShape.Swirl ? new Vector3(0f, SwirlHeight, 0f) : Vector3.Zero;
        _active[e.EffectId] = visual;

        // The richer picture, where the effect layer is drawing. A swirl or a shell it replaces is
        // hidden (never freed: the marker is still what ends with the status).
        if (SpellVfx.StatusAura(
                Entity!, e.EffectId, shape, effect.School, SwirlHeight, HeadHeight, visual, out bool replaces) is { } rig)
        {
            _auras[e.EffectId] = rig;
            if (replaces)
            {
                _concealed.Add(e.EffectId);
                visual.Visible = false;
                if (visual is GpuParticles3D particles)
                {
                    particles.Emitting = false;
                }
            }
        }
    }

    /// <summary>Draws what the local player wears while the view is outside their head, and takes
    /// it down while it is inside.</summary>
    private void RefreshWorn()
    {
        if (Entity is not { } bearer || SpellVfx.StatusAuraHidden(bearer))
        {
            foreach (VfxRig rig in _auras.Values)
            {
                rig.Stop();
            }

            _auras.Clear();
            return;
        }

        foreach (KeyValuePair<string, (StatusVfxShape Shape, DamageType School)> worn in _worn)
        {
            if (_auras.TryGetValue(worn.Key, out VfxRig? live) && live.AnyLive)
            {
                continue;
            }

            if (SpellVfx.StatusAura(
                    bearer, worn.Key, worn.Value.Shape, worn.Value.School, SwirlHeight, HeadHeight, null, out _) is { } rig)
            {
                _auras[worn.Key] = rig;
            }
            else
            {
                _auras.Remove(worn.Key);
            }
        }
    }

    private void OnRemoved(StatusEffectRemovedEvent e)
    {
        if (!ReferenceEquals(e.Target, Entity))
        {
            return;
        }

        _worn.Remove(e.EffectId);
        _concealed.Remove(e.EffectId);
        if (_auras.Remove(e.EffectId, out VfxRig? aura))
        {
            aura.Stop();
        }

        if (!_active.Remove(e.EffectId, out Node3D? swirl) || !IsInstanceValid(swirl))
        {
            return;
        }

        // This handler is reached during teardown as well as during play: StatusEffectsComponent's
        // OnTeardown calls ClearAll, which publishes a removal for every live effect. By then the
        // swirl may already have left the tree with the body, and GetTree() returns null there — so
        // the fade timer was a null dereference on the ordinary path of a cell unload or a death
        // despawn. Nothing to fade into once the world is going away; just drop it.
        if (!swirl.IsInsideTree())
        {
            swirl.QueueFree();
            return;
        }

        // A shaped mark has nothing to fade: it simply ends with the status.
        if (swirl is not GpuParticles3D particles)
        {
            swirl.QueueFree();
            return;
        }

        // Stop emitting and let the last particles fade before freeing.
        particles.Emitting = false;
        swirl.GetTree().CreateTimer(1.2).Timeout += () =>
        {
            if (IsInstanceValid(swirl))
            {
                swirl.QueueFree();
            }
        };
    }

    /// <summary>A small orbiting-ember swirl tinted to the school colour, built in code so no scene
    /// asset is needed (real particle art is a Phase 53 refinement).</summary>
    private static GpuParticles3D BuildSwirl(Color tint)
    {
        var material = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Ring,
            EmissionRingRadius = 0.38f,
            EmissionRingInnerRadius = 0.30f,
            EmissionRingHeight = 0.1f,
            EmissionRingAxis = Vector3.Up,
            Direction = new Vector3(0f, 1f, 0f),
            Spread = 12f,
            InitialVelocityMin = 0.25f,
            InitialVelocityMax = 0.5f,
            Gravity = Vector3.Zero,
            ScaleMin = 0.5f,
            ScaleMax = 1.0f,
        };

        var mesh = new QuadMesh { Size = new Vector2(0.09f, 0.09f) };
        mesh.Material = new StandardMaterial3D
        {
            AlbedoColor = new Color(tint.R, tint.G, tint.B, 0.85f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
            EmissionEnabled = true,
            Emission = tint,
            EmissionEnergyMultiplier = 1.6f,
        };

        return new GpuParticles3D
        {
            Name = "StatusVfx",
            ProcessMaterial = material,
            DrawPass1 = mesh,
            Amount = 10,
            Lifetime = 1.1,
            Emitting = true,
        };
    }

    // --- shaped marks (magic upgrade): primitives only, tinted by the school colour ---

    private Node3D BuildShape(StatusVfxShape shape, Color tint)
    {
        // Reduced Motion: the marks that orbit hold still instead.
        float spin = UiTheme.MotionEnabled ? 70f : 0f;
        var root = new StatusVfxSpin { Name = "StatusVfx" + shape };
        switch (shape)
        {
            case StatusVfxShape.MarkRing:
                root.DegreesPerSecond = spin;
                root.AddChild(Mesh(
                    new TorusMesh { InnerRadius = 0.26f, OuterRadius = 0.32f, Rings = 24, RingSegments = 8 },
                    Glow(tint, 0.9f),
                    new Vector3(0f, HeadHeight, 0f)));
                break;
            case StatusVfxShape.Thorns:
                const int Thorns = 7;
                for (int i = 0; i < Thorns; i++)
                {
                    float angle = Mathf.Tau * i / Thorns;
                    MeshInstance3D thorn = Mesh(
                        new CylinderMesh { TopRadius = 0f, BottomRadius = 0.06f, Height = 0.55f, RadialSegments = 6 },
                        Glow(tint.Darkened(0.2f), 1f),
                        new Vector3(Mathf.Cos(angle) * 0.45f, 0.26f, Mathf.Sin(angle) * 0.45f));
                    thorn.Rotation = new Vector3(Mathf.Sin(angle) * 0.35f, 0f, -Mathf.Cos(angle) * 0.35f);
                    root.AddChild(thorn);
                }

                break;
            case StatusVfxShape.BrokenGlyph:
                const int Fragments = 5;
                for (int i = 0; i < Fragments; i++)
                {
                    // Five of six arc slots: the missing one is the break.
                    float angle = Mathf.Tau * i / (Fragments + 1);
                    MeshInstance3D bar = Mesh(
                        new BoxMesh { Size = new Vector3(0.22f, 0.025f, 0.05f) },
                        Glow(tint, 0.95f),
                        new Vector3(Mathf.Cos(angle) * 0.3f, HeadHeight, Mathf.Sin(angle) * 0.3f));
                    bar.Rotation = new Vector3(0f, -angle - (Mathf.Pi / 2f), 0f);
                    root.AddChild(bar);
                }

                MeshInstance3D slash = Mesh(
                    new BoxMesh { Size = new Vector3(0.7f, 0.025f, 0.05f) },
                    Glow(tint, 0.95f),
                    new Vector3(0f, HeadHeight, 0f));
                slash.Rotation = new Vector3(0f, Mathf.Pi / 4f, 0f);
                root.AddChild(slash);
                break;
            case StatusVfxShape.Stars:
                root.DegreesPerSecond = spin * 2f;
                for (int i = 0; i < 3; i++)
                {
                    float angle = Mathf.Tau * i / 3f;
                    root.AddChild(Mesh(
                        new SphereMesh { Radius = 0.06f, Height = 0.12f, RadialSegments = 8, Rings = 4 },
                        Glow(tint, 1f),
                        new Vector3(Mathf.Cos(angle) * 0.32f, HeadHeight, Mathf.Sin(angle) * 0.32f)));
                }

                break;
            case StatusVfxShape.IceShell:
                root.AddChild(Mesh(
                    new CapsuleMesh { Radius = 0.6f, Height = 1.95f, RadialSegments = 12, Rings = 4 },
                    Glow(tint, 0.4f),
                    new Vector3(0f, 0.98f, 0f)));
                break;
            case StatusVfxShape.WardShell:
                root.AddChild(Mesh(
                    new SphereMesh { Radius = 0.95f, Height = 1.9f, RadialSegments = 16, Rings = 8 },
                    Glow(tint, 0.22f),
                    new Vector3(0f, 1f, 0f)));
                break;
        }

        return root;
    }

    private static MeshInstance3D Mesh(Mesh mesh, Material material, Vector3 position) => new()
    {
        Mesh = mesh,
        MaterialOverride = material,
        Position = position,
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
    };

    private static StandardMaterial3D Glow(Color tint, float alpha) => new()
    {
        AlbedoColor = new Color(tint.R, tint.G, tint.B, alpha),
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        EmissionEnabled = true,
        Emission = tint,
        EmissionEnergyMultiplier = 1.4f,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };
}
