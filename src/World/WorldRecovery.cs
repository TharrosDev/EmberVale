using Embervale.Combat;
using Embervale.Core;
using Embervale.Core.Diagnostics;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Player;
using Godot;

namespace Embervale.World;

/// <summary>
/// The realm's standing promise that no piece of ground is a dead end: a player who ends up
/// somewhere they cannot walk out of is put back on recent dry, walkable ground they stood on.
///
/// ⚠️ <b>THIS EXISTS INSTEAD OF A SWIMMING SYSTEM AND INSTEAD OF INVISIBLE WALLS.</b> The 2026-08-29
/// overhaul gave the world real basins, precipices and crevasses — Hollowreach's open water is 4.5 m
/// deep behind a 53-degree drop-off, the Ancient Aerie's north face falls thirty metres onto a
/// trench floor, and Glacier Pass has two crevasses cut at a 3:1 falloff that are ten metres wide
/// and eight deep. Every one of those is deliberate and every one of them is a hole. Three answers
/// were available. A swimming system is a whole feature and this pass is not the place for it. A
/// ring of colliders round each is the artefact the overhaul was fought to remove: the land is
/// supposed to say "not that way", and it does. So the land keeps saying it, and this catches the
/// player when something else — a slide, a knockback, a dismount, a dragon, a jump they should not
/// have made — puts them somewhere the land cannot let them out of.
///
/// ⚠️ <b>IT RECOVERS, IT DOES NOT KILL.</b> Drowning or attrition damage would be a second, worse
/// trap: a player who can neither escape nor survive loses progress to a mistake the world never
/// warned them about.
///
/// <b>Three triggers, one response.</b>
/// <list type="number">
/// <item><b>Deep water</b> — over <see cref="WorldWater.DrownDepth"/> at the feet for
/// <see cref="WaterGrace"/> seconds. Immediate, because there is no version of standing in four
/// metres of water that the player meant.</item>
/// <item><b>A pit with no way out</b> — well below the surrounding ground, not making progress, and
/// a local walkability search finds no escape. Slower and much more cautious, because a canyon floor
/// with a road along it looks identical to a trap until you check whether it goes anywhere.</item>
/// <item><b>Out of the world</b> — the feet more than <see cref="FallThroughDepth"/> under the
/// terrain with no floor beneath them, or below <see cref="KillFloorY"/>. A seam gap, a collider
/// that streamed out under the player, a physics tunnel: the player is falling forever and every
/// frame spent waiting is a frame of void.</item>
/// </list>
///
/// <b>The response.</b> A short fade to black (<see cref="FadeOutSeconds"/>), the move at the peak,
/// a fade back (<see cref="FadeInSeconds"/>). The destination is the newest point of a
/// <see cref="SafeGroundTrail"/> that is still safe <i>now</i> — dry, gentle, not in a hole, and a
/// capsule fits there according to <see cref="SafePlacementService"/> — preferring one at least
/// <see cref="MinHazardDistance"/> back from the hazard so the player is not set down on the lip they
/// just slid off. The player faces away from the hazard, and a toast says what happened.
///
/// ⚠️ <b>THE PIT CHECK IS LOCAL AND LAZY ON PURPOSE.</b> The obvious design was to precompute the
/// region's trap patches with <see cref="WorldTraversalAnalysis"/> at load and test membership. That
/// is sixteen thousand field samples on every region entry to answer a question that is almost never
/// asked, and it would go stale the moment anything moved. A forty-metre flood fill costs four
/// hundred samples and only runs after the player has already spent <see cref="PitGrace"/> seconds
/// failing to get out of a hole — which, in a normal session, is never.
///
/// Owns a <see cref="WorldWading"/> child: the half of the water contract that keeps the player out
/// of deep water in the first place.
/// </summary>
public sealed partial class WorldRecovery : Node
{
    /// <summary>Seconds over <see cref="WorldWater.DrownDepth"/> before recovery. Long enough that
    /// being knocked into a deep channel and scrambling back out is not interrupted mid-stride.</summary>
    [Export(PropertyHint.Range, "0,10,0.1")] public float WaterGrace { get; set; } = 2.5f;

