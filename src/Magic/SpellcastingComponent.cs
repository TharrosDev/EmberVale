using System.Collections.Generic;
using Embervale.Combat;
using Embervale.Core.Events;
using Embervale.Core.Pooling;
using Embervale.Entities;
using Embervale.Save;
using Embervale.Stats;
using Godot;

namespace Embervale.Magic;

/// <summary>
/// The spellcasting brain for an entity: the spells it knows, which one is prepared,
/// per-spell cooldowns, and the cast itself (mana spend → deliver). It is the magic
/// analogue of <see cref="MeleeWeaponComponent"/> and is deliberately input-agnostic —
/// the player controller (and later enemy AI) decides <em>when</em> to call
/// <see cref="TryCast"/> / <see cref="Cycle"/>.
///
/// Delivery is resource-driven (<see cref="SpellResource.Delivery"/>): a projectile
/// fired along the caster's aim, an instant burst around the caster, or a self heal/buff.
/// Known spells + the prepared index persist via <see cref="ISaveable"/>; cooldowns are
/// transient.
/// </summary>
[GlobalClass]
public partial class SpellcastingComponent : EntityComponent, ISaveable
{
    private const float SpellPoiseDamage = 12f;
    private const float DefaultNovaRadius = 4f;
    private const float MuzzleOffset = 1.2f;

    /// <summary>Spell ids this entity starts knowing (authored by the factory/scene).</summary>
    [Export]
    public Godot.Collections.Array<string> KnownSpellIds { get; set; } = new();

    /// <summary>Aim source for projectiles/area targeting; the player's camera pivot.
    /// Falls back to the entity body when not injected.</summary>
    public Node3D? AimNode { get; set; }

    private readonly List<SpellResource> _spells = new();
    private readonly Dictionary<string, double> _cooldowns = new();

    private StatsComponent? _stats;
    private CombatComponent? _combat;
    private int _selected;

    // Pooled projectiles: rapid casting reuses bolts instead of churning the scene tree.
    private NodePool<SpellProjectile>? _projectilePool;

    public string SaveId => SaveKey("spells");

    public IReadOnlyList<SpellResource> Spells => _spells;

    public int SelectedIndex => _selected;

    public SpellResource? Selected =>
        _spells.Count == 0 ? null : _spells[Mathf.Clamp(_selected, 0, _spells.Count - 1)];

    protected override void OnInitialize()
    {
        _stats = Entity!.GetComponent<StatsComponent>();
        _combat = Entity.GetComponent<CombatComponent>();
        _projectilePool = new NodePool<SpellProjectile>(
            () => new SpellProjectile { Released = ReturnProjectile }, prewarm: 4);
        RebuildSpells();
        SaveManager.Instance?.Register(this);
    }

    protected override void OnTeardown()
    {
        _projectilePool?.Clear();
        SaveManager.Instance?.Unregister(this);
    }

    private void ReturnProjectile(SpellProjectile projectile) => _projectilePool?.Return(projectile);

    public override void _Process(double delta)
    {
        if (_cooldowns.Count == 0)
        {
            return;
        }

        // Snapshot the keys so removing/updating entries during the tick is safe.
        foreach (string id in new List<string>(_cooldowns.Keys))
        {
            double remaining = _cooldowns[id] - delta;
            if (remaining <= 0d)
            {
                _cooldowns.Remove(id);
            }
            else
            {
                _cooldowns[id] = remaining;
            }
        }
    }

    /// <summary>Seconds of cooldown remaining for a spell (0 = ready).</summary>
    public float CooldownOf(SpellResource spell) =>
        _cooldowns.TryGetValue(spell.Id, out double cd) ? (float)Mathf.Max(0d, cd) : 0f;

    /// <summary>Moves the prepared-spell selection by <paramref name="direction"/> (wrapping).</summary>
    public void Cycle(int direction)
    {
        if (_spells.Count == 0)
        {
            return;
        }

        int count = _spells.Count;
        _selected = (((_selected + direction) % count) + count) % count;
        if (Entity != null && Selected != null)
        {
            EventBus.Instance?.Publish(new SpellSelectedEvent(Entity, Selected.Id));
        }
    }

    /// <summary>Teaches a new spell at runtime (e.g. from a tome pickup or trainer).</summary>
    public void Learn(string spellId)
    {
        if (_spells.Exists(s => s.Id == spellId) || SpellDatabase.Get(spellId) is not { } spell)
        {
            return;
        }

        _spells.Add(spell);
        if (!KnownSpellIds.Contains(spellId))
        {
            KnownSpellIds.Add(spellId);
        }
    }

