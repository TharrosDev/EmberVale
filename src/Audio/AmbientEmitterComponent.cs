using System;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Player;
using Godot;

namespace Embervale.Audio;

/// <summary>
/// A positional ambience emitter you drop into a cell scene (Phase 38K second pass): it publishes one
/// <see cref="SoundCueRequestedEvent"/> at its own position on a jittered interval while the player is
/// close enough to hear it. The <see cref="AudioDirector"/> already routes any <c>sfx.*</c> cue to a
/// pooled <see cref="PositionalSfxPlayer"/> (<see cref="AudioCueRouting.IsPositional"/>), so a market
/// murmur or a crackling brazier costs a node in the scene and a cue id — no new routing, no new bus.
///
/// <para><b>Why this is not an <c>amb.*</c> bed.</b> <see cref="AmbienceSelection.Resolve"/> picks one
/// <em>global</em> 2D bed from weather, then "in town", then the clock, and "in town" is a
/// <see cref="World.SafeZones"/> membership test that cannot tell the Embermarket from the square one
/// street north. A new member there would have changed the ambience of the whole town to get a sound
/// into one district. A positional emitter is both the smaller change and the correct one — and it
/// attenuates with distance, which a bed never does.</para>
///
/// <para>This is a plain <see cref="Node3D"/> rather than an <c>EntityComponent</c> on purpose: the
/// things worth making a noise here — a brazier, a cauldron, the middle of an aisle — are static props
/// and bare markers, not entities, and giving them an <c>Entity</c> just to host a sound would put a
/// <c>DisplayName</c> and an interact prompt on a fire.</para>
/// </summary>
[GlobalClass]
public partial class AmbientEmitterComponent : Node3D
{
    /// <summary>Cue id to publish. Empty disables the emitter. Use an <c>sfx.*</c> prefix — anything
    /// else routes 2D (<see cref="AudioCueRouting"/>) and the sound stops being positional.</summary>
    [Export] public string CueId { get; set; } = string.Empty;

    /// <summary>Mean seconds between plays.</summary>
    [Export] public float IntervalSeconds { get; set; } = 6f;

    /// <summary>Fraction of the interval to vary by, 0..1. At 0 the emitter is a metronome, which is
    /// the one thing an ambience emitter must never sound like.</summary>
    [Export] public float IntervalJitter { get; set; } = 0.5f;

    /// <summary>Beyond this distance from the player the emitter stays silent. It keeps its clock
    /// running rather than resetting it, so walking into range does not always land on a fresh cue.</summary>
    [Export] public float Radius { get; set; } = 18f;

    private double _timer;

    /// <summary>The delay before the next cue: <paramref name="interval"/> scaled by a roll across
    /// <c>±jitter</c>. Pure so it unit-tests without Godot. Floored well above zero because a jitter of
    /// 1 with a roll of 0 would otherwise ask for a zero-length wait, and an emitter that fires every
    /// frame is a wall of sound rather than an ambience.</summary>
    public static double NextInterval(float interval, float jitter, float roll)
    {
        float mean = Math.Max(0.05f, interval);
        float spread = Math.Clamp(jitter, 0f, 1f);
        float scale = 1f - spread + (2f * spread * Math.Clamp(roll, 0f, 1f));
        return Math.Max(0.05f, mean * scale);
    }

    public override void _Ready()
    {
        // Stagger the first play: several emitters authored in one scene all start on the same frame,
        // and without this they would chorus on every cycle instead of overlapping.
        _timer = NextInterval(IntervalSeconds, IntervalJitter, GD.Randf());
    }

    public override void _Process(double delta)
    {
        if (CueId.Length == 0)
        {
            return;
        }

        _timer -= delta;
        if (_timer > 0d)
        {
            return;
        }

        _timer = NextInterval(IntervalSeconds, IntervalJitter, GD.Randf());

        if (PlayerInRange())
        {
            EventBus.Instance?.Publish(new SoundCueRequestedEvent(CueId, GlobalPosition));
        }
    }

    /// <summary>True when the player is within <see cref="Radius"/>. With no player resolvable — the
    /// main menu, a validator run — this is false, so the emitter is silent rather than shouting into
    /// an empty world.</summary>
    private bool PlayerInRange()
    {
        if (ServiceLocator.Instance == null ||
            !ServiceLocator.Instance.TryGet(out PlayerCharacter player) ||
            !IsInstanceValid(player))
        {
            return false;
        }

        return GlobalPosition.DistanceSquaredTo(player.GlobalPosition) <= Radius * Radius;
    }
}
