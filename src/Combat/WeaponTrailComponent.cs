using System.Collections.Generic;
using Embervale.Combat.Actions;
using Embervale.Core.Events;
using Embervale.Entities;
using Godot;

namespace Embervale.Combat;

/// <summary>
/// The slash trail of a melee swing (Phase 29C, rebuilt). The streak appears at the instant the blow
/// goes live (<see cref="ActionReleasedEvent"/>), not when the wind-up starts, and its leading edge
/// <em>travels</em> through the swing the way the blade does: a light chain alternates left-to-right and
/// right-to-left on a slant, a heavy swing is a vertical chop. Its length of life comes from the
/// action's own authored <c>TrailFrom</c>/<c>TrailTo</c> (<see cref="TrailStyles"/>), so a slow maul
/// leaves a long streak and a quick dagger a short one. A hostile attacker's trail is warm red-white,
/// so an incoming arc reads as different from the player's own.
///
/// <para>The swing sound lives here too, at the release frame, with the action's own
/// <c>SwingCueId</c>; a hostile wind-up also makes a low, quiet gather sound that the player can hear
/// coming. One pivot and one mesh per attacker, toggled by alpha — no churn, so no pool.
/// Visual-only.</para>
/// </summary>
[GlobalClass]
public partial class WeaponTrailComponent : EntityComponent
{
    private static readonly Dictionary<int, ArrayMesh> StreakCache = new();

    /// <summary>Seconds for the slash to fade out when no action gives a better answer.</summary>
    [Export] public float FadeSeconds { get; set; } = 0.18f;

    private Node3D _pivot = null!;
    private MeshInstance3D _streak = null!;
    private StandardMaterial3D _material = null!;
    private TrailStyle _style;
    private float _age;
    private float _life;
    private float _windup;
    private int _combo;
    private bool _live;