    /// <summary>Seconds stuck in a hole before the escape search runs at all.</summary>
    [Export(PropertyHint.Range, "1,60,0.5")] public float PitGrace { get; set; } = 9f;

    /// <summary>How far below the surrounding ground counts as "in a hole".</summary>
    [Export(PropertyHint.Range, "1,20,0.5")] public float PitDepth { get; set; } = 4f;

    /// <summary>Radius the surrounding ground is measured over, and the escape search's reach.</summary>
    [Export(PropertyHint.Range, "6,60,1")] public float PitReach { get; set; } = 22f;

    /// <summary>Moving further than this resets the pit timer — the player is getting somewhere.</summary>
    [Export(PropertyHint.Range, "1,30,0.5")] public float ProgressDistance { get; set; } = 7f;

    /// <summary>How often safe ground is sampled into the trail while the player stands on it.</summary>
    [Export(PropertyHint.Range, "0.05,2,0.05")] public float SampleSeconds { get; set; } = 0.35f;

    /// <summary>Steepest ground a recovery point may sit on. A safe point on a 40-degree bank puts
    /// the player straight back down the slope they were recovered from.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float MaxSafeSlope { get; set; } = 0.45f;

    /// <summary>Points kept in the trail.</summary>
    [Export(PropertyHint.Range, "1,32,1")] public int TrailLength { get; set; } = 8;

    /// <summary>Minimum horizontal spacing between trail points.</summary>
    [Export(PropertyHint.Range, "0.5,20,0.5")] public float TrailSpacing { get; set; } = 4f;

    /// <summary>A trail point closer than this to the hazard is used only if nothing further back
    /// qualifies.</summary>
    [Export(PropertyHint.Range, "0,20,0.5")] public float MinHazardDistance { get; set; } = 3f;

    /// <summary>Trail points further than this from the hazard are ignored — they were left behind
    /// by a teleport, not a walk (see <see cref="SafeGroundTrail.Pick"/>).</summary>
    [Export(PropertyHint.Range, "10,500,5")] public float MaxRecoveryDistance { get; set; } = 120f;

    /// <summary>How far under the terrain, with no floor below, counts as fallen out of the world.</summary>
    [Export(PropertyHint.Range, "1,100,0.5")] public float FallThroughDepth { get; set; } = 8f;

    /// <summary>Absolute world Y below which the player has left the world whatever is under them.</summary>
    [Export(PropertyHint.Range, "-5000,0,10")] public float KillFloorY { get; set; } = -600f;

    /// <summary>How far down the floor probe looks before declaring a void.</summary>
    [Export(PropertyHint.Range, "5,500,5")] public float FloorProbeDepth { get; set; } = 60f;

    [Export(PropertyHint.Range, "0,2,0.05")] public float FadeOutSeconds { get; set; } = 0.25f;
    [Export(PropertyHint.Range, "0,3,0.05")] public float FadeInSeconds { get; set; } = 0.5f;

    /// <summary>A player this far from the hazard when the fade peaks was moved by something else.</summary>
    private const float StaleHazardDistance = 25f;

    /// <summary>Locale key of the toast shown after a recovery.</summary>
    public const string RecoveredKey = "world.recovered";

    private SafeGroundTrail _trail = new();
    private Vector3 _pitAnchor;
    private float _submerged;
    private float _stuck;
    private float _sampleTimer;
    private readonly CanvasLayer _fadeLayer;
    private readonly ColorRect _fade;
    private float _fadeElapsed = -1f;
    private Vector3 _pendingHazard;
    private string _pendingReason = string.Empty;
    private bool _moved;

    /// <summary>The newest remembered safe ground, or null before any has been seen. The public read
    /// for anything that needs "which way is out" — <see cref="WorldWading"/>'s push-back uses it
    /// over a flat-bottomed basin.</summary>
    public Vector3? LastSafeGround => _trail.Newest;

    /// <summary>Is a recovery fade running right now?</summary>
    public bool IsRecovering => _fadeElapsed >= 0f;

