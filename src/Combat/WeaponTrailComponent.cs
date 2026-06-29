using Embervale.Core.Events;
using Embervale.Entities;
using Godot;

namespace Embervale.Combat;

/// <summary>
/// A brief slash trail on a melee swing (Phase 29C). Holds one translucent "slash" quad in front of the
/// body and flashes it on <see cref="AttackPerformedEvent"/>, fading its alpha to zero over a short window.
/// A single per-attacker node toggled by alpha — no churn, so no pool. A ranged swing (the bow) shows no
/// slash; it publishes a bow-release sound cue instead. Visual-only.
/// </summary>
[GlobalClass]
public partial class WeaponTrailComponent : EntityComponent
{
    /// <summary>Seconds for the slash to fade out.</summary>
    [Export] public float FadeSeconds { get; set; } = 0.18f;

    private MeleeWeaponComponent? _weapon;
    private MeshInstance3D _slash = null!;
    private StandardMaterial3D _material = null!;
    private float _alpha;

    private static readonly Color SlashColor = new(0.9f, 0.95f, 1.0f);

    protected override void OnInitialize()
    {
        _weapon = Entity!.GetComponent<MeleeWeaponComponent>();

        _material = new StandardMaterial3D
        {
            AlbedoColor = new Color(SlashColor.R, SlashColor.G, SlashColor.B, 0f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            EmissionEnabled = true,
            Emission = SlashColor,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        _slash = new MeshInstance3D
        {
            // A flat crescent-ish quad swept in front of the body, where the melee hitbox lands.
            Mesh = new QuadMesh { Size = new Vector2(1.4f, 1.2f) },
            MaterialOverride = _material,
            Position = new Vector3(0f, 1.0f, -1.1f),
            RotationDegrees = new Vector3(90f, 0f, 0f),
            Visible = false,
        };
        // Deferred: the body is still setting up its children during this component's _Ready, so a
        // direct AddChild fails ("parent busy setting up children") and would orphan the slash.
        Entity!.Body.CallDeferred(Node.MethodName.AddChild, _slash);

        EventBus.Instance?.Subscribe<AttackPerformedEvent>(OnAttack);
    }

    protected override void OnTeardown() => EventBus.Instance?.Unsubscribe<AttackPerformedEvent>(OnAttack);

    private void OnAttack(AttackPerformedEvent e)
    {
        if (!ReferenceEquals(e.Attacker, Entity))
        {
            return;
        }

        Vector3 pos = Entity!.Body.GlobalPosition;

        // A bow release isn't a slash — skip the trail, fire its own cue.
        if (_weapon?.Weapon is { Ranged: true })
        {
            EventBus.Instance?.Publish(new SoundCueRequestedEvent("sfx.combat.bow", pos));
            return;
        }

        _alpha = 1f;
        _slash.Visible = true;
        EventBus.Instance?.Publish(new SoundCueRequestedEvent("sfx.combat.swing", pos));
    }

    public override void _Process(double delta)
    {
        if (_alpha <= 0f)
        {
            return;
        }

        _alpha -= FadeSeconds > 0f ? (float)delta / FadeSeconds : 1f;
        if (_alpha <= 0f)
        {
            _alpha = 0f;
            _slash.Visible = false;
        }

        _material.AlbedoColor = new Color(SlashColor.R, SlashColor.G, SlashColor.B, _alpha * 0.6f);
    }
}
