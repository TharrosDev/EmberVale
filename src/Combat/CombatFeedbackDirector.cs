using Embervale.Core.Events;
using Embervale.Core.Pooling;
using Godot;

namespace Embervale.Combat;

/// <summary>
/// Combat impact feedback (Phase 29C): on every resolved hit it spawns a placeholder spark at the target
/// through a pooled <see cref="ImpactEffect"/> (CLAUDE.md §8) and publishes a positional
/// <see cref="SoundCueRequestedEvent"/> the Phase 31 audio system will consume. Owns the effect pool for
/// its lifetime.
/// </summary>
public partial class CombatFeedbackDirector : Node
{
    private NodePool<ImpactEffect> _pool = null!;

    public override void _Ready()
    {
        _pool = new NodePool<ImpactEffect>(() => new ImpactEffect { Released = Reclaim }, prewarm: 6);
        EventBus.Instance?.Subscribe<DamageDealtEvent>(OnDamage);
    }

    public override void _ExitTree()
    {
        EventBus.Instance?.Unsubscribe<DamageDealtEvent>(OnDamage);
        _pool?.Clear();
    }

    private void Reclaim(ImpactEffect effect) => _pool.Return(effect);

    private void OnDamage(DamageDealtEvent e)
    {
        Vector3 pos = e.Target.Body.GlobalPosition + (Vector3.Up * 1.1f);

        (float r, float g, float b) = CombatFx.Tint(e.IsCrit, e.IsBlocked);
        ImpactEffect spark = _pool.Get();
        GetTree().CurrentScene.AddChild(spark);
        spark.GlobalPosition = pos;
        spark.Launch(new Color(r, g, b));

        EventBus.Instance?.Publish(new SoundCueRequestedEvent(CombatFx.CueId(e.IsCrit, e.IsBlocked), pos));
    }
}