    public WorldRecovery()
    {
        // Built in the constructor, not _Ready (CLAUDE.md §7): the overlay must exist the moment a
        // recovery can start, and a fade drawn by nothing is a teleport with no explanation.
        _fadeLayer = new CanvasLayer { Name = "RecoveryFade", Layer = 9 };
        _fade = new ColorRect
        {
            Color = new Color(0f, 0f, 0f, 0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false,
        };
        _fade.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _fadeLayer.AddChild(_fade);
    }

    public override void _Ready()
    {
        _trail = new SafeGroundTrail(TrailLength, TrailSpacing);
        AddChild(_fadeLayer);
        AddChild(new WorldWading { Name = "WorldWading" });
        EventBus.Instance?.Subscribe<RegionChangedEvent>(OnRegionChanged);
    }

    public override void _ExitTree()
    {
        EventBus.Instance?.Unsubscribe<RegionChangedEvent>(OnRegionChanged);
    }

    /// <summary>A region change moves the player without walking them; the old trail is on the
    /// other side of a load and can only mislead.</summary>
    private void OnRegionChanged(RegionChangedEvent e) => _trail.Clear();

    /// <summary>
    /// In <c>_PhysicsProcess</c>, not <c>_Process</c>: the floor probe and the capsule check are
    /// space-state queries, which are only valid inside a physics step.
    /// </summary>
    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        if (WorldGround.Field is not { } field ||
            ServiceLocator.Instance == null ||
            !ServiceLocator.Instance.TryGet(out PlayerCharacter player) ||
            !IsInstanceValid(player))
        {
            return;
        }

        if (_fadeElapsed >= 0f)
        {
            TickFade(player, field, dt);
            return;
        }

        if (GameManager.Instance is not { IsPlaying: true })
        {
            return;
        }

        Vector3 position = player.GlobalPosition;
        float terrain = field.Height(position.X, position.Z);

        if (position.Y < terrain - FallThroughDepth || position.Y < KillFloorY || float.IsNaN(position.Y))
        {
            if (WorldRecoveryRules.IsFallThrough(
                    position.Y, terrain, HasFloorBelow(player, position), FallThroughDepth, KillFloorY))
            {
                Begin(position, $"a fall out of the world ({position.Y:0.0} m, terrain {terrain:0.0} m)");
                return;
            }
        }

        float depth = WorldWater.WadingDepthAt(position.X, position.Y, position.Z, field);
        if (depth >= WorldWater.DrownDepth)
        {
            _submerged += dt;
            if (_submerged >= WaterGrace)
            {
                _submerged = 0f;
                Begin(position, $"{depth:0.0} m of water");
            }
            return;
        }

        // ⚠️ Reset only on the ground. A jump out of deep water lifts the feet above the waterline
        // for half a second; counting that as "out" would let a player hop in place forever.
        if (depth <= WorldWater.WadeDepth && player.IsOnFloor())
        {
            _submerged = 0f;
            RememberSafePoint(player, field, position, depth, dt);
        }

        TrackPit(field, position, dt);
    }

    private void RememberSafePoint(
        PlayerCharacter player, WorldHeightfield field, Vector3 position, float depth, float delta)
    {
        _sampleTimer += delta;
        if (_sampleTimer < SampleSeconds)
        {
            return;
        }

        _sampleTimer = 0f;
        // ⚠️ Dry AND gentle AND not already in a hole. A safe point taken on the lip of the thing
        // the player is about to slide into is not a recovery, it is a loop. Floor contact is the
        // caller's check: a point sampled mid-jump is a point in the air.
        if (depth <= 0.05f && IsSafeGround(field, position))
        {
            _trail.Record(position);
        }
    }

    /// <summary>The heightfield half of "is this safe ground": dry, gentle, and not in a hole.</summary>
    private bool IsSafeGround(WorldHeightfield field, Vector3 point) =>
        WorldWater.DepthAt(point.X, point.Z, field) <= 0.05f &&
        field.SlopeAt(point.X, point.Z) <= MaxSafeSlope &&
        !InHole(field, point);

    private void TrackPit(WorldHeightfield field, Vector3 position, float delta)
    {
        if (!InHole(field, position))
        {
            _stuck = 0f;
            return;
        }

        if (_stuck <= 0f || position.DistanceTo(_pitAnchor) > ProgressDistance)
        {
            _pitAnchor = position;
            _stuck = 0.0001f;
            return;
        }

        _stuck += delta;
        if (_stuck < PitGrace)
        {
            return;
        }

        _stuck = 0f;
        if (HasEscape(field, position))
        {
            // A canyon with a road along it. Nothing to do; the next check is another grace away.
            return;
        }

        Begin(position, "a pit with no walkable exit");
    }