    protected override void OnInitialize()
    {
        _material = new StandardMaterial3D
        {
            AlbedoColor = new Color(1f, 1f, 1f, 0f),
            VertexColorUseAsAlbedo = true,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        _streak = new MeshInstance3D
        {
            MaterialOverride = _material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        _pivot = new Node3D { Name = "WeaponTrail", Position = new Vector3(0f, 1.15f, 0f), Visible = false };
        _pivot.AddChild(_streak);

        // Deferred: the body is still setting up its children during this component's _Ready, so a
        // direct AddChild fails ("parent busy setting up children") and would orphan the trail.
        Entity!.Body.CallDeferred(Node.MethodName.AddChild, _pivot);

        EventBus.Instance?.Subscribe<AttackPerformedEvent>(OnAttack);
        EventBus.Instance?.Subscribe<ActionReleasedEvent>(OnReleased);
        EventBus.Instance?.Subscribe<AttackInterruptedEvent>(OnInterrupted);
    }

    protected override void OnTeardown()
    {
        EventBus.Instance?.Unsubscribe<AttackPerformedEvent>(OnAttack);
        EventBus.Instance?.Unsubscribe<ActionReleasedEvent>(OnReleased);
        EventBus.Instance?.Unsubscribe<AttackInterruptedEvent>(OnInterrupted);
    }

    /// <summary>Hostile actors only: the low gather sound of a swing being wound up, quiet enough not
    /// to mask anything and there for the player who is listening.</summary>
    private void OnAttack(AttackPerformedEvent e)
    {
        if (!ReferenceEquals(e.Attacker, Entity))
        {
            return;
        }

        _windup = e.WindupSeconds;
        _combo = e.ComboIndex;
        if (IsHostile())
        {
            EventBus.Instance?.Publish(new SoundCueRequestedEvent(
                CombatFx.SwingCue, Entity!.Body.GlobalPosition, -8f, 0.7f));
        }
    }

    private void OnInterrupted(AttackInterruptedEvent e)
    {
        if (ReferenceEquals(e.Attacker, Entity))
        {
            _live = false;
            _pivot.Visible = false;
        }
    }

    private void OnReleased(ActionReleasedEvent e)
    {
        if (!ReferenceEquals(e.Actor, Entity) || e.Kind is not (ActionKind.Attack or ActionKind.HeavyAttack))
        {
            return;
        }

        ActionDefinitionResource? action = Entity!.GetComponent<CharacterActionComponent>()?.Current;
        _style = TrailStyles.For(
            e.Kind, _combo, IsHostile(), _windup, action?.ActiveFrom ?? 0.34f,
            action?.TrailFrom ?? 0.3f, action?.TrailTo ?? 0.6f);
        _life = _windup > 0f ? _style.FadeSeconds : FadeSeconds;
        _age = 0f;
        _live = true;

        int streak = (int)_style.StreakDegrees;
        _streak.Mesh = Streak(streak);
        _streak.Scale = new Vector3(_style.Direction * (_style.Vertical ? -1 : 1) * _style.Radius, 1f, _style.Radius);
        _pivot.RotationDegrees = new Vector3(0f, 0f, _style.Vertical ? 90f : _style.TiltDegrees);
        Apply(0f);
        _pivot.Visible = true;

        Vector3 pos = Entity.Body.GlobalPosition;
        float pitch = e.Kind == ActionKind.HeavyAttack ? 0.85f : 1f;
        EventBus.Instance?.Publish(new SoundCueRequestedEvent(
            action?.SwingCueId ?? CombatFx.SwingCue, pos, e.Kind == ActionKind.HeavyAttack ? 2f : 0f, pitch));
    }

    private bool IsHostile() => Entity?.GetComponent<CombatComponent>() is { Team: not 0 };

    /// <summary>A band of an annulus streaking behind a leading edge at angle 0, fading to nothing at
    /// its trailing end (vertex alpha). Unit radius; the component scales it. Cached per length.</summary>
    private static ArrayMesh Streak(int degrees)
    {
        int key = Mathf.Clamp(degrees, 15, 180);
        if (StreakCache.TryGetValue(key, out ArrayMesh? cached) && GodotObject.IsInstanceValid(cached))
        {
            return cached;
        }

        const float Inner = 0.72f;
        int segments = Mathf.Max(4, key / 10);
        float span = Mathf.DegToRad(key);
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        st.SetNormal(Vector3.Up);
        for (int i = 0; i < segments; i++)
        {
            // Angle runs from the trailing end (-span) up to the leading edge (0).
            float a0 = -span + (span * i / segments);
            float a1 = -span + (span * (i + 1) / segments);
            float f0 = (float)i / segments;
            float f1 = (float)(i + 1) / segments;
            Vector3 i0 = new(Mathf.Sin(a0) * Inner, 0f, -Mathf.Cos(a0) * Inner);
            Vector3 i1 = new(Mathf.Sin(a1) * Inner, 0f, -Mathf.Cos(a1) * Inner);
            Vector3 o0 = new(Mathf.Sin(a0), 0f, -Mathf.Cos(a0));
            Vector3 o1 = new(Mathf.Sin(a1), 0f, -Mathf.Cos(a1));
            Color c0 = new(1f, 1f, 1f, f0 * f0);
            Color c1 = new(1f, 1f, 1f, f1 * f1);
            st.SetColor(c0);
            st.AddVertex(i0);
            st.SetColor(c0);
            st.AddVertex(o0);
            st.SetColor(c1);
            st.AddVertex(o1);
            st.SetColor(c0);
            st.AddVertex(i0);
            st.SetColor(c1);
            st.AddVertex(o1);
            st.SetColor(c1);
            st.AddVertex(i1);
        }

        ArrayMesh mesh = st.Commit();
        StreakCache[key] = mesh;
        return mesh;
    }

    private void Apply(float t)
    {
        // The leading edge travels through the swing. Rotation about the pivot's up axis by psi turns
        // an angle a into a - psi, so psi = -direction x lead.
        float direction = _style.Vertical ? -1f : _style.Direction;
        float lead = direction * TrailStyles.LeadDegrees(_style, t);
        _streak.RotationDegrees = new Vector3(0f, -lead, 0f);

        // The comfort slider holds the brightness of the trail down under Reduced Motion.
        float comfort = Mathf.Lerp(0.4f, 1f, LiveComfort.Get().ScreenFlash);
        _material.AlbedoColor = new Color(
            _style.R, _style.G, _style.B, _style.Alpha * TrailStyles.Fade(t) * comfort);
    }

    public override void _Process(double delta)
    {
        if (!_live)
        {
            return;
        }

        _age += (float)delta;
        float t = _life > 0f ? _age / _life : 1f;
        if (t >= 1f)
        {
            _live = false;
            _pivot.Visible = false;
            return;
        }

        Apply(t);
    }
}