    public bool CanCast(SpellResource? spell)
    {
        if (spell == null || _stats == null || !_stats.IsAlive)
        {
            return false;
        }

        return CooldownOf(spell) <= 0f && _stats.GetCurrent(StatType.Mana) >= spell.ManaCost;
    }

    /// <summary>Casts the prepared spell. Returns false if none is ready/affordable.</summary>
    public bool TryCast()
    {
        SpellResource? spell = Selected;
        if (!CanCast(spell))
        {
            return false;
        }

        _stats!.ModifyCurrent(StatType.Mana, -spell!.ManaCost);
        _cooldowns[spell.Id] = spell.Cooldown;
        Deliver(spell);

        if (Entity != null)
        {
            EventBus.Instance?.Publish(new SpellCastEvent(Entity, spell.Id));
        }

        return true;
    }

    private void Deliver(SpellResource spell)
    {
        int team = _combat?.Team ?? 0;
        switch (spell.Delivery)
        {
            case SpellDelivery.Self:
                CastSelf(spell);
                break;
            case SpellDelivery.Area:
                CastArea(spell, team);
                break;
            default:
                CastProjectile(spell, team);
                break;
        }
    }

    private DamagePacket BuildPacket(SpellResource spell)
    {
        (float amount, bool isCrit) = CombatMath.RollSpell(spell.BaseDamage, _stats);
        return new DamagePacket(amount, spell.School, Entity, isCrit, SpellPoiseDamage);
    }

    private void CastProjectile(SpellResource spell, int team)
    {
        (Vector3 origin, Vector3 direction) = Aim();
        SpellProjectile projectile = _projectilePool?.Get() ?? new SpellProjectile { Released = ReturnProjectile };

        // Add to the tree (so its visual children build on first use) then position + arm it.
        GetTree().CurrentScene.AddChild(projectile);
        projectile.GlobalPosition = origin;
        projectile.Launch(spell, BuildPacket(spell), Entity, team, direction);
    }

    private void CastArea(SpellResource spell, int team)
    {
        if (Entity?.Body is not Node3D body)
        {
            return;
        }

        Vector3 center = body.GlobalPosition + (Vector3.Up * 1f);
        float radius = spell.ImpactRadius > 0f ? spell.ImpactRadius : DefaultNovaRadius;
        SpellResolver.Detonate(body, spell, BuildPacket(spell), Entity, team, center, radius);
    }

    private void CastSelf(SpellResource spell)
    {
        if (spell.Healing > 0f)
        {
            _stats?.Heal(spell.Healing);
        }

        if (spell.HasStatusEffect)
        {
            Entity?.GetComponent<StatusEffectsComponent>()?
                .Apply(StatusEffectDatabase.Get(spell.StatusEffectId), Entity);
        }
    }

    private (Vector3 Origin, Vector3 Direction) Aim()
    {
        Node3D? node = AimNode ?? Entity?.Body;
        if (node == null)
        {
            return (Vector3.Zero, Vector3.Forward);
        }

        Vector3 forward = (-node.GlobalTransform.Basis.Z).Normalized();
        return (node.GlobalPosition + (forward * MuzzleOffset), forward);
    }

    private void RebuildSpells()
    {
        _spells.Clear();
        foreach (string id in KnownSpellIds)
        {
            if (SpellDatabase.Get(id) is { } spell)
            {
                _spells.Add(spell);
            }
        }

        if (_selected >= _spells.Count)
        {
            _selected = 0;
        }
    }

    // --- ISaveable ----------------------------------------------------------

    public Godot.Collections.Dictionary Save()
    {
        var ids = new Godot.Collections.Array();
        foreach (SpellResource spell in _spells)
        {
            ids.Add(spell.Id);
        }

        return new Godot.Collections.Dictionary
        {
            ["spells"] = ids,
            ["selected"] = _selected,
        };
    }

    public void Load(Godot.Collections.Dictionary data)
    {
        if (data.TryGetValue("spells", out Variant spellsVar))
        {
            KnownSpellIds = new Godot.Collections.Array<string>();
            foreach (Variant entry in spellsVar.AsGodotArray())
            {
                KnownSpellIds.Add(entry.AsString());
            }

            RebuildSpells();
        }

        if (data.TryGetValue("selected", out Variant selectedVar))
        {
            _selected = Mathf.Clamp(selectedVar.AsInt32(), 0, Mathf.Max(0, _spells.Count - 1));
        }
    }
}