    /// <summary>Is the ground here well below everything around it?</summary>
    private bool InHole(WorldHeightfield field, Vector3 position)
    {
        float here = field.Height(position.X, position.Z);
        float highest = here;
        for (int i = 0; i < 8; i++)
        {
            float angle = Mathf.Tau * i / 8f;
            highest = Mathf.Max(highest, field.Height(
                position.X + (Mathf.Cos(angle) * PitReach),
                position.Z + (Mathf.Sin(angle) * PitReach)));
        }
        return highest - here >= PitDepth;
    }

    /// <summary>
    /// A local walkability flood fill: can the player climb out of here within
    /// <see cref="PitReach"/> metres? Shares <see cref="WorldTraversalAnalysis"/>'s grade limit, so
    /// the runtime and the validator agree on what "walkable" means.
    /// </summary>
    private bool HasEscape(WorldHeightfield field, Vector3 position)
    {
        const float Step = 2f;
        int span = Mathf.CeilToInt(PitReach / Step);
        int side = (span * 2) + 1;
        float climbLimit = Step * WorldTraversalAnalysis.MaxGrade;
        float start = field.Height(position.X, position.Z);

        var heights = new float[side * side];
        for (int z = 0; z < side; z++)
        {
            for (int x = 0; x < side; x++)
            {
                heights[(z * side) + x] = field.Height(
                    position.X + ((x - span) * Step), position.Z + ((z - span) * Step));
            }
        }

        var seen = new bool[side * side];
        var queue = new System.Collections.Generic.Queue<int>();
        int origin = (span * side) + span;
        seen[origin] = true;
        queue.Enqueue(origin);
        while (queue.Count > 0)
        {
            int here = queue.Dequeue();
            int hx = here % side;
            int hz = here / side;
            // Reaching ground a clear step above the pit floor, at the edge of the search, is an
            // exit: the player is on a slope that keeps going up and out.
            if (heights[here] - start >= PitDepth * 0.9f)
            {
                return true;
            }
            for (int direction = 0; direction < 4; direction++)
            {
                int nx = hx + (direction == 0 ? 1 : direction == 1 ? -1 : 0);
                int nz = hz + (direction == 2 ? 1 : direction == 3 ? -1 : 0);
                if (nx < 0 || nz < 0 || nx >= side || nz >= side)
                {
                    continue;
                }
                int next = (nz * side) + nx;
                if (seen[next] || heights[next] - heights[here] > climbLimit)
                {
                    continue;
                }
                seen[next] = true;
                queue.Enqueue(next);
            }
        }

        return false;
    }

    /// <summary>Is there any world collision under the player within <see cref="FloorProbeDepth"/>?
    /// The difference between a crypt under the heightfield and a void.</summary>
    private bool HasFloorBelow(PlayerCharacter player, Vector3 position)
    {
        var query = PhysicsRayQueryParameters3D.Create(
            position + (Vector3.Up * 0.5f), position + (Vector3.Down * FloorProbeDepth), CombatLayers.World);
        query.Exclude = new Godot.Collections.Array<Rid> { player.GetRid() };
        return player.GetWorld3D().DirectSpaceState.IntersectRay(query).Count > 0;
    }

    /// <summary>Starts the fade; the move happens at its peak.</summary>
    private void Begin(Vector3 hazard, string reason)
    {
        _pendingHazard = hazard;
        _pendingReason = reason;
        _fadeElapsed = 0f;
        _moved = false;
        _fade.Visible = true;
    }

