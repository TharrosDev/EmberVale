using Embervale.Combat;
using Embervale.Core.Events;
using Embervale.Entities;
using Embervale.Magic;
using Embervale.Stats;
using Godot;

namespace Embervale.Animation;

/// <summary>
/// Drives a rigged character's <see cref="AnimationPlayer"/> from the existing combat/locomotion
/// state (Phase 30C) — the visuals-only bridge between gameplay components and the 30B/30C glTF
/// clips. Convention over configuration: the body model (under <see cref="BodyMeshPath"/>) ships
/// clips whose names start with <c>idle</c>, <c>run</c>, <c>block</c>, <c>attack</c>, <c>hit</c>
/// and <c>death</c> (loop clips are authored with Godot's <c>-loop</c> suffix); any humanoid using
/// those names gets animation for free (the 30F enemy sets reuse this component).
///
/// Gameplay timing is untouched: hit/attack windows stay owned by <see cref="MeleeWeaponComponent"/>
/// and friends — this component only watches events and per-frame state and plays clips.
/// </summary>
[GlobalClass]
public partial class CharacterAnimationComponent : EntityComponent
{
    /// <summary>Node name of the body visual root the <see cref="AnimationPlayer"/> lives under.</summary>
    [Export] public string BodyMeshPath { get; set; } = "BodyMesh";

    /// <summary>Horizontal speed (m/s) above which locomotion reads as running.</summary>
    [Export] public float RunSpeedThreshold { get; set; } = 0.6f;

    private AnimationPlayer? _player;
    private CombatComponent? _combat;
    private StatsComponent? _stats;
    private SpellcastingComponent? _spellcasting;
    private Skeleton3D? _skeleton;
    private string _idle = "", _run = "", _block = "", _attack = "", _hit = "", _death = "";
    private string _cast = "", _channel = "";
    private bool _deathPlayed;

    protected override void OnInitialize()
    {
        _combat = Entity!.GetComponent<CombatComponent>();
        _stats = Entity.GetComponent<StatsComponent>();

        if (Entity.Body.GetNodeOrNull<Node3D>(BodyMeshPath) is { } bodyRoot)
        {
            _player = FindAnimationPlayer(bodyRoot);
        }

        if (_player != null)
        {
            _idle = ResolveClip("idle");
            _run = ResolveClip("run");
            _block = ResolveClip("block");
            _attack = ResolveClip("attack");
            _hit = ResolveClip("hit");
            _death = ResolveClip("death");
            _cast = ResolveClip("cast");
            _channel = ResolveClip("channel");
        }

        _spellcasting = Entity.GetComponent<SpellcastingComponent>();
        if (Entity.Body.GetNodeOrNull<Node3D>(BodyMeshPath) is { } root)
        {
            _skeleton = FindSkeleton(root);
        }

        EventBus.Instance?.Subscribe<AttackPerformedEvent>(OnAttack);
        EventBus.Instance?.Subscribe<EntityDamagedEvent>(OnDamaged);
        EventBus.Instance?.Subscribe<SpellCastEvent>(OnSpellCast);
    }

    protected override void OnTeardown()
    {
        EventBus.Instance?.Unsubscribe<AttackPerformedEvent>(OnAttack);
        EventBus.Instance?.Unsubscribe<EntityDamagedEvent>(OnDamaged);
        EventBus.Instance?.Unsubscribe<SpellCastEvent>(OnSpellCast);
    }

