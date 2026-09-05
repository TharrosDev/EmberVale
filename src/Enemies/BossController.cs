using System.Collections.Generic;
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
/// on top of the shared <see cref="EnemyAIComponent"/> + <see cref="Combat.CharacterActionComponent"/> —
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
    private CombatComponent? _combat;
    private TelegraphComponent? _telegraph;
    private StandardMaterial3D? _mat;
    private Color _baseColor = new(0.85f, 0.32f, 0.10f);
    private float _baseEmission = 0.5f;

    private int _phase = 1;
    private float _telegraphFlare;

    /// <summary>Seconds the current wind-up lasts, taken from the attack event rather than assumed.
    /// A fixed constant drifted from the real window the moment a phase buffed attack speed — the
    /// flare outlasted the blow it was warning about, which is worse than not warning at all.</summary>
    private float _telegraphSeconds = 0.5f;
    private bool _engaged;
    private double _fightElapsed;
    private bool _enraged;

    /// <summary>Adds summoned by this fight, per wave. Held so a repeat can top the fight up to the
    /// wave's cap rather than stack on it, and so they can be cleared when the boss falls. Godot
    /// nodes, so every read filters through IsInstanceValid — a held list is not a list of live
    /// objects, which is a lesson this codebase has already paid for once.</summary>
    private readonly Dictionary<BossAddWaveResource, List<EnemyEntity>> _adds = new();

    /// <summary>Seconds until each repeating wave's next summon.</summary>
    private readonly Dictionary<BossAddWaveResource, double> _waveTimers = new();

    /// <summary>Scratch list of add-waves due to fire this tick, reused every frame (see
    /// <see cref="TickAddWaves"/>). Keeps Summon out of the dictionary's enumeration.</summary>
    private readonly List<BossAddWaveResource> _dueWaves = new();

    /// <summary>The group an arena's spawn markers declare themselves with, in its own .tscn.</summary>
    public const string SpawnMarkerGroup = "boss_add_spawn";

    /// <summary>Phases in this fight, for the healthbar and the phase-changed event.</summary>
    public int TotalPhases => Mathf.Max(1, _boss.Phases.Count);

    /// <summary>This fight's authored data, so the encounter director can read the dead boss's own
    /// intro/defeat/reward config rather than one boss's constants.</summary>
    public BossResource Fight => _boss;

    private bool _encounterBegun;

    /// <summary>
    /// Announces the fight — once. The brazier calls it right after summoning so the Iron King keeps
    /// his entrance, and <see cref="OnDamage"/> calls it on the first blow traded so a lair boss,
    /// which nobody summons, still gets an intro lock and a healthbar. Whichever happens first wins.
    /// </summary>
    public void BeginEncounter()
    {
        if (_encounterBegun || Entity == null)
        {
            return;
        }

        _encounterBegun = true;
        EventBus.Instance?.Publish(
            new BossEncounterStartedEvent(Entity, Entity.DisplayName, TotalPhases));
    }

    protected override void OnInitialize()
    {
        ProcessMode = ProcessModeEnum.Pausable;
        _boss = ResolveBoss();
        _stats = Entity!.GetComponent<StatsComponent>();
        _casting = Entity.GetComponent<SpellcastingComponent>();
        _ai = Entity.GetComponent<EnemyAIComponent>();
        _combat = Entity.GetComponent<CombatComponent>();
        _telegraph = Entity.GetComponent<TelegraphComponent>();

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

        // Phase one is never "entered" — AdvanceTo only ever steps up from it — so its colour and
        // wind-up vulnerability have to be pushed here or the opening stage would run on defaults.
        ApplyPhasePresentation();

        EventBus.Instance?.Subscribe<DamageDealtEvent>(OnDamage);
        EventBus.Instance?.Subscribe<AttackPerformedEvent>(OnAttack);
        EventBus.Instance?.Subscribe<AttackInterruptedEvent>(OnInterrupted);
        EventBus.Instance?.Subscribe<EntityDiedEvent>(OnDied);
    }

    protected override void OnTeardown()
    {
        EventBus.Instance?.Unsubscribe<DamageDealtEvent>(OnDamage);
        EventBus.Instance?.Unsubscribe<AttackPerformedEvent>(OnAttack);
        EventBus.Instance?.Unsubscribe<AttackInterruptedEvent>(OnInterrupted);
        EventBus.Instance?.Unsubscribe<EntityDiedEvent>(OnDied);
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
        // A corpse escalates nothing. The body outlives the death by the AI profile's DespawnDelay
        // (4 s by default), and this component ticks for every frame of it — long enough for a fuse
        // that was about to blow to blow, buffing a dead boss into its final phase and calling in
        // that phase's adds. The dragons all carry one (135–240 s), so it is a real window, not a
        // theoretical one.
        if (Defeated)
        {
            return;
        }

        TickEnrage(delta);
        TickAddWaves(delta);

        if (_telegraphFlare <= 0f)
        {
            return;
        }

        _telegraphFlare = Mathf.Max(0f, _telegraphFlare - ((float)delta / _telegraphSeconds));
        ApplyTelegraph();
    }

    // --- Phases -------------------------------------------------------------

    /// <summary>
    /// Whether this fight is over. Read before any escalation, because <b>the killing blow arrives
    /// here after the death</b>: <see cref="Stats.StatsComponent.ApplyDamage"/> publishes
    /// <c>EntityDiedEvent</c> from inside itself, and <see cref="Combat.CombatComponent"/> publishes
    /// <c>DamageDealtEvent</c> only once it returns — so <see cref="OnDied"/> runs first and
    /// <see cref="OnDamage"/> then sees a boss at zero health.
    ///
    /// Untreated, that ordering made every finisher summon the phase it skipped into. A boss killed
    /// from phase 2 always crosses the phase-3 threshold on the way to zero, so the Iron King's death
    /// blow entered phase 3 <em>after</em> <see cref="ClearAdds"/> had run: two cultists spawned out
    /// of the corpse on a 22 s repeat with nothing left to clear them, the arena's ember vents
    /// re-lit off the phase-changed event that had just hidden them, and the log announced an
    /// escalation for a boss that was already dead.
    ///
    /// <see cref="Combat.CombatComponent"/> guards the same hazard the same way one file over ("a
    /// kill blow doesn't also stagger the corpse"), so this is that idiom rather than a new one.
    /// </summary>
    private bool Defeated => _stats is { IsAlive: false };

    private void OnDamage(DamageDealtEvent e)
    {
        if (Defeated)
        {
            return;
        }

        // Either direction counts as engagement: a boss the player is chipping at from range and one
        // that has landed the first blow are both in a fight, and both fuses should be burning.
        if (ReferenceEquals(e.Source, Entity) || ReferenceEquals(e.Target, Entity))
        {
            _engaged = true;
            BeginEncounter();
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

        SummonWaves(definition);
        ApplyPhasePresentation();
        ApplyTelegraph();
        EventBus.Instance?.Publish(new BossPhaseChangedEvent(Entity!, phase, TotalPhases));
        Log.Info($"{Entity!.DisplayName} enters phase {phase}/{TotalPhases} — the fight escalates.");
    }

    /// <summary>Pushes the current phase's presentation and vulnerability outward: the ring takes
    /// its colour, and the combat component takes the wind-up poise multiplier — that component is
    /// where incoming poise resolves, so it is where the knob has to land.</summary>
    private void ApplyPhasePresentation()
    {
        if (_boss.Phases.Count < _phase || _boss.Phases[_phase - 1] is not { } definition)
        {
            return;
        }

        if (_telegraph != null)
        {
            _telegraph.RingColor = definition.TelegraphColor;
        }

        if (_combat != null)
        {
            _combat.WindupPoiseMultiplier = definition.WindupPoiseMultiplier;
        }

        // A phase may fight with its own moves (§20). Empty clears the override rather than leaving
        // the previous phase's set in place — a boss that escalates must be able to de-escalate on a
        // reload, and a stale override would outlive the phase that set it.
        if (Entity?.GetComponent<Combat.Actions.CharacterActionComponent>()?.Weapon is { } weapon)
        {
            var attacks = new Combat.Actions.ActionDefinitionResource[definition.Attacks.Count];
            for (int i = 0; i < definition.Attacks.Count; i++)
            {
                attacks[i] = definition.Attacks[i];
            }

            weapon.PhaseOverride = attacks.Length > 0 ? attacks : null;
        }
    }

    /// <summary>The boss falling ends the fight, adds included.</summary>
    private void OnDied(EntityDiedEvent e)
    {
        if (ReferenceEquals(e.Entity, Entity))
        {
            ClearAdds();
        }
    }

    // --- Adds ---------------------------------------------------------------

    /// <summary>Brings in every wave this phase authors, and arms the repeating ones.</summary>
    private void SummonWaves(BossPhaseResource definition)
    {
        foreach (BossAddWaveResource wave in definition.AddWaves)
        {
            if (wave == null || string.IsNullOrEmpty(wave.TemplateId))
            {
                continue;
            }

            Summon(wave);
            if (wave.RepeatSeconds > 0f)
            {
                _waveTimers[wave] = wave.RepeatSeconds;
            }
        }
    }

    private void TickAddWaves(double delta)
    {
        if (_waveTimers.Count == 0)
        {
            return;
        }

        // Reusable buffer, not a fresh List every frame: this ticks for the whole fight the moment any
        // phase authors a repeating wave, so the old snapshot was steady garbage during the one
        // encounter that can least afford a GC hitch. Same fix as SpellcastingComponent._expiring.
        //
        // Updating an existing value in place mid-enumeration is fine on .NET 8, and resetting a timer
        // is all this loop does to the dictionary — but Summon spawns actors and is deferred out of the
        // enumeration anyway, so nothing it might reach can invalidate the enumerator.
        _dueWaves.Clear();
        foreach (KeyValuePair<BossAddWaveResource, double> entry in _waveTimers)
        {
            double remaining = entry.Value - delta;
            if (remaining > 0d)
            {
                _waveTimers[entry.Key] = remaining;
            }
            else
            {
                _dueWaves.Add(entry.Key);
            }
        }

        foreach (BossAddWaveResource wave in _dueWaves)
        {
            _waveTimers[wave] = wave.RepeatSeconds;
            Summon(wave);
        }
    }

    /// <summary>
    /// Spawns as much of <paramref name="wave"/> as its cap has room for, at the arena's declared
    /// markers or — a lair has none — on a ring around the boss.
    /// </summary>
    private void Summon(BossAddWaveResource wave)
    {
        if (Entity?.Body is not Node3D body || body.GetParent() is not Node arena)
        {
            return;
        }

        List<EnemyEntity> live = LiveAdds(wave);
        int count = BossAdds.SummonCount(wave.Count, live.Count, wave.MaxAlive);
        if (count <= 0)
        {
            return;
        }

        List<Node3D> markers = SpawnMarkers(arena);
        for (int i = 0; i < count; i++)
        {
            // Create at zero, add, THEN place (CLAUDE.md): a cell root has already been moved to the
            // cell's centre, so handing a world position to Create applies that offset twice — the
            // bug that put 35D's dragon in the void.
            EnemyEntity add = EnemyTemplateRegistry.Create(wave.TemplateId, Vector3.Zero);
            arena.AddChild(add);
            add.GlobalPosition = markers.Count > 0
                ? markers[(live.Count + i) % markers.Count].GlobalPosition
                : body.GlobalPosition + BossAdds.SpawnSlot(i, count, RingRadius);

            EnemyScaling.ApplyHealthMultiplier(add, wave.HealthMultiplier, "boss.add");
            live.Add(add);
        }

        Log.Info($"{Entity!.DisplayName} calls {count}x {wave.TemplateId}.");
    }

    /// <summary>Radius of the fallback ring — outside the boss's own body, inside its reach.</summary>
    private const float RingRadius = 4.5f;

    /// <summary>This wave's still-living adds, pruned of anything freed or dead.</summary>
    private List<EnemyEntity> LiveAdds(BossAddWaveResource wave)
    {
        if (!_adds.TryGetValue(wave, out List<EnemyEntity>? list))
        {
            list = new List<EnemyEntity>();
            _adds[wave] = list;
        }

        list.RemoveAll(add => !GodotObject.IsInstanceValid(add) ||
            add.GetComponent<StatsComponent>() is { IsAlive: false });
        return list;
    }

    /// <summary>
    /// The arena's declared spawn markers: nodes in <see cref="SpawnMarkerGroup"/> that sit under
    /// the same parent this boss does. Scoped by ancestry rather than by distance so two loaded
    /// arenas can never borrow each other's markers, and by group rather than by node path so
    /// renaming a marker in the scene cannot silently unbind it.
    /// </summary>
    private List<Node3D> SpawnMarkers(Node arena)
    {
        var markers = new List<Node3D>();
        foreach (Node node in GetTree().GetNodesInGroup(SpawnMarkerGroup))
        {
            if (node is Node3D marker && arena.IsAncestorOf(marker))
            {
                markers.Add(marker);
            }
        }

        return markers;
    }

    /// <summary>
    /// Kills every add the moment the boss falls, through the ordinary damage path so they still
    /// drop loot and grant XP for the damage the player already did. Despawning them silently would
    /// take that value back; leaving them alive would mean chasing minions round an empty arena for
    /// a reward that has already been earned.
    /// </summary>
    private void ClearAdds()
    {
        foreach (List<EnemyEntity> list in _adds.Values)
        {
            foreach (EnemyEntity add in list)
            {
                if (GodotObject.IsInstanceValid(add) &&
                    add.GetComponent<StatsComponent>() is { IsAlive: true } stats)
                {
                    stats.ApplyDamage(stats.GetValue(StatType.Health) * 2f, Entity);
                }
            }

            list.Clear();
        }

        _waveTimers.Clear();
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
            _telegraphSeconds = Mathf.Max(0.05f, e.WindupSeconds);
            _telegraphFlare = 1f;
            ApplyTelegraph();
        }
    }

    /// <summary>A punished wind-up drops the flare with the blow, so the two telegraphs (this and
    /// the ground ring) end together and the interrupt reads as the win it is.</summary>
    private void OnInterrupted(AttackInterruptedEvent e)
    {
        if (ReferenceEquals(e.Attacker, Entity))
        {
            _telegraphFlare = 0f;
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
        _mat.EmissionEnergyMultiplier = Mathf.Lerp(_baseEmission, definition.TelegraphEnergy, _telegraphFlare);
        _mat.Emission = _baseColor.Lerp(
            definition.TelegraphColor, Mathf.Clamp(_telegraphFlare * (0.3f + (0.2f * _phase)), 0f, 1f));
    }
}
