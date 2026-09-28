using Embervale.Combat;
using Embervale.Core.Events;
using Embervale.Entities;
using Godot;

namespace Embervale.Debugging;

/// <summary>
/// The C# half of <c>tools/combat_defence_probe.gd</c>. A <see cref="DamagePacket"/> is a C# struct, so a
/// GDScript probe cannot build one; this lets it deliver a real packet through a real
/// <see cref="Hurtbox"/> or arm a real <see cref="Hitbox"/>, and counts the combat events the defence
/// pipeline publishes so the probe can assert on them. It contains no rules of its own.
/// </summary>
public partial class CombatProbeDriver : RefCounted
{
    public int GuardBroken { get; private set; }

    public int Criticals { get; private set; }

    public int Parries { get; private set; }

    public int GradedParries { get; private set; }

    public int Windows { get; private set; }

    public int Staggers { get; private set; }

    public int LastParryGrade { get; private set; }

    public int LastCriticalKind { get; private set; }

    public int LastWindowCause { get; private set; }

    /// <summary>Subscribes to the events this probe counts. Call once, after the tree exists.</summary>
    public void Listen()
    {
        EventBus bus = EventBus.Instance;
        bus.Subscribe<GuardBrokenEvent>(_ => GuardBroken++);
        bus.Subscribe<CriticalHitEvent>(e =>
        {
            Criticals++;
            LastCriticalKind = (int)e.Kind;
        });
        bus.Subscribe<EntityParriedEvent>(_ => Parries++);
        bus.Subscribe<ParryGradedEvent>(e =>
        {
            GradedParries++;
            LastParryGrade = (int)e.Grade;
        });
        bus.Subscribe<PunishWindowOpenedEvent>(e =>
        {
            Windows++;
            LastWindowCause = (int)e.Cause;
        });
        bus.Subscribe<EntityStaggeredEvent>(_ => Staggers++);
    }

    /// <summary>Delivers one blow straight through a hurtbox, as a hitbox would, and reports the result.</summary>
    public Godot.Collections.Dictionary Deliver(
        Hurtbox hurtbox, Node attacker, float amount, int kind, float charge, float poise)
    {
        DamageResult r = hurtbox.Receive(Packet(attacker, amount, kind, charge, poise));
        return new Godot.Collections.Dictionary
        {
            ["final"] = r.FinalAmount,
            ["crit"] = r.IsCrit,
            ["blocked"] = r.IsBlocked,
            ["parry"] = (int)r.Parry,
            ["guard_broken"] = r.GuardBroken,
            ["opening"] = (int)r.Opening,
        };
    }

    /// <summary>Opens a real hitbox with a packet; it hits whatever overlaps it on the physics step.</summary>
    public void Arm(Hitbox hitbox, Node attacker, float amount, int kind, float charge, float poise)
    {
        hitbox.Activate(Packet(attacker, amount, kind, charge, poise));
    }

    private static DamagePacket Packet(Node attacker, float amount, int kind, float charge, float poise) =>
        new(amount, DamageType.Physical, attacker as IEntity, false, poise, (HitKind)kind, charge);
}
