using Embervale.Combat;
using Embervale.Core.Events;
using Embervale.Magic;
using Godot;

namespace Embervale.Debugging;

/// <summary>
/// The C# half of <c>tools/magic_core_probe.gd</c>. The magic events are C# structs a GDScript probe
/// cannot subscribe to, so this counts the ones the casting core raises and exposes what they carried
/// through Godot types. It contains no rules of its own.
/// </summary>
public partial class MagicCoreProbeDriver : RefCounted
{
    private string _respawningName = string.Empty;

    /// <summary>Registers before the actor, matching PlayerHost's immediate death/respawn ordering.</summary>
    public void BeginImmediateRespawn(string actorName)
    {
        _respawningName = actorName;
        EventBus.Instance.Subscribe<EntityDiedEvent>(RespawnImmediately);
    }

    public void EndImmediateRespawn()
    {
        EventBus.Instance.Unsubscribe<EntityDiedEvent>(RespawnImmediately);
        _respawningName = string.Empty;
    }

    private void RespawnImmediately(EntityDiedEvent e)
    {
        if (e.Entity.Body.Name == _respawningName)
        {
            e.Entity.GetComponent<Embervale.Stats.StatsComponent>()?.RefillResources();
        }
    }

    public void Kill(Node3D body) =>
        (body as Entities.IEntity)?.GetComponent<Embervale.Stats.StatsComponent>()?.ApplyDamage(99999f);

    public void PreLoad() => EventBus.Instance.Publish(new GameLoadingEvent("magic_probe"));

    /// <summary>Raises the actual action release before an idle interrupt poll can run.</summary>
    public void ReleaseCast(Node3D body) => EventBus.Instance.Publish(
        new ActionReleasedEvent((Entities.IEntity)body, "action.probe_release", Embervale.Combat.Actions.ActionKind.Cast));

    public void Hit(Node3D target, SpellResource spell, Node3D casterBody, float damage)
    {
        if (target.GetNodeOrNull<Hurtbox>("Hurtbox") is { } hurtbox)
        {
            var caster = casterBody as Entities.IEntity;
            int team = caster?.GetComponent<CombatComponent>()?.Team ?? 0;
            SpellResolver.HitOne(casterBody, hurtbox,
                new DamagePacket(damage, spell.School, caster, false, 0f, HitKind.Spell, Unblockable: true),
                spell, caster, team);
        }
    }

    public float ExpectedLifesteal(float damage, float fraction, bool marked) =>
        SchoolIdentity.LifestealAmount(damage, fraction, marked);

    public int WindupStarted { get; private set; }

    public float LastWindup { get; private set; }

    public string LastWindupSpell { get; private set; } = string.Empty;

    public int Interrupted { get; private set; }

    public string LastInterruptedSpell { get; private set; } = string.Empty;

    /// <summary>True when the last interrupt named who caused it.</summary>
    public bool LastInterrupterKnown { get; private set; }

    public int Blocked { get; private set; }

    public string LastBlockedSpell { get; private set; } = string.Empty;

    public int Hits { get; private set; }

    public float LastHitAmount { get; private set; }

    public string LastHitSpell { get; private set; } = string.Empty;

    public int BarrierEnded { get; private set; }

    public int BarrierBroken { get; private set; }

    public int Confirmed { get; private set; }

    public int LastConfirmedKind { get; private set; }

    public float LastConfirmedWeight { get; private set; }

    public int Casts { get; private set; }

    /// <summary>Subscribes to the events this probe counts. Call once, after the tree exists.</summary>
    public void Listen()
    {
        EventBus bus = EventBus.Instance;
        bus.Subscribe<CastWindupStartedEvent>(e =>
        {
            WindupStarted++;
            LastWindup = e.WindupSeconds;
            LastWindupSpell = e.SpellId;
        });
        bus.Subscribe<SpellInterruptedEvent>(e =>
        {
            Interrupted++;
            LastInterruptedSpell = e.SpellId;
            LastInterrupterKnown = e.Interrupter != null;
        });
        bus.Subscribe<SpellBlockedEvent>(e =>
        {
            Blocked++;
            LastBlockedSpell = e.SpellId;
        });
        bus.Subscribe<SpellHitEvent>(e =>
        {
            Hits++;
            LastHitAmount = e.Amount;
            LastHitSpell = e.SpellId;
        });
        bus.Subscribe<BarrierEndedEvent>(e =>
        {
            BarrierEnded++;
            if (e.Broken)
            {
                BarrierBroken++;
            }
        });
        bus.Subscribe<HitConfirmedEvent>(e =>
        {
            Confirmed++;
            LastConfirmedKind = (int)e.Kind;
            LastConfirmedWeight = e.Weight;
        });
        bus.Subscribe<SpellCastEvent>(_ => Casts++);
    }

    /// <summary>Zeroes the counters between cases.</summary>
    public void Reset()
    {
        WindupStarted = Interrupted = Blocked = Hits = BarrierEnded = BarrierBroken = Confirmed = Casts = 0;
        LastInterrupterKnown = false;
    }

    /// <summary>Looses a real <see cref="Arrow"/> (a struct packet cannot cross from GDScript) from
    /// <paramref name="from"/> along <paramref name="direction"/>, for a spell barrier to eat or a body to take.</summary>
    public void FireArrow(Node3D parent, Node3D shooterBody, Vector3 from, Vector3 direction, float damage, int team)
    {
        var arrow = new Arrow();
        parent.AddChild(arrow);
        arrow.GlobalPosition = from;
        Entities.IEntity? shooter = shooterBody as Entities.IEntity;
        arrow.Launch(
            new DamagePacket(damage, DamageType.Physical, shooter, false, 5f, HitKind.Ranged),
            shooter, team, direction, 30f, 40f, string.Empty);
    }

    /// <summary>Applies a status from a loaded resource with no source (<c>Apply</c> takes an
    /// <c>IEntity</c>, an interface a GDScript call cannot pass).</summary>
    public void ApplyStatus(StatusEffectsComponent status, StatusEffectResource definition) =>
        status.Apply(definition, null);

    public void ApplyStatusFrom(StatusEffectsComponent status, StatusEffectResource definition, Node3D source) =>
        status.Apply(definition, source as Entities.IEntity);

    public void StepStatuses(StatusEffectsComponent status, double seconds) => status._Process(seconds);

    public bool WorldSegmentClear(Node3D context, Vector3 from, Vector3 to)
    {
        using var ray = new WorldRay();
        return ray.FirstSolid(context.GetWorld3D().DirectSpaceState, from, to) == null;
    }

    public double StatusRemaining(StatusEffectsComponent statuses, string id)
    {
        foreach (StatusEffect effect in statuses.ActiveEffects)
        {
            if (effect.Definition.Id == id)
            {
                return effect.Remaining;
            }
        }
        return 0d;
    }

    /// <summary>Uses an actual ground spell's burst resolver without a struct crossing GDScript.</summary>
    public void GroundImpact(Node3D context, SpellResource spell, Node3D casterBody, Vector3 centre,
        float damage, float charge, int team)
    {
        Entities.IEntity? caster = casterBody as Entities.IEntity;
        SpellResolver.Detonate(context, spell,
            new DamagePacket(damage, spell.School, caster, false, spell.PoiseDamage, HitKind.Spell, charge,
                Unblockable: !spell.Blockable), caster, team, centre, spell.ImpactRadius);
    }
}
