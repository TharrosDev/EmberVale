using Embervale.Combat;
using Embervale.Core.Diagnostics;
using Embervale.Core.Events;
using Embervale.Entities;
using Embervale.Magic;
using Embervale.Stats;
using Godot;

namespace Embervale.Enemies;

/// <summary>
/// Runs a boss fight's <b>phases</b>, <b>per-phase abilities</b>, <b>enrage</b> and <b>telegraphs</b>
/// on top of the shared <see cref="EnemyAIComponent"/> + <see cref="Combat.MeleeWeaponComponent"/> —
/// no AI rewrite, the boss fights with the same brain everything else does.
///
/// <b>Phase 36A:</b> all of it is now authored data. A <see cref="BossResource"/> named by
/// <see cref="BossId"/> carries the HP thresholds, the escalation per stage, the spells a stage
/// hands over, the telegraph colours and the enrage fuse. Phase 28B's hard-coded 66/33 table lives
/// on only as <see cref="FallbackBoss"/>, so a boss whose id is missing or misspelled still fights
/// in three escalating stages instead of degrading to a large enemy with a healthbar.
///
/// The four ideas, in the order they matter:
/// 1. <b>Phases.</b> Health crossing a threshold enters a stage and never leaves it — bosses
///    escalate, they do not calm down. A <see cref="BossPhaseChangedEvent"/> goes out for the
///    healthbar (28C) and cinematics (Phase 43).
/// 2. <b>Abilities.</b> A stage can grant spells, so "phase three breathes fire" is data.
/// 3. <b>Enrage.</b> A fuse lit by the first damage traded, so a boss cannot be out-waited. It is
///    deliberately not lit by <c>BossEncounterStartedEvent</c> — only <c>BossSummonComponent</c>
///    publishes that (the Iron King's path), so every lair boss would have a fuse that never caught.
/// 4. <b>Telegraphs.</b> Each swing flares the body's emissive during the wind-up and fades it over
///    the swing, so heavy hits are readable ("no button-mashing"), hotter in later stages.
/// </summary>
[GlobalClass]
public partial class BossController : EntityComponent
{
    private const float TelegraphDuration = 0.5f;

    /// <summary>Which <see cref="BossResource"/> drives this fight (see <c>data/bosses/</c>).</summary>
    [Export] public string BossId { get; set; } = string.Empty;

    /// <summary>Directly-assigned resource, for tests and scenes that would rather inline it than go
    /// through the database. Wins over <see cref="BossId"/> when set — mirrors
    /// <see cref="EnemyAIComponent.Profile"/>.</summary>
    [Export] public BossResource? Boss { get; set; }

    private BossResource _boss = null!;
    private StatsComponent? _stats;
    private SpellcastingComponent? _casting;
    private EnemyAIComponent? _ai;
    private StandardMaterial3D? _mat;
    private Color _baseColor = new(0.85f, 0.32f, 0.10f);
    private float _baseEmission = 0.5f;

    private int _phase = 1;
    private float _telegraph;
    private bool _engaged;
    private double _fightElapsed;
    private bool _enraged;

    /// <summary>Phases in this fight, for the healthbar and the phase-changed event.</summary>
    public int TotalPhases => Mathf.Max(1, _boss.Phases.Count);

    protected override void OnInitialize()
    {
        ProcessMode = ProcessModeEnum.Pausable;
        _boss = ResolveBoss();
        _stats = Entity!.GetComponent<StatsComponent>();
        _casting = Entity.GetComponent<SpellcastingComponent>();
        _ai = Entity.GetComponent<EnemyAIComponent>();

        // The phase/telegraph glow drives the boss's emissive material: the stand-in capsule's
        // MaterialOverride, or (30D model) the first emissive surface under the "Mesh" scene root
        // (the Iron King's ember core/eyes), claimed as a unique copy.
        if (Entity!.Body.GetNodeOrNull<Node3D>("Mesh") is { } visual)
        {
            _mat = visual is MeshInstance3D { MaterialOverride: StandardMaterial3D overrideMat }
                ? overrideMat
                : ClaimEmissiveSurface(visual);
        }

        if (_mat != null)
        {
            _baseColor = _mat.Emission;
            _baseEmission = _mat.EmissionEnergyMultiplier;
        }

        EventBus.Instance?.Subscribe<DamageDealtEvent>(OnDamage);
        EventBus.Instance?.Subscribe<AttackPerformedEvent>(OnAttack);
    }

