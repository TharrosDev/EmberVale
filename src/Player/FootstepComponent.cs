using System;
using Embervale.Core.Events;
using Embervale.Entities;
using Embervale.Movement;
using Embervale.World;
using Godot;

namespace Embervale.Player;

/// <summary>
/// Footstep, jump and landing sounds for anything that walks (Phase 31E; reworked in the 2026-09
/// footstep upgrade). Emits through <see cref="SoundCueRequestedEvent"/>, so every sound is a
/// positional cue on the SFX bus like any other world sound and the <c>AudioDirector</c>'s 3D players
/// attenuate it with distance.
///
/// <list type="bullet">
/// <item><b>When.</b> On the animation's own foot contact where the rig has feet — the moment the
/// lower foot changes — and every <see cref="StrideDistance"/> metres where it has none.
/// <see cref="FootstepGait"/> owns that rule, and it alternates left and right by construction.</item>
/// <item><b>Where.</b> At the striking foot, so in first person the left boot is heard on the left.</item>
/// <item><b>What.</b> Declared water the feet are in, then the floor collider's <c>surface</c> tag,
/// then the generated terrain's biome and slope, then the collider's name — see
/// <see cref="Surfaces"/>. Untagged ground used to mean stone everywhere.</item>
/// <item><b>How loud.</b> Scaled by gait between <see cref="WalkSpeed"/> and
/// <see cref="SprintSpeed"/>, detuned across <see cref="PitchVariants"/> with the same variant never
/// twice running (<see cref="FootstepAudio"/>).</item>
/// <item><b>Jumps and landings.</b> A push-off when the body leaves the ground going up, and a
/// landing whose volume and depth follow how fast it was falling.</item>
/// </list>
///
/// <para>On the player it is unconditional. On NPCs and enemies (<see cref="MaxAudibleDistance"/> set
/// by their factories) it does nothing at all beyond that range from the camera — no bone reads, no
/// raycast — so a loaded region of actors costs a distance check each.</para>
/// </summary>
public partial class FootstepComponent : EntityComponent
{
    /// <summary>Metres between footfalls when the rig has no feet to read (cadence = speed / stride).
    /// With feet, a stretch 1.25× this long without an animated contact still produces one.</summary>
    [Export] public float StrideDistance { get; set; } = 2.0f;

    /// <summary>Below this horizontal speed the body counts as standing still (no steps).</summary>
    [Export] public float MinSpeed { get; set; } = 0.6f;

    /// <summary>At or below this speed a footfall is at its quietest…</summary>
    [Export] public float WalkSpeed { get; set; } = 2.5f;

    /// <summary>…and at or above this one, its loudest.</summary>
    [Export] public float SprintSpeed { get; set; } = 8f;

    /// <summary>Footfall volume at a walk, in dB relative to the recording.</summary>
    [Export] public float QuietDb { get; set; } = -8f;

    /// <summary>Footfall volume at a sprint.</summary>
    [Export] public float LoudDb { get; set; } = 1f;

    /// <summary>How many detunes a footfall is spread across; the same one never plays twice running.</summary>
    [Export] public int PitchVariants { get; set; } = 5;

    /// <summary>Largest detune either side of the recording's pitch.</summary>
    [Export] public float PitchJitter { get; set; } = 0.06f;

    /// <summary>Extra pitch at a full sprint — a hurried step is shorter and brighter.</summary>
    [Export] public float SprintPitchRise { get; set; } = 0.06f;

    /// <summary>Upward speed on leaving the ground that counts as a jump (a push-off sound).</summary>
    [Export] public float JumpSpeed { get; set; } = 2f;

    /// <summary>Volume of the jump push-off.</summary>
    [Export] public float JumpVolumeDb { get; set; } = -3f;

    /// <summary>Downward speed below which a landing is just the next footfall (a kerb, a stair).</summary>
    [Export] public float LandingMinFall { get; set; } = 2.5f;

    /// <summary>Downward speed at which a landing is as loud and as low as it gets.</summary>
    [Export] public float LandingMaxFall { get; set; } = 12f;

    /// <summary>Volume of the lightest landing that makes a sound…</summary>
    [Export] public float LandingQuietDb { get; set; } = -4f;

    /// <summary>…and of the heaviest.</summary>
    [Export] public float LandingLoudDb { get; set; } = 5f;

