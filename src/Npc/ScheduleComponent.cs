using Embervale.Core;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Dialogue;
using Embervale.Enemies;
using Embervale.Entities;
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

    private ScheduleResource? _schedule;
    private Node3D _body = null!;
    private Vector3 _target;
    private string _activity = string.Empty;
    private double _panicTimer;
    private bool _talking;

    private bool Panicking => _panicTimer > 0d;

    protected override void OnInitialize()
    {
        _body = Entity!.Body;
        _schedule = ScheduleDatabase.Get(ScheduleId);
        _target = _body.GlobalPosition;

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

        _target = entry.Destination;
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

        _target = here + (away.Normalized() * PanicRadius);
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
        if (dist <= ArriveDistance)
        {
            return;
        }

        float step = WalkSpeed * (float)delta;
        if (step >= dist)
        {
            _body.GlobalPosition = new Vector3(target.X, pos.Y, target.Z);
        }
        else
        {
            _body.GlobalPosition = pos + (to / dist * step);
        }

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
}
