using Embervale.Core;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Dialogue;
using Embervale.Enemies;
using Embervale.Entities;
using Embervale.Player;
using Embervale.World;
using Godot;

namespace Embervale.Npc;

/// <summary>
/// Drives a non-combat NPC through a daily routine and lets it react to the world. It
/// reads the <see cref="WorldClock"/>, picks the <see cref="ScheduleEntry"/> for the
/// current hour and walks the host (a static <see cref="Entity"/>) toward that block's
/// destination, facing where it goes. Movement is a simple kinematic step — villagers
/// don't need physics.
///
/// Reactions (event-driven, the established pattern):
///   * a nearby <see cref="EnemyAlertedEvent"/> makes it flee away from the threat for a
///     short panic window, overriding the schedule;
///   * a <see cref="DialogueStartedEvent"/> where it is the speaker freezes it so it
///     stops and faces the player until the conversation ends.
/// </summary>
[GlobalClass]
public partial class ScheduleComponent : EntityComponent
{
    /// <summary>Routine resolved through the <see cref="ScheduleDatabase"/>.</summary>
    [Export] public string ScheduleId { get; set; } = string.Empty;

    [Export] public float WalkSpeed { get; set; } = 1.6f;

    /// <summary>Distance at which the NPC counts as "arrived" and stops.</summary>
    [Export] public float ArriveDistance { get; set; } = 0.35f;

    /// <summary>An alert within this range triggers a panic/flee reaction.</summary>
    [Export] public float PanicRadius { get; set; } = 7f;

    [Export] public float PanicSeconds { get; set; } = 8f;

    [ExportGroup("Level of Detail")]
    /// <summary>Beyond this distance from the player the NPC integrates movement on a coarse
    /// cadence instead of every frame (event reactions stay instant). Mirrors the enemy AI LOD.</summary>
    [Export] public float ActiveDistance { get; set; } = 40f;

    /// <summary>Seconds between movement steps while far from the player.</summary>
    [Export] public float SleepInterval { get; set; } = 0.5f;

    private ScheduleResource? _schedule;
    private Node3D _body = null!;
    private Vector3 _target;
    private Vector3 _cellOrigin;
    private string _activity = string.Empty;
    private double _panicTimer;
    private bool _talking;
    private double _sleepTimer;

    /// <summary>True once the last movement step found the body at its target. A townsperson
    /// spends most of the day standing where its routine put it, and a standing body has no
    /// movement to integrate — so it drops to the same coarse cadence a distant one uses instead of
    /// reading its position across the engine boundary every frame to learn it has not moved.
    /// Cleared the moment a new target is set, so leaving is as immediate as it ever was.</summary>
    private bool _arrived;

    /// <summary>Seconds between "is the player near" checks; the answer only picks a cadence.</summary>
    private const double FarCheckInterval = 0.25d;

    private double _farCheckTimer;
    private bool _far;

    /// <summary>Sets where the routine is walking to and wakes the movement step for it.</summary>
    private void SetTarget(Vector3 target)
    {
        _target = target;
        if (_arrived)
        {
            _arrived = false;
            _sleepTimer = 0d; // do not hand the first step the time spent standing
        }
    }

    /// <summary>How far the body's origin sits above the ground, captured where it was authored.
    /// Almost every NPC is 0 (origin at the feet); a body placed on a plinth keeps its plinth.</summary>
    private float _groundClearance;

    private bool Panicking => _panicTimer > 0d;

    protected override void OnInitialize()
    {
        _body = Entity!.Body;
        _schedule = ScheduleDatabase.Get(ScheduleId);
        _cellOrigin = CellOriginOf(_body);
        _target = _body.GlobalPosition;
        Vector3 here = _body.GlobalPosition;
        _groundClearance = here.Y - WorldGround.HeightAt(here.X, here.Z);

        EventBus.Instance?.Subscribe<TimeOfDayChangedEvent>(OnTimeChanged);
        EventBus.Instance?.Subscribe<EnemyAlertedEvent>(OnEnemyAlerted);
        EventBus.Instance?.Subscribe<DialogueStartedEvent>(OnDialogueStarted);
        EventBus.Instance?.Subscribe<DialogueEndedEvent>(OnDialogueEnded);

        ApplyScheduleFor(CurrentHour());
    }

    protected override void OnTeardown()
    {
        EventBus.Instance?.Unsubscribe<TimeOfDayChangedEvent>(OnTimeChanged);
        EventBus.Instance?.Unsubscribe<EnemyAlertedEvent>(OnEnemyAlerted);
        EventBus.Instance?.Unsubscribe<DialogueStartedEvent>(OnDialogueStarted);
        EventBus.Instance?.Unsubscribe<DialogueEndedEvent>(OnDialogueEnded);
    }

    public string Activity => _activity;

    public override void _Process(double delta)
    {
        if (GameManager.Instance is { IsPlaying: false })
        {
            return;
        }

        // LOD: far from the player, integrate on a coarse cadence with the accumulated time, so a
        // crowd of distant villagers doesn't tick movement every frame. Reactions (panic/dialogue)
        // are event-driven and stay instant; only this per-frame movement step is throttled.
        _farCheckTimer -= delta;
        if (_farCheckTimer <= 0d)
        {
            _farCheckTimer = FarCheckInterval;
            _far = IsFarFromPlayer();
        }

        // Standing at the target (or held in conversation) with no panic running is the same
        // "nothing to integrate" case as being far away, and takes the same cadence.
        bool resting = (_arrived || _talking) && !Panicking;
        if (_far || resting)
        {
            _sleepTimer += delta;
            if (_sleepTimer < SleepInterval)
            {
                return;
            }

            delta = _sleepTimer;
            _sleepTimer = 0d;
        }

        if (_panicTimer > 0d)
        {
            _panicTimer -= delta;
            if (_panicTimer <= 0d)
            {
                ApplyScheduleFor(CurrentHour()); // calm restored — resume the routine
            }
        }

        // Stand and face the player while in conversation.
        if (_talking)
        {
            return;
        }

        MoveToward(_target, delta);
    }