    private void TickFade(PlayerCharacter player, WorldHeightfield field, float dt)
    {
        _fadeElapsed += dt;
        _fade.Color = new Color(0f, 0f, 0f,
            WorldRecoveryRules.FadeAlpha(_fadeElapsed, FadeOutSeconds, FadeInSeconds));

        // Hold the body still behind the black so a player sinking or falling does not arrive with
        // the momentum of the hazard they were pulled out of.
        player.Velocity = Vector3.Zero;

        if (!_moved && WorldRecoveryRules.IsAtPeak(_fadeElapsed, FadeOutSeconds))
        {
            _moved = true;
            // A load or a portal that landed mid-fade (the tree pauses for it, so the fade resumes
            // afterwards) has already moved the player somewhere safe. Pulling them back to a trail
            // point from the old position would undo it.
            if (player.GlobalPosition.DistanceTo(_pendingHazard) <= StaleHazardDistance)
            {
                Recover(player, field, _pendingHazard, _pendingReason);
            }
        }

        if (WorldRecoveryRules.IsFinished(_fadeElapsed, FadeOutSeconds, FadeInSeconds))
        {
            _fadeElapsed = -1f;
            _fade.Visible = false;
            _fade.Color = new Color(0f, 0f, 0f, 0f);
        }
    }

    private void Recover(PlayerCharacter player, WorldHeightfield field, Vector3 from, string reason)
    {
        _submerged = 0f;
        _stuck = 0f;

        Vector3 landing = Vector3.Zero;
        bool placed = false;

        // ⚠️ Re-judged NOW, not trusted from when it was recorded. A point that was dry when the
        // player walked over it can be under a world event's barricade, a companion or a streamed
        // collider since; recovering onto a second hazard is the loop this whole node exists to end.
        Vector3? trailPoint = _trail.Pick(
            from,
            point => IsSafeGround(field, point) && TryPlace(player, point, out _),
            MinHazardDistance, MaxRecoveryDistance);
        if (trailPoint is { } chosen && TryPlace(player, chosen, out landing))
        {
            placed = true;
        }
        else if (NearestShore(player, field, from) is { } shore)
        {
            landing = shore;
            placed = true;
        }

        if (!placed)
        {
            // Nothing within reach passes the capsule check — collision is probably not resident.
            // The heightfield is still better than the void or the bottom of the lake.
            Vector3 target = _trail.Newest ?? from;
            landing = new Vector3(target.X, field.Height(target.X, target.Z) + 0.6f, target.Z);
            Log.Warn($"WorldRecovery: no capsule-clear ground near {from.Snapped(Vector3.One)}; " +
                     $"placing on the heightfield at {landing.Snapped(Vector3.One)}.");
        }

        player.GlobalPosition = landing;
        player.Velocity = Vector3.Zero;
        if (WorldRecoveryRules.FacingAwayYaw(from.X, from.Z, landing.X, landing.Z) is { } yaw)
        {
            player.Rotation = new Vector3(0f, yaw, 0f);
        }

        Log.Info($"WorldRecovery: pulled the player out of {reason} at {from.Snapped(Vector3.One)} " +
                 $"back to {landing.Snapped(Vector3.One)}.");
        EventBus.Instance?.Publish(new WorldHazardNoticeEvent(RecoveredKey));
    }

    /// <summary>Capsule-validates a candidate against real collision, with no ring search of its own
    /// — the trail and the shore walk are already the search.</summary>
    private static bool TryPlace(PlayerCharacter player, Vector3 point, out Vector3 landing) =>
        SafePlacementService.TryResolve(player, point, out landing, searchRadius: 0f);

    /// <summary>
    /// The fallback when the trail has nothing usable — a save loaded straight into a hole, or a
    /// teleport. Walks outward on a coarse spiral for the first dry, walkable ground a capsule fits on.
    /// </summary>
    private Vector3? NearestShore(PlayerCharacter player, WorldHeightfield field, Vector3 from)
    {
        for (float radius = 6f; radius <= 90f; radius += 6f)
        {
            int samples = Mathf.Max(8, Mathf.RoundToInt(radius));
            for (int i = 0; i < samples; i++)
            {
                float angle = Mathf.Tau * i / samples;
                float x = from.X + (Mathf.Cos(angle) * radius);
                float z = from.Z + (Mathf.Sin(angle) * radius);
                var point = new Vector3(x, field.Height(x, z), z);
                if (IsSafeGround(field, point) && TryPlace(player, point, out Vector3 landing))
                {
                    return landing;
                }
            }
        }

        Log.Warn("WorldRecovery: no walkable ground within 90 m of the player.");
        return null;
    }
}
