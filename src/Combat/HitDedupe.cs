using System.Collections.Generic;

namespace Embervale.Combat;

/// <summary>
/// "Has this actor already been hit by this swing/blast?" — the once-per-target rule, keyed on the
/// <em>owning entity</em> rather than the hurtbox that happened to overlap.
///
/// Until Phase 35A every actor had exactly one <see cref="Hurtbox"/>, so per-hurtbox and per-actor
/// dedupe were the same thing. A dragon has four (head/wings/body/tail), and a sword arc or a
/// fireball that clips three of them would otherwise deliver three full <see cref="DamagePacket"/>s
/// for one attack. Both damage entry points — <see cref="Hitbox"/> polling and
/// <see cref="Embervale.Magic.SpellResolver.Detonate"/> — route through here so the rule is fixed in
/// one place instead of at each caller.
///
/// Takes plain <c>object</c>s rather than <c>Hurtbox</c>/<c>IEntity</c> so it stays Godot-free and
/// unit-testable. The hurtbox itself is the fallback key for an owner-less hurtbox (the training
/// dummy in <c>GameBootstrap</c> is one), which preserves the old behaviour exactly.
/// </summary>
public sealed class HitDedupe
{
    private readonly HashSet<object> _struck = new();

    /// <summary>True the first time this owner is struck, false for every later hurtbox of the same
    /// owner within the same window.</summary>
    public bool TryHit(object? owner, object hurtbox)
    {
        return _struck.Add(owner ?? hurtbox);
    }

    /// <summary>Reopens the window — every actor becomes hittable again.</summary>
    public void Clear()
    {
        _struck.Clear();
    }
}