    // --- Schedule -----------------------------------------------------------

    private void ApplyScheduleFor(int hour)
    {
        if (_schedule?.EntryForHour(hour) is not { } entry)
        {
            return;
        }

        // Through ScheduleResource.DestinationOf, never entry.Destination directly: a cell-local
        // routine is only a place once its cell is added, and one caller that forgets walks a
        // merchant out of the market and into the town square.
        SetTarget(ScheduleResource.DestinationOf(entry, _cellOrigin));
        SetActivity(entry.Activity);
    }

    private void OnTimeChanged(TimeOfDayChangedEvent e)
    {
        // The schedule yields to active reactions; they re-pick the block when they end.
        if (Panicking || _talking)
        {
            return;
        }

        ApplyScheduleFor(e.Hour);
    }

    /// <summary>The world position of the streamed cell <paramref name="node"/> belongs to: the
    /// ancestor the <see cref="RegionStreamer"/> parents directly. Zero outside a streamed region (a
    /// cell instanced bare by a capture harness sits at the origin, so local is world there).</summary>
    private static Vector3 CellOriginOf(Node node)
    {
        for (Node? current = node; current != null; current = current.GetParent())
        {
            if (current is Node3D cell && current.GetParent() is RegionStreamer)
            {
                return cell.GlobalPosition;
            }
        }
        return Vector3.Zero;
    }

    // --- Reactions ----------------------------------------------------------

    private void OnEnemyAlerted(EnemyAlertedEvent e)
    {
        Vector3 here = _body.GlobalPosition;
        if (here.DistanceTo(e.Position) > PanicRadius)
        {
            return;
        }

        _panicTimer = PanicSeconds;
        SetActivity("Fleeing");

        Vector3 away = here - e.Position;
        away.Y = 0f;
        if (away.LengthSquared() < 0.01f)
        {
            away = Vector3.Forward;
        }

        SetTarget(here + (away.Normalized() * PanicRadius));
    }

    private void OnDialogueStarted(DialogueStartedEvent e)
    {
        if (!ReferenceEquals(e.Speaker, Entity))
        {
            return;
        }

        _talking = true;
        Face(e.Player.Body.GlobalPosition);
    }

    private void OnDialogueEnded(DialogueEndedEvent e)
    {
        if (!_talking)
        {
            return;
        }

        _talking = false;
        if (!Panicking)
        {
            ApplyScheduleFor(CurrentHour());
        }
    }

    // --- Movement -----------------------------------------------------------

    private void MoveToward(Vector3 target, double delta)
    {
        Vector3 pos = _body.GlobalPosition;
        Vector3 to = target - pos;
        to.Y = 0f;

        float dist = to.Length();
        _arrived = dist <= ArriveDistance;
        if (_arrived)
        {
            return;
        }

        // ⚠️ THIS SLIDE KEEPS Y, AND THAT WAS ONLY EVER RIGHT ON A FLAT WORLD. An NPC here is moved
        // by writing GlobalPosition, so it has no gravity and no floor: before the 2026-08-29
        // overhaul it held whatever height it spawned at, which was 0 everywhere. Now the routine
        // has to follow the ground, or the Hollowreach fence walks under his own boardwalk and the
        // clan quartermaster walks over the beast runs.
        float step = WalkSpeed * (float)delta;
        Vector3 next = step >= dist
            ? new Vector3(target.X, pos.Y, target.Z)
            : pos + (to / dist * step);
        _body.GlobalPosition = new Vector3(
            next.X, WorldGround.HeightAt(next.X, next.Z) + _groundClearance, next.Z);

        Face(target);
    }

    private void Face(Vector3 target)
    {
        Vector3 pos = _body.GlobalPosition;
        var flat = new Vector3(target.X, pos.Y, target.Z);
        if (flat.DistanceSquaredTo(pos) > 0.0009f)
        {
            _body.LookAt(flat, Vector3.Up);
        }
    }

    // --- Helpers ------------------------------------------------------------

    private void SetActivity(string activity)
    {
        if (_activity == activity)
        {
            return;
        }

        _activity = activity;
        if (Entity != null)
        {
            EventBus.Instance?.Publish(new NpcActivityChangedEvent(Entity, activity));
        }
    }

    private static int CurrentHour()
    {
        return ServiceLocator.Instance != null && ServiceLocator.Instance.TryGet(out WorldClock clock)
            ? clock.Hour
            : 8;
    }

    /// <summary>True when the player is registered and beyond <see cref="ActiveDistance"/>. With no
    /// player resolvable, returns false so the NPC ticks every frame exactly as before.</summary>
    private bool IsFarFromPlayer()
    {
        if (ServiceLocator.Instance == null ||
            !ServiceLocator.Instance.TryGet(out PlayerCharacter player) ||
            !IsInstanceValid(player))
        {
            return false;
        }

        return _body.GlobalPosition.DistanceSquaredTo(player.GlobalPosition) > ActiveDistance * ActiveDistance;
    }
}