    private static Skeleton3D? FindSkeleton(Node node)
    {
        if (node is Skeleton3D skeleton)
        {
            return skeleton;
        }

        foreach (Node child in node.GetChildren())
        {
            if (FindSkeleton(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>The 30E cast beat: play the cast thrust and pop a school-tinted flash at the
    /// casting hand. A channeled spell publishes a cast event per tick — the sustained
    /// channel-loop pose covers those, so per-tick one-shots/flashes are skipped.</summary>
    private void OnSpellCast(SpellCastEvent e)
    {
        if (!ReferenceEquals(e.Caster, Entity) || _spellcasting is { IsChanneling: true })
        {
            return;
        }

        PlayOneShot(_cast);

        if (SpellDatabase.Get(e.SpellId) is { } spell)
        {
            var flash = new SpellFlash
            {
                Radius = 0.5f,
                FlashColor = SpellSchools.Color(spell.School),
            };
            Entity!.Body.GetTree().CurrentScene.AddChild(flash);
            flash.GlobalPosition = CastingHandPosition();
        }
    }

    /// <summary>World position of the left (casting) hand bone, falling back to chest height.</summary>
    private Vector3 CastingHandPosition()
    {
        if (_skeleton != null)
        {
            for (int i = 0; i < _skeleton.GetBoneCount(); i++)
            {
                string name = _skeleton.GetBoneName(i);
                if (name.Contains("hand", System.StringComparison.OrdinalIgnoreCase) &&
                    name.EndsWith("L", System.StringComparison.OrdinalIgnoreCase))
                {
                    return (_skeleton.GlobalTransform * _skeleton.GetBoneGlobalPose(i)).Origin;
                }
            }
        }

        return Entity!.Body.GlobalPosition + (Vector3.Up * 1.3f);
    }

    private static AnimationPlayer? FindAnimationPlayer(Node node)
    {
        if (node is AnimationPlayer player)
        {
            return player;
        }

        foreach (Node child in node.GetChildren())
        {
            if (FindAnimationPlayer(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>The imported clip whose name starts with <paramref name="prefix"/> ("" if absent) —
    /// tolerant of the importer keeping or stripping the authored <c>-loop</c> suffix.</summary>
    private string ResolveClip(string prefix)
    {
        foreach (string name in _player!.GetAnimationList())
        {
            if (name.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
            {
                return name;
            }
        }

        return string.Empty;
    }

    private void OnAttack(AttackPerformedEvent e)
    {
        if (ReferenceEquals(e.Attacker, Entity))
        {
            PlayOneShot(_attack);
        }
    }

    private void OnDamaged(EntityDamagedEvent e)
    {
        // A blocked/absorbed poke shouldn't flinch through a block pose; death owns the rest.
        if (ReferenceEquals(e.Entity, Entity) && e.RemainingHealth > 0f && _combat is not { IsBlocking: true })
        {
            PlayOneShot(_hit);
        }
    }

    private void PlayOneShot(string clip)
    {
        if (_player != null && clip.Length > 0 && !_deathPlayed)
        {
            _player.Play(clip);
        }
    }

    public override void _Process(double delta)
    {
        if (_player == null)
        {
            return;
        }

        // Death latches until the entity is alive again (respawn), then control resumes.
        if (_stats is { IsAlive: false })
        {
            if (!_deathPlayed && _death.Length > 0)
            {
                _player.Play(_death);
                _deathPlayed = true;
            }

            return;
        }

        _deathPlayed = false;

        // Let one-shots (attack/hit/cast) finish before locomotion reclaims the player.
        if (_player.IsPlaying() &&
            (_player.CurrentAnimation == _attack || _player.CurrentAnimation == _hit ||
             _player.CurrentAnimation == _cast))
        {
            return;
        }

        string next =
            _spellcasting is { IsCharging: true } or { IsChanneling: true } && _channel.Length > 0 ? _channel
            : _combat is { IsBlocking: true } && _block.Length > 0 ? _block
            : HorizontalSpeed() > RunSpeedThreshold && _run.Length > 0 ? _run
            : _idle;

        if (next.Length > 0 && _player.CurrentAnimation != next)
        {
            _player.Play(next, customBlend: 0.15);
        }
    }

    private float HorizontalSpeed()
    {
        if (Entity?.Body is CharacterBody3D body)
        {
            Vector3 v = body.Velocity;
            return new Vector2(v.X, v.Z).Length();
        }

        return 0f;
    }
}