    /// <summary>How far the heaviest landing's pitch drops below the recording.</summary>
    [Export] public float LandingPitchDrop { get; set; } = 0.2f;

    /// <summary>Water at least this deep over the feet turns every footfall into a wade.</summary>
    [Export] public float WadeDepth { get; set; } = 0.08f;

    /// <summary>Added to every sound this body makes. NPC factories pass a negative value so the
    /// player's own steps stay the ones in front.</summary>
    [Export] public float VolumeOffsetDb { get; set; }

    /// <summary>Beyond this distance from the camera the component does nothing. 0 = always on (the
    /// player).</summary>
    [Export] public float MaxAudibleDistance { get; set; }

    /// <summary>Terrain steeper than this (rise over run) is heard as bare rock.</summary>
    [Export] public float RockSlope { get; set; } = 1f;

    private static readonly StringName TerrainColliderName = "TerrainCollider";

    private readonly FootstepGait _gait = new();
    private readonly Random _random = new();
    private CharacterBody3D? _body;
    private MountComponent? _mount;
    private Skeleton3D? _skeleton;
    private int _leftFoot = -1;
    private int _rightFoot = -1;
    private float _rigRetry;
    private bool _wasGrounded = true;
    private float _fallSpeed;
    private int _variant = -1;

    protected override void OnInitialize()
    {
        _gait.Stride = StrideDistance;
        _body = Entity?.Body as CharacterBody3D;
        _mount = Entity?.GetComponent<MountComponent>();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_body == null)
        {
            return;
        }

        // 39B: a rider's boots are not on the ground. Silence rather than a hoof cue because there
        // is no hoof cue — nothing in the step set is a horse. Adding one is an audio job of the
        // shape Phase 31E did, not a line in this guard. Treated as grounded so a dismount does not
        // sound like a landing.
        if (_mount is { IsMounted: true } || !Audible())
        {
            _gait.Reset();
            _wasGrounded = true;
            _fallSpeed = 0f;
            return;
        }

        Vector3 velocity = _body.Velocity;
        if (!_body.IsOnFloor())
        {
            if (_wasGrounded && velocity.Y >= JumpSpeed)
            {
                Emit(ResolveSurfaceCue(_body.GlobalPosition), _body.GlobalPosition, JumpVolumeDb,
                    NextPitch(0f));
            }

            _wasGrounded = false;
            _fallSpeed = Mathf.Max(_fallSpeed, -velocity.Y);
            _gait.Reset();
            return;
        }

        if (!_wasGrounded)
        {
            _wasGrounded = true;
            float? weight = FootstepAudio.LandingWeight(_fallSpeed, LandingMinFall, LandingMaxFall);
            _fallSpeed = 0f;
            if (weight is { } heavy)
            {
                Emit(ResolveSurfaceCue(_body.GlobalPosition), _body.GlobalPosition,
                    FootstepAudio.VolumeDb(heavy, LandingQuietDb, LandingLoudDb),
                    FootstepAudio.LandingPitch(heavy, LandingPitchDrop));
                _gait.Reset();
                return;
            }
        }

        float speed = new Vector2(velocity.X, velocity.Z).Length();
        if (speed < MinSpeed)
        {
            _gait.Reset();
            return;
        }

        bool feet = ResolveRig();
        _gait.FallbackDistance = feet ? StrideDistance * 1.25f : StrideDistance;
        Footfall footfall = _gait.Step(speed * (float)delta,
            feet ? FootWorld(_leftFoot).Y : null,
            feet ? FootWorld(_rightFoot).Y : null);
        if (footfall == Footfall.None)
        {
            return;
        }