    protected override void OnTeardown()
    {
        EventBus.Instance?.Unsubscribe<DamageDealtEvent>(OnDamage);
        EventBus.Instance?.Unsubscribe<AttackPerformedEvent>(OnAttack);
    }

    /// <summary>An inline <see cref="Boss"/> wins; otherwise the id is looked up. A miss warns and
    /// falls back to the Phase 28B table, so a content typo costs the authored numbers, not the
    /// fight's structure.</summary>
    private BossResource ResolveBoss()
    {
        if (Boss != null)
        {
            return Boss;
        }

        if (!string.IsNullOrEmpty(BossId) && BossDatabase.Get(BossId) is { } found)
        {
            return found;
        }

        if (!string.IsNullOrEmpty(BossId))
        {
            Log.Warn($"Boss '{BossId}' is not registered; falling back to the default three-phase fight.");
        }

        return FallbackBoss();
    }

    /// <summary>Phase 28B's original hard-coded fight, kept as the safety net described above.</summary>
    private static BossResource FallbackBoss() => new()
    {
        Id = "boss.fallback",
        Phases = new Godot.Collections.Array<BossPhaseResource>
        {
            new() { HealthFraction = 1f, TelegraphEnergy = 2.5f },
            new() { HealthFraction = 0.66f, AttackSpeedBonus = 0.25f, MoveSpeedBonus = 0.15f, TelegraphEnergy = 3.5f },
            new() { HealthFraction = 0.33f, AttackSpeedBonus = 0.30f, MoveSpeedBonus = 0.20f, TelegraphEnergy = 5.5f },
        },
    };

