using Embervale.Combat;
using Embervale.Core.Diagnostics;
using Embervale.Core.Events;
using Embervale.Entities;
using Embervale.Stats;
using Godot;

namespace Embervale.Enemies;

/// <summary>
/// Drives the boss's <b>phases</b> and <b>attack telegraphs</b> (Phase 28B) on top of the shared
/// <see cref="EnemyAIComponent"/> + <see cref="MeleeWeaponComponent"/>. Two ideas, both data-light:
///
/// 1. <b>Phases.</b> As the boss's health crosses thresholds (66% / 33%) it escalates — each phase
///    stacks attack-speed + move-speed modifiers so the later thirds of the fight are visibly more
///    relentless. A <see cref="BossPhaseChangedEvent"/> is published for the healthbar / cinematics
///    (28C) and the future <c>BossController</c> generalisation (Phase 36).
/// 2. <b>Telegraphs.</b> Every swing (an <see cref="AttackPerformedEvent"/> from this boss) flares the
///    body's emissive glow during the wind-up and fades it over the swing, so heavy hits are readable
///    ("no button-mashing") — and the flare burns brighter in later phases.
///
/// The generalizable bits for Phase 36: the HP-threshold→profile table and the publish-on-transition
/// event; the telegraph is a presentation hook any attack wind-up can drive.
/// </summary>
[GlobalClass]
public partial class BossController : EntityComponent
{
    private const int TotalPhases = 3;
    private const float TelegraphDuration = 0.5f;
    private static readonly Color WarnColor = new(1.0f, 0.25f, 0.05f);

    private StatsComponent? _stats;
    private StandardMaterial3D? _mat;
    private Color _baseColor = new(0.85f, 0.32f, 0.10f);
    private float _baseEmission = 0.5f;

    private int _phase = 1;
    private float _telegraph;

    protected override void OnInitialize()
    {
        ProcessMode = ProcessModeEnum.Pausable;
        _stats = Entity!.GetComponent<StatsComponent>();

        if (Entity!.Body.GetNodeOrNull<MeshInstance3D>("Mesh")?.MaterialOverride is StandardMaterial3D mat)
        {
            _mat = mat;
            _baseColor = mat.Emission;
            _baseEmission = mat.EmissionEnergyMultiplier;
        }

        EventBus.Instance?.Subscribe<DamageDealtEvent>(OnDamage);
        EventBus.Instance?.Subscribe<AttackPerformedEvent>(OnAttack);
    }

    protected override void OnTeardown()
    {
        EventBus.Instance?.Unsubscribe<DamageDealtEvent>(OnDamage);
        EventBus.Instance?.Unsubscribe<AttackPerformedEvent>(OnAttack);
    }

    public override void _Process(double delta)
    {
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
        if (!ReferenceEquals(e.Target, Entity) || _stats == null)
        {
            return;
        }

        float max = _stats.GetValue(StatType.Health);
        if (max <= 0f)
        {
            return;
        }

        float fraction = _stats.GetCurrent(StatType.Health) / max;
        int target = fraction <= 0.33f ? 3 : fraction <= 0.66f ? 2 : 1;
        while (_phase < target)
        {
            EnterPhase(_phase + 1);
        }
    }

    private void EnterPhase(int phase)
    {
        _phase = phase;

        // Each phase stacks a little more aggression. Distinct sources so they accumulate.
        (float atk, float move) = phase switch
        {
            2 => (0.25f, 0.15f),
            3 => (0.30f, 0.20f),
            _ => (0f, 0f),
        };
        if (_stats != null && atk > 0f)
        {
            // Remove-then-add so re-entering a phase (encounter restart / reload) can't stack the modifier.
            string source = $"boss.phase{phase}";
            Stat attackSpeed = _stats.GetStat(StatType.AttackSpeed);
            Stat moveSpeed = _stats.GetStat(StatType.MoveSpeed);
            attackSpeed.RemoveModifiersFromSource(source);
            attackSpeed.AddModifier(new StatModifier(atk, ModifierType.PercentMult, source));
            moveSpeed.RemoveModifiersFromSource(source);
            moveSpeed.AddModifier(new StatModifier(move, ModifierType.PercentMult, source));
        }

        EventBus.Instance?.Publish(new BossPhaseChangedEvent(Entity!, phase, TotalPhases));
        Log.Info($"The Iron King enters phase {phase}/{TotalPhases} — his blows come faster.");
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
        if (_mat == null)
        {
            return;
        }

        // Brighter, redder flare in later phases so the escalation reads at a glance.
        float peak = _phase >= 3 ? 5.5f : _phase == 2 ? 3.5f : 2.5f;
        _mat.EmissionEnergyMultiplier = Mathf.Lerp(_baseEmission, peak, _telegraph);
        _mat.Emission = _baseColor.Lerp(WarnColor, Mathf.Clamp(_telegraph * (0.3f + (0.2f * _phase)), 0f, 1f));
    }
}