        Vector3 at = StrikePoint(footfall, feet);
        float intensity = FootstepAudio.Intensity(speed, WalkSpeed, SprintSpeed);
        Emit(ResolveSurfaceCue(at), at, FootstepAudio.VolumeDb(intensity, QuietDb, LoudDb),
            NextPitch(intensity));
    }

    private void Emit(string cue, Vector3 at, float volumeDb, float pitch) =>
        EventBus.Instance?.Publish(new SoundCueRequestedEvent(cue, at, volumeDb + VolumeOffsetDb, pitch));

    private float NextPitch(float intensity)
    {
        _variant = FootstepAudio.NextVariant(_variant, PitchVariants, _random.NextDouble());
        return FootstepAudio.Pitch(_variant, PitchVariants, PitchJitter, intensity, SprintPitchRise);
    }

    private bool Audible()
    {
        if (MaxAudibleDistance <= 0f)
        {
            return true;
        }

        return _body!.GetViewport()?.GetCamera3D() is { } camera &&
               camera.GlobalPosition.DistanceSquaredTo(_body.GlobalPosition) <=
               MaxAudibleDistance * MaxAudibleDistance;
    }

    /// <summary>Finds the rig's feet, retrying about once a second: a body's model can arrive after
    /// the component initialises, and a quadruped simply never resolves and uses the stride.</summary>
    private bool ResolveRig()
    {
        if (_skeleton != null && GodotObject.IsInstanceValid(_skeleton))
        {
            return _leftFoot >= 0 && _rightFoot >= 0;
        }

        _skeleton = null;
        _rigRetry -= (float)GetPhysicsProcessDeltaTime();
        if (_rigRetry > 0f)
        {
            return false;
        }

        _rigRetry = 1f;
        _skeleton = Embervale.Animation.FootIkComponent.FindRig(_body!);
        _leftFoot = _skeleton?.FindBone("LeftFoot") ?? -1;
        _rightFoot = _skeleton?.FindBone("RightFoot") ?? -1;
        return _leftFoot >= 0 && _rightFoot >= 0;
    }

    private Vector3 FootWorld(int bone) =>
        _skeleton!.GlobalTransform * _skeleton.GetBoneGlobalPose(bone).Origin;

    /// <summary>Where the striking foot meets the floor: the foot bone over the body's floor height,
    /// or a boot-width either side of the body when there are no feet to read.</summary>
    private Vector3 StrikePoint(Footfall footfall, bool feet)
    {
        Vector3 origin = _body!.GlobalPosition;
        if (feet)
        {
            Vector3 foot = FootWorld(footfall == Footfall.Left ? _leftFoot : _rightFoot);
            return new Vector3(foot.X, origin.Y, foot.Z);
        }

        float side = footfall == Footfall.Left ? -0.12f : 0.12f;
        return origin + (_body.GlobalBasis.X.Normalized() * side);
    }

    /// <summary>The cue for the ground at <paramref name="at"/>; the order is <see cref="Surfaces"/>'.</summary>
    private string ResolveSurfaceCue(Vector3 at)
    {
        float? water = WorldWater.SurfaceAt(at.X, at.Z, WorldWater.Bodies, WorldGround.Field);
        if (FootstepAudio.IsWading(water, _body!.GlobalPosition.Y, WadeDepth))
        {
            return Surfaces.WaterCue;
        }

        PhysicsDirectSpaceState3D? space = _body.GetWorld3D()?.DirectSpaceState;
        if (space == null)
        {
            return Surfaces.DefaultCue;
        }

        PhysicsRayQueryParameters3D query = PhysicsRayQueryParameters3D.Create(
            at + (Vector3.Up * 0.3f), at + (Vector3.Down * 0.6f));
        query.Exclude = new Godot.Collections.Array<Rid> { _body.GetRid() };

        Godot.Collections.Dictionary hit = space.IntersectRay(query);
        if (hit.Count == 0 || hit["collider"].As<Node>() is not { } collider)
        {
            return Surfaces.DefaultCue;
        }

        if (collider.HasMeta("surface"))
        {
            return Surfaces.CueFromTag(collider.GetMeta("surface").AsString());
        }

        if (collider.Name == TerrainColliderName && WorldGround.Field is { } field)
        {
            WorldSample sample = field.Sample(at.X, at.Z);
            return Surfaces.CueId(Surfaces.FromTerrain(
                sample.AlpineWeight, sample.BarrenWeight, sample.Slope, RockSlope));
        }

        string? tag = null;
        if (collider is CollisionObject3D body && hit.ContainsKey("shape"))
        {
            uint owner = body.ShapeFindOwner((int)hit["shape"]);
            tag = Surfaces.TagFromName((body.ShapeOwnerGetOwner(owner) as Node)?.Name.ToString());
        }

        tag ??= Surfaces.TagFromName(collider.Name.ToString()) ??
              Surfaces.TagFromName(collider.GetParent()?.Name.ToString());
        return tag == null ? Surfaces.DefaultCue : Surfaces.CueFromTag(tag);
    }
}