    /// <summary>The first emission-enabled surface material under <paramref name="node"/>, replaced
    /// with a uniquely-owned duplicate this controller may animate freely.</summary>
    private static StandardMaterial3D? ClaimEmissiveSurface(Node node)
    {
        if (node is MeshInstance3D mesh && mesh.Mesh is { } res)
        {
            for (int i = 0; i < res.GetSurfaceCount(); i++)
            {
                if (mesh.GetActiveMaterial(i) is StandardMaterial3D { EmissionEnabled: true } m)
                {
                    var owned = (StandardMaterial3D)m.Duplicate();
                    mesh.SetSurfaceOverrideMaterial(i, owned);
                    return owned;
                }
            }
        }

        foreach (Node child in node.GetChildren())
        {
            if (ClaimEmissiveSurface(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    public override void _Process(double delta)
    {
        TickEnrage(delta);

        if (_telegraph <= 0f)
        {
            return;
        }

        _telegraph = Mathf.Max(0f, _telegraph - (float)delta / TelegraphDuration);
        ApplyTelegraph();
    }

    // --- Phases -------------------------------------------------------------

    private void OnDamage(DamageDealtEvent e)
    {
        // Either direction counts as engagement: a boss the player is chipping at from range and one
        // that has landed the first blow are both in a fight, and both fuses should be burning.
        if (ReferenceEquals(e.Source, Entity) || ReferenceEquals(e.Target, Entity))
        {
            _engaged = true;
        }

        if (!ReferenceEquals(e.Target, Entity) || _stats == null)
        {
            return;
        }

        float max = _stats.GetValue(StatType.Health);
        if (max <= 0f)
        {
            return;
        }

        AdvanceTo(BossPhases.SelectPhase(_stats.GetCurrent(StatType.Health) / max, Thresholds()));
    }

    /// <summary>Authored thresholds, high to low. Materialized per call rather than cached because a
    /// boss takes a handful of hits a second, not a thousand.</summary>
    private float[] Thresholds()
    {
        var thresholds = new float[_boss.Phases.Count];
        for (int i = 0; i < _boss.Phases.Count; i++)
        {
            thresholds[i] = _boss.Phases[i].HealthFraction;
        }

        return thresholds;
    }

    /// <summary>Steps up to <paramref name="target"/>, entering every stage on the way so a hit that
    /// crosses two thresholds still applies both stages' escalation and abilities. Never steps down.</summary>
    private void AdvanceTo(int target)
    {
        while (_phase < target && _phase < _boss.Phases.Count)
        {
            EnterPhase(_phase + 1);
        }
    }

    private void EnterPhase(int phase)
    {
        _phase = phase;
        if (_boss.Phases.Count < phase || _boss.Phases[phase - 1] is not { } definition)
        {
            return;
        }

        // Remove-then-add under a per-phase source so re-entering a phase (encounter restart,
        // reload) can never stack the modifier.
        ApplyBonuses($"boss.phase{phase}", definition.AttackSpeedBonus, definition.MoveSpeedBonus);
        Grant(definition.GrantSpellIds);

        if (!string.IsNullOrEmpty(definition.AiProfileId) && _ai != null)
        {
            _ai.ProfileId = definition.AiProfileId;
        }

        ApplyTelegraph();
        EventBus.Instance?.Publish(new BossPhaseChangedEvent(Entity!, phase, TotalPhases));
        Log.Info($"{Entity!.DisplayName} enters phase {phase}/{TotalPhases} — the fight escalates.");
    }

    // --- Enrage -------------------------------------------------------------

    private void TickEnrage(double delta)
    {
        // Most bosses author no fuse (the Iron King among them), so bail before touching the clock
        // rather than accumulating a double every frame for a comparison that can never be true.
        if (_boss.EnrageSeconds <= 0f || !_engaged || _enraged)
        {
            return;
        }

        _fightElapsed += delta;
        if (!BossPhases.ShouldEnrage(_fightElapsed, _boss.EnrageSeconds, _enraged))
        {
            return;
        }

        _enraged = true;
        ApplyBonuses("boss.enrage", _boss.EnrageAttackSpeedBonus, _boss.EnrageMoveSpeedBonus);
        Grant(_boss.EnrageSpellIds);

        // Finishing in the opening stance would undercut the whole point of a fuse.
        if (_boss.EnrageForcesFinalPhase)
        {
            AdvanceTo(_boss.Phases.Count);
        }

        Log.Info($"{Entity!.DisplayName} enrages after {_boss.EnrageSeconds:0}s — the fight will not be waited out.");
    }

    // --- Shared application -------------------------------------------------

    private void ApplyBonuses(string source, float attackSpeed, float moveSpeed)
    {
        if (_stats == null)
        {
            return;
        }

        Apply(_stats.GetStat(StatType.AttackSpeed), attackSpeed);
        Apply(_stats.GetStat(StatType.MoveSpeed), moveSpeed);

        void Apply(Stat stat, float bonus)
        {
            stat.RemoveModifiersFromSource(source);
            if (bonus != 0f)
            {
                stat.AddModifier(new StatModifier(bonus, ModifierType.PercentMult, source));
            }
        }
    }

    /// <summary>Hands spells to the boss through the same grant path a dialogue reward uses, which
    /// ignores <c>PlayerLearnable</c> — exactly what a monster-only spell needs. A boss with no
    /// <c>SpellcastingComponent</c> silently keeps its melee, which is the sane read of an archetype
    /// that authored abilities but no caster.</summary>
    private void Grant(Godot.Collections.Array<string> spellIds)
    {
        if (_casting == null)
        {
            return;
        }

        foreach (string id in spellIds)
        {
            if (!string.IsNullOrEmpty(id))
            {
                _casting.Learn(id);
            }
        }
    }

    // --- Telegraph ----------------------------------------------------------

    private void OnAttack(AttackPerformedEvent e)
    {
        if (ReferenceEquals(e.Attacker, Entity))
        {
            _telegraph = 1f;
            ApplyTelegraph();
        }
    }

    private void ApplyTelegraph()
    {
        if (_mat == null || _boss.Phases.Count < _phase)
        {
            return;
        }

        BossPhaseResource definition = _boss.Phases[_phase - 1];
        _mat.EmissionEnergyMultiplier = Mathf.Lerp(_baseEmission, definition.TelegraphEnergy, _telegraph);
        _mat.Emission = _baseColor.Lerp(
            definition.TelegraphColor, Mathf.Clamp(_telegraph * (0.3f + (0.2f * _phase)), 0f, 1f));
    }
}
