using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Embervale.Combat;
using Embervale.Combat.Actions;
using Embervale.Core.Diagnostics;
using Embervale.Core.Events;
using Embervale.Enemies;
using Embervale.Entities;
using Embervale.Magic;
using Embervale.Magic.Vfx;
using Embervale.Player;
using Godot;

namespace Embervale.Debugging;

/// <summary>
/// The spell screenshot harness: <c>godot --path . -- --spellshots</c>. Every spell in
/// <c>data/spells</c> is cast for real on a level strip of ground near where the save stands, at dusk,
/// and photographed four times: <c>windup</c>, <c>release</c> (the bolt in flight, the telegraph on the
/// ground, the first frame of a burst), <c>impact</c> and <c>linger</c>. Files are
/// <c>&lt;spell&gt;_&lt;view&gt;_&lt;phase&gt;.png</c>. Run WITHOUT <c>--headless</c>.
///
/// <para>A player spell is cast by the player at three practice targets, through the cast button the
/// input router reads, so the camera frames it as it does in play. All of them are taken in third
/// person (<c>tp</c>), a few per school and every self-cast in first (<c>fp</c>), and a handful again
/// at midday (<c>tpday</c>). An enemy-only spell is cast at the player by the creature that owns it,
/// in both views. A few of the player's own spells are also cast AT the first-person player by a
/// humanoid enemy (<c>efp</c>): a bolt, a nova and a channel arriving at the camera.</para>
///
/// <para>Environment: <c>EMBERVALE_SPELLSHOTS_FILTER</c> (comma list of spell ids or school names;
/// default all), <c>EMBERVALE_SPELLSHOTS_VIEW</c> (<c>tp</c>, <c>fp</c> or <c>both</c>),
/// <c>EMBERVALE_SPELLSHOTS_TIER</c> (<c>performance|low|medium|high|ultra</c>),
/// <c>EMBERVALE_SPELLSHOTS_REDUCED</c> (<c>1</c> = reduced motion), <c>EMBERVALE_SPELLSHOTS_HOUR</c>
/// (the dusk hour, default 19.5) and <c>EMBERVALE_SPELLSHOTS_BACKDROP</c> (<c>0</c> removes the dark
/// wall stood behind the targets). Settings are changed live and never saved; autosaves are off.</para>
///
/// <para>Every shot logs how many nodes the effect director has under <c>VfxRoot</c>, and the run
/// FAILS when a spell with an effect recipe added nothing there from cast to linger: a regression to
/// "nothing drew" shows in the log and the exit code without anyone opening a PNG.</para>
/// </summary>
public sealed partial class SpellShots : TimedShots
{
    /// <summary>Narrows the run to spell ids and school names, comma separated.</summary>
    public const string FilterVariable = "EMBERVALE_SPELLSHOTS_FILTER";

    private const string ViewVariable = "EMBERVALE_SPELLSHOTS_VIEW";
    private const string HourVariable = "EMBERVALE_SPELLSHOTS_HOUR";
    private const string BackdropVariable = "EMBERVALE_SPELLSHOTS_BACKDROP";
    private const string SpellPrefix = "spell.";

    private const float DuskHour = 19.5f;
    private const float DayHour = 12.5f;
    private const float LaneLength = 18f;
    private const float LaneHalfWidth = 3.5f;
    private const float TargetDistance = 9f;
    private const float CloseDistance = 3.5f;

    /// <summary>Metres between the aim origin and where a bolt first appears, plus a target's radius.</summary>
    private const float MuzzleAndBody = 1.6f;

    /// <summary>Seconds to let the body land, the view settle and the last spell's effects fade.</summary>
    private const double SettleSeconds = 1.2;

    /// <summary>Seconds the cast button is given to start a cast before the cast is begun directly.</summary>
    private const double InputGrace = 0.4;

    /// <summary>Besides every self-cast, the spells photographed in first person: two or more a school.</summary>
    private static readonly HashSet<string> FirstPersonSet = new()
    {
        "emberlash", "flame_lance", "sunfall", "pyre_wall",
        "rime_shard", "frost_nova", "glacial_bulwark", "blizzard",
        "ball_lightning", "storm_conduit", "thunder_step", "stormbrand",
        "null_lance", "gravity_well",
        "stinging_swarm", "thornsnare", "lifebloom_totem",
        "ember_siphon", "soul_tithe", "grave_mark",
    };

    private static readonly HashSet<string> DaylightSet = new()
    {
        "emberlash", "sunfall", "rime_shard", "storm_conduit", "null_lance", "mending_bloom", "soul_tithe",
    };

    /// <summary>The creature that casts each enemy-only spell.</summary>
    private static readonly Dictionary<string, string> EnemyCasters = new()
    {
        ["spell.dragon_breath"] = "enemy.wild_dragon",
        ["spell.ash_breath"] = "enemy.ash_dragon",
        ["spell.drake_breath"] = "enemy.frost_drake",
        ["spell.elder_word"] = "enemy.ancient_dragon",
        ["spell.wither"] = "enemy.hollow_necromancer",
    };

    private const string FallbackCaster = "enemy.hollow_necromancer";

    /// <summary>The player's own spells a humanoid enemy also casts at the first-person player: a
    /// charged bolt, a nova around the caster and a channel. What the player's effects look like
    /// coming the other way had never been photographed.</summary>
    private static readonly HashSet<string> EnemyCastSet = new() { "flame_lance", "blizzard", "storm_conduit" };

    private sealed record Plan(SpellResource Spell, bool FirstPerson, bool Day, bool ByEnemy = false)
    {
        /// <summary>Cast at the player: an enemy-only spell, or one of the player's own in an enemy's hands.</summary>
        public bool Enemy => !Spell.PlayerLearnable || ByEnemy;

        public string Label =>
            $"{StemOf(Spell)}_{(ByEnemy ? "efp" : FirstPerson ? "fp" : "tp")}{(Day ? "day" : string.Empty)}";
    }

    /// <summary>One cast being photographed: who casts it, and when each beat of it happened.</summary>
    private sealed class Run
    {
        public Run(Plan plan, SpellcastingComponent casting, IEntity caster)
        {
            Plan = plan;
            Casting = casting;
            Caster = caster;
        }

        public Plan Plan { get; }

        public SpellcastingComponent Casting { get; }

        public IEntity Caster { get; }

        public SpellResource Spell => Plan.Spell;

        public bool Channeled => Spell.CastMode == CastMode.Channeled;

        /// <summary>Where the player's crosshair is held.</summary>
        public Vector3 AimPoint;

        /// <summary>Metres from the caster to what it is casting at.</summary>
        public float Distance;

        public bool AimLive = true;

        /// <summary>The cast is being held open: the button is down, or the harness is updating it.</summary>
        public bool Held;

        /// <summary>Begun through the component rather than the cast button.</summary>
        public bool Direct;

        public long PressTick;
        public double Pressed = -1;
        public double Began = -1;
        public double Released = -1;
        public double Impact = -1;
        public double Stopped = -1;
        public float Windup;
        public VfxCensus Baseline;
        public VfxCensus Peak;

        /// <summary>What was already drawing under <c>VfxRoot</c> when the cast began.</summary>
        public HashSet<ulong> Before { get; } = new();

        /// <summary>Something that was not drawing before the cast drew during it.</summary>
        public bool DrewNew;
    }

    private readonly List<Node> _props = new();
    private readonly List<Node> _stale = new();
    private Run? _run;
    private ShotLane _lane;
    private bool _prepared;
    private string? _stageFailure;
    private double _stagedAt;
    private VfxCensus _census;
    private MeshInstance3D? _backdrop;

    protected override string Flag => "--spellshots";

    protected override string OutputDir => "user://spell_shots";

    private static string StemOf(SpellResource spell) =>
        spell.Id.StartsWith(SpellPrefix, StringComparison.Ordinal) ? spell.Id[SpellPrefix.Length..] : spell.Id;

    private static float DuskHourOrOverride =>
        float.TryParse(OS.GetEnvironment(HourVariable), NumberStyles.Float, CultureInfo.InvariantCulture, out float hour)
            ? Mathf.PosMod(hour, 24f)
            : DuskHour;

    // --- the shot list -----------------------------------------------------------------------------

    protected override void BuildTimedShots()
    {
        string view = OS.GetEnvironment(ViewVariable).Trim().ToLowerInvariant();
        bool third = view != "fp";
        bool first = view != "tp";

        List<SpellResource> spells = Filtered();
        if (spells.Count == 0)
        {
            Problem($"{FilterVariable}='{OS.GetEnvironment(FilterVariable)}' matched no spell.");
            return;
        }

        List<SpellResource> own = spells.Where(s => s.PlayerLearnable).ToList();
        List<SpellResource> enemy = spells.Where(s => !s.PlayerLearnable).ToList();
        var plans = new List<Plan>();
        if (third)
        {
            plans.AddRange(own.Select(s => new Plan(s, FirstPerson: false, Day: false)));
        }

        if (first)
        {
            plans.AddRange(own
                .Where(s => s.Delivery == SpellDelivery.Self || FirstPersonSet.Contains(StemOf(s)))
                .Select(s => new Plan(s, FirstPerson: true, Day: false)));
        }

        // Enemy casts come after the player's: what they leave on the player (a chill, a burn) must
        // not be standing on the caster of the next player spell.
        if (third)
        {
            plans.AddRange(enemy.Select(s => new Plan(s, FirstPerson: false, Day: false)));
        }

        if (first)
        {
            plans.AddRange(enemy.Select(s => new Plan(s, FirstPerson: true, Day: false)));
            plans.AddRange(own
                .Where(s => EnemyCastSet.Contains(StemOf(s)))
                .Select(s => new Plan(s, FirstPerson: true, Day: false, ByEnemy: true)));
        }

        if (third)
        {
            plans.AddRange(own.Where(s => DaylightSet.Contains(StemOf(s))).Select(s => new Plan(s, FirstPerson: false, Day: true)));
        }

        foreach (Plan plan in plans)
        {
            Add(plan);
        }

        Log.Info($"{Flag}: {plans.Count} cast(s) of {spells.Count} spell(s); view={(view.Length > 0 ? view : "both")}.");
    }

    private static List<SpellResource> Filtered()
    {
        string[] tokens = OS.GetEnvironment(FilterVariable)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.ToLowerInvariant())
            .Select(t => t.StartsWith(SpellPrefix, StringComparison.Ordinal) ? t[SpellPrefix.Length..] : t)
            .ToArray();
        return SpellDatabase.All
            .Where(s => tokens.Length == 0 ||
                        tokens.Contains(StemOf(s).ToLowerInvariant()) ||
                        tokens.Contains(s.School.ToString().ToLowerInvariant()))
            .OrderBy(s => (int)s.School)
            .ThenBy(s => s.Id, StringComparer.Ordinal)
            .ToList();
    }

    private void Add(Plan plan)
    {
        string label = plan.Label;
        TimedShot($"{label}_windup", () => Begin(plan), WindupReady, () => LogShot("windup"), timeout: 16.0);

        // The two shots after the wind-up change nothing themselves (a charged spell apart, which is
        // let go here), so they may follow each other as closely as a capture allows.
        TimedShot($"{label}_release", ReleaseCharge, ReleaseReady, () => LogShot("release"),
            minFrames: plan.Spell.CastMode == CastMode.Charged ? 3 : 1);
        TimedShot($"{label}_impact", () => { }, ImpactReady, () => LogShot("impact"), minFrames: 1);
        TimedShot($"{label}_linger", StopCast, LingerReady, () =>
        {
            LogShot("linger");
            Conclude(plan);
        }, minFrames: 1);
    }

    protected override string? Fatal(string name)
    {
        if (ShotStage.Player() is not { } player)
        {
            return "player is not registered";
        }

        return player.GetComponent<PlayerCameraRig>()?.Camera is { Current: true }
            ? _stageFailure
            : "player has no current gameplay camera";
    }

    // --- staging -----------------------------------------------------------------------------------

    private void Begin(Plan plan)
    {
        _run = null;
        _stageFailure = null;
        ShotStage.ReleaseInputs();

        // The last cast's actors are queued for deletion and still solid until the queue runs: their
        // colliders share the world layer the ground rays read, so staging on the same tick would
        // stand the next targets on the old ones' heads. Staging waits until they are gone.
        Then(() => ShotStage.Player() != null, ClearField);
        Then(() => _stale.TrueForAll(node => !IsInstanceValid(node)), () => Stage(plan));
        Then(() => _run == null || (Clock >= _stagedAt + SettleSeconds && (ShotStage.Player()?.IsOnFloor() ?? true)), StartCast);
    }

    private void ClearField()
    {
        _stale.Clear();
        foreach (Node prop in _props)
        {
            if (IsInstanceValid(prop))
            {
                ShotStage.Free(prop);
                _stale.Add(prop);
            }
        }

        _props.Clear();
        ShotStage.ClearSpellNodes(GetTree(), _stale);

        // And what the last spell left drawn: a Gravity Well's floor glyph outlived its spell by
        // long enough to be in the next two spells' frames.
        _director ??= FindDirector(GetTree().Root);
        if (_director != null && IsInstanceValid(_director))
        {
            _director.KillAll();
        }
        else
        {
            _director = null;
        }
    }

    private SpellVfxDirector? _director;

    private static SpellVfxDirector? FindDirector(Node node)
    {
        if (node is SpellVfxDirector director)
        {
            return director;
        }

        foreach (Node child in node.GetChildren())
        {
            if (FindDirector(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private void Stage(Plan plan)
    {
        _stale.Clear();
        if (ShotStage.Player() is not { } player ||
            player.GetComponent<SpellcastingComponent>() is not { } casting ||
            GetTree().CurrentScene is not { } scene)
        {
            _stageFailure = "the player, its spellbook or the scene is missing";
            return;
        }

        if (!_prepared)
        {
            Prepare(player);
        }

        ShotStage.ResetCaster(player);
        ShotStage.SetHour(plan.Day ? DayHour : DuskHourOrOverride);
        ShotStage.ClearWeather();
        ShotStage.SetView(player, plan.FirstPerson);
        ShotStage.Place(player, ShotStage.OnGround(player, _lane.Start), _lane.Forward);
        ShotStage.SetPitch(player, 0f);

        _run = plan.Enemy ? StageEnemyCast(plan, player, scene) : StagePlayerCast(plan, player, casting, scene);
        _stagedAt = Clock;
    }

    private void Prepare(PlayerCharacter player)
    {
        _prepared = true;
        ShotStage.PreparePlayer(player);
        Log.Info($"{Flag}: effects {ShotStage.ApplyEffectTier()}; {ShotStage.ControlState()}");

        _lane = ShotStage.FindLane(player, LaneLength, LaneHalfWidth);
        string ground = $"ground at {_lane.Start:0.0} heading {_lane.Forward:0.00}, level within {_lane.Spread:0.00} m";
        if (_lane.Clear)
        {
            Log.Info($"{Flag}: {ground}.");
        }
        else
        {
            Log.Warn($"{Flag}: no level, open strip within reach of the save; using the best found ({ground}). " +
                     "Shots may be on a slope or have scenery in them.");
        }

        if (OS.GetEnvironment(BackdropVariable).Trim() != "0" && GetTree().CurrentScene is { } scene)
        {
            // A matt dark wall well behind the targets: something for an effect to read against that
            // is the same in every shot, whatever the save happens to be standing in front of.
            _backdrop = new MeshInstance3D
            {
                Name = "SpellShotBackdrop",
                Mesh = new BoxMesh { Size = new Vector3(120f, 40f, 0.4f) },
                MaterialOverride = new StandardMaterial3D
                {
                    AlbedoColor = new Color(0.10f, 0.10f, 0.11f),
                    Roughness = 1f,
                    MetallicSpecular = 0f,
                },
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            scene.AddChild(_backdrop);
            _backdrop.GlobalTransform = new Transform3D(
                Basis.LookingAt(_lane.Forward, Vector3.Up), _lane.At(LaneLength + 14f) + (Vector3.Up * 14f));
        }
    }

    private Run StagePlayerCast(Plan plan, PlayerCharacter player, SpellcastingComponent casting, Node scene)
    {
        SpellResource spell = plan.Spell;

        // Three targets: one to hit, one straight behind it for a piercing bolt, one beside it for
        // anything that chains, spreads or bursts. A nova, a cone and a dash get them close.
        float distance = spell.Delivery is SpellDelivery.Area or SpellDelivery.Cone or SpellDelivery.Dash
            ? CloseDistance
            : TargetDistance;
        Vector3 main = ShotStage.OnGround(player, _lane.At(distance));
        _props.Add(ShotStage.SpawnTarget(scene, main));
        _props.Add(ShotStage.SpawnTarget(scene, ShotStage.OnGround(player, _lane.At(distance + 3f))));
        _props.Add(ShotStage.SpawnTarget(scene, ShotStage.OnGround(player, _lane.At(distance + 0.8f, 2.6f))));

        // Teach skips the corruption gate a corrupted spell has on learning; nothing gates the cast.
        casting.Teach(spell);
        return new Run(plan, casting, player)
        {
            Distance = distance,
            AimPoint = spell.Delivery switch
            {
                SpellDelivery.Ground => main + (Vector3.Up * 0.15f),
                SpellDelivery.Barrier => ShotStage.OnGround(player, _lane.At(distance * 0.6f)) + (Vector3.Up * 0.05f),
                _ => main + Vector3.Up,
            },
        };
    }

    private Run? StageEnemyCast(Plan plan, PlayerCharacter player, Node scene)
    {
        SpellResource spell = plan.Spell;
        // One of the player's own spells is cast by the humanoid fallback caster.
        string archetypeId = EnemyCasters.GetValueOrDefault(spell.Id, FallbackCaster);
        EnemyArchetypeResource? archetype = EnemyArchetypeDatabase.Get(archetypeId) ?? EnemyArchetypeDatabase.Get(FallbackCaster);
        if (archetype == null)
        {
            _stageFailure = $"no archetype '{archetypeId}' to cast {spell.Id}";
            return null;
        }

        // A breath is a cone as long as its impact radius: stand the creature so the player is well
        // inside it, and never so close that the body fills the view.
        // A nova or a dash of the player's own is cast from close, as the player casts it.
        float distance = spell.Delivery == SpellDelivery.Cone
            ? Mathf.Clamp((spell.ImpactRadius > 0f ? spell.ImpactRadius : 4f) * 0.6f, archetype.CapsuleRadius + 3f, 12f)
            : plan.ByEnemy && spell.Delivery is SpellDelivery.Area or SpellDelivery.Dash ? CloseDistance
            : TargetDistance;
        Vector3 feet = ShotStage.OnGround(player, _lane.At(distance));
        EnemyEntity enemy = EnemyArchetypeFactory.Create(archetype, feet + (Vector3.Up * 0.04f));
        scene.AddChild(enemy);
        _props.Add(enemy);
        enemy.LookAt(new Vector3(player.GlobalPosition.X, enemy.GlobalPosition.Y, player.GlobalPosition.Z), Vector3.Up);

        // The harness is its brain: nothing else may move it, cast for it or open a boss fight.
        foreach (string component in new[] { "AI", "BossController", "Breath", "Flight" })
        {
            if (enemy.GetNodeOrNull<Node>(component) is { } node)
            {
                node.ProcessMode = ProcessModeEnum.Disabled;
            }
        }

        if (enemy.GetComponent<SpellcastingComponent>() is not { } casting)
        {
            _stageFailure = $"'{archetype.Id}' has no spellbook to cast {spell.Id} from";
            return null;
        }

        casting.Teach(spell);
        ShotStage.Deepen(enemy);
        return new Run(plan, casting, enemy)
        {
            Distance = distance,
            AimPoint = feet + (Vector3.Up * (archetype.CapsuleHeight * 0.6f)),
        };
    }

    // --- the cast ----------------------------------------------------------------------------------

    private void StartCast()
    {
        if (_run is not { } run)
        {
            return;
        }

        Node3D? root = ShotStage.VfxRoot(GetTree());
        run.Baseline = ShotStage.Census(root);
        run.Peak = run.Baseline;
        ShotStage.Drawing(root, run.Before);
        run.Pressed = Clock;
        run.PressTick = PhysicsTicks;
        if (run.Plan.Enemy)
        {
            AimEnemy(run);
            BeginDirect(run);
            return;
        }

        if (!run.Casting.Select(run.Spell.Id))
        {
            Problem($"{run.Plan.Label}: {run.Spell.Id} could not be selected ({Describe(run)}).");
        }

        // The press is made between ticks so the router's next tick sees its edge (TimedShots.NextFrame).
        run.Pressed = -1;
        NextFrame(() =>
        {
            Godot.Input.ActionPress(InputActions.Cast);
            run.Pressed = Clock;
            run.PressTick = PhysicsTicks;
            run.Held = true;
        });
    }

    /// <summary>Begins the cast on the component itself: how an enemy casts, and the fallback when the
    /// cast button did not reach the player's router.</summary>
    private void BeginDirect(Run run)
    {
        run.Direct = true;
        bool began = run.Casting.BeginCastById(run.Spell.Id);
        run.Held = began && run.Spell.CastMode != CastMode.Instant;
        if (!began)
        {
            Problem($"{run.Plan.Label}: the cast of {run.Spell.Id} was refused ({Describe(run)}).");
        }
    }

    private string Describe(Run run) =>
        $"canCast={run.Casting.CanCast(run.Spell)} known={run.Casting.IsKnown(run.Spell)} " +
        $"cooldown={run.Casting.CooldownOf(run.Spell):0.0}s locked={run.Casting.SelectionLocked} {ShotStage.ControlState()}";

    /// <summary>Points the creature's cast origin at the player's chest, as its own breath does.</summary>
    private static void AimEnemy(Run run)
    {
        if (run.Casting.AimNode is { } aim && IsInstanceValid(aim) && ShotStage.Player() is { } player)
        {
            Vector3 chest = player.GlobalPosition + Vector3.Up;
            if (aim.GlobalPosition.DistanceSquaredTo(chest) > 0.01f)
            {
                aim.LookAt(chest, Vector3.Up);
            }
        }
    }

    protected override void PhysicsTick(double delta)
    {
        if (_run is not { } run || !IsInstanceValid(run.Casting) || ShotStage.Player() is not { } player)
        {
            return;
        }

        if (run.AimLive)
        {
            ShotStage.AimCameraAt(player, run.AimPoint);
        }

        if (run.Plan.Enemy)
        {
            AimEnemy(run);
        }

        if (run.Pressed < 0)
        {
            return;
        }

        SpellcastingComponent casting = run.Casting;
        bool winding = casting.PendingSpell != null;
        if (run.Began < 0 && (winding || casting.IsCharging || casting.IsChanneling))
        {
            run.Began = Clock;
        }

        if (!run.Direct)
        {
            // An instant spell is a tap: the button comes up once the router has had ticks to see it.
            if (run.Held && run.Spell.CastMode == CastMode.Instant && PhysicsTicks - run.PressTick >= 3)
            {
                run.Held = false;
                NextFrame(() => Godot.Input.ActionRelease(InputActions.Cast));
            }

            if (run.Began < 0 && Clock - run.Pressed > InputGrace)
            {
                Log.Warn($"{Flag}: {run.Plan.Label}: the cast button did not start {run.Spell.Id} " +
                         $"({Describe(run)}); beginning the cast directly.");
                NextFrame(() => Godot.Input.ActionRelease(InputActions.Cast));
                BeginDirect(run);
            }
        }
        else if (run.Held)
        {
            casting.UpdateCast(delta);
        }

        // The wind-up has run out and nothing is being charged: the spell has left. The action's
        // release event says so on the same tick; this covers a caster with no action timeline.
        if (run.Released < 0 && run.Began >= 0 && !winding && !casting.IsCharging)
        {
            run.Released = Clock;
        }

        // A dash and a blink move the caster, and a beam is held where it was aimed.
        if (run.Released >= 0 && !run.Plan.Enemy)
        {
            run.AimLive = false;
        }
    }

    /// <summary>Lets a charged spell go, at the charge the wind-up shot was taken at.</summary>
    private void ReleaseCharge()
    {
        if (_run is { Held: true } run && run.Spell.CastMode == CastMode.Charged)
        {
            EndHold(run);
        }
    }

    /// <summary>Ends whatever is still held (a channel, a charge nobody released) before the last shot.</summary>
    private void StopCast()
    {
        if (_run is not { } run)
        {
            return;
        }

        EndHold(run);
        run.Stopped = Clock;
        ShotStage.ReleaseInputs();
    }

    /// <summary>Called from a shot's drive, which runs between ticks: the button comes up here, and a
    /// cast the harness is holding open itself is ended on the next tick.</summary>
    private void EndHold(Run run)
    {
        if (!run.Held)
        {
            return;
        }

        run.Held = false;
        if (!run.Direct)
        {
            Godot.Input.ActionRelease(InputActions.Cast);
            return;
        }

        Then(() =>
        {
            if (IsInstanceValid(run.Casting))
            {
                run.Casting.EndCast();
            }
        });
    }

    // --- when each shot is taken -------------------------------------------------------------------

    private static double Travel(Run run) =>
        run.Spell.Delivery == SpellDelivery.Projectile && run.Spell.ProjectileSpeed > 0f
            ? Math.Max(0f, run.Distance - MuzzleAndBody) / run.Spell.ProjectileSpeed
            : 0d;

    /// <summary>Most of the way through the wind-up, or with the charge all but full.</summary>
    private bool WindupReady()
    {
        if (_stageFailure != null)
        {
            return true;
        }

        if (_run is not { Began: >= 0 } run)
        {
            return false;
        }

        double wait = run.Spell.CastMode == CastMode.Charged
            ? run.Spell.ChargeTime * 0.9
            : Math.Max(0.05, run.Windup * 0.6);
        return run.Released >= 0 || Clock >= run.Began + wait;
    }

    /// <summary>Just after the spell leaves: a bolt part-way to its target, a telegraph half run, a
    /// beam established, or the first frames of a burst.</summary>
    private bool ReleaseReady()
    {
        if (_run is not { } run)
        {
            return true;
        }

        if (run.Released < 0)
        {
            return false;
        }

        double delay = run.Channeled ? 0.25
            : run.Spell.Delivery switch
            {
                SpellDelivery.Projectile => Travel(run) * 0.4,
                SpellDelivery.Ground or SpellDelivery.Barrier => run.Spell.GroundDelay * 0.5,
                _ => 0.04,
            };
        return Clock >= run.Released + delay;
    }

    /// <summary>The moment it lands, in harness time. The spell's own hit or burst event says when;
    /// where there is none (a wall rising, a self-cast, a miss) it is worked out from the spell.</summary>
    private static double ImpactAt(Run run)
    {
        if (run.Released < 0)
        {
            return double.MaxValue;
        }

        if (run.Channeled)
        {
            return run.Released + 0.7;
        }

        switch (run.Spell.Delivery)
        {
            case SpellDelivery.Barrier:
                return run.Released + run.Spell.GroundDelay + 0.15;
            case SpellDelivery.Self:
                return run.Released + 0.12;
        }

        double expected = run.Spell.Delivery == SpellDelivery.Ground ? run.Spell.GroundDelay : Travel(run);
        return run.Impact >= 0 ? run.Impact + 0.05 : run.Released + expected + 0.4;
    }

    private bool ImpactReady() => _run is not { } run || Clock >= ImpactAt(run);

    /// <summary>What is left afterwards: longer for a zone, a wall or a totem, which should be running.</summary>
    private bool LingerReady()
    {
        if (_run is not { } run)
        {
            return true;
        }

        if (run.Channeled)
        {
            return run.Stopped >= 0 && Clock >= run.Stopped + 0.5;
        }

        SpellResource spell = run.Spell;
        bool stands = spell.ZoneDuration > 0f || spell.SummonDuration > 0f || spell.Delivery == SpellDelivery.Barrier;
        return Clock >= ImpactAt(run) + (stands ? 1.6 : 0.9);
    }

    // --- what the run saw --------------------------------------------------------------------------

    protected override void PreDraw()
    {
        Node3D? root = ShotStage.VfxRoot(GetTree());
        _census = ShotStage.Census(root);
        if (_run is { Pressed: >= 0 } run)
        {
            run.Peak = run.Peak.Max(_census);
            run.DrewNew = run.DrewNew || ShotStage.AnyNewDrawing(root, run.Before);
        }
    }

    /// <summary>One line a shot: the live effect-node count on the frame the PNG shows.</summary>
    private void LogShot(string phase)
    {
        string cast = _run is { } run
            ? $"baseline [{run.Baseline}] sinceCast={Since(run.Pressed)} sinceRelease={Since(run.Released)} " +
              $"sinceImpact={Since(run.Impact)} caster={(run.Direct ? "direct" : "input")}"
            : "not staged";
        Log.Info($"{Flag}: shot {CurrentShot} phase={phase} vfx [{_census}] " +
                 $"vfxRoot={(ShotStage.VfxRoot(GetTree()) != null ? "found" : "MISSING")} {cast}");
    }

    private string Since(double moment) =>
        moment < 0 ? "never" : (Clock - moment).ToString("0.00", CultureInfo.InvariantCulture) + "s";

    /// <summary>After the last shot of a cast: did it happen, and did it draw.</summary>
    private void Conclude(Plan plan)
    {
        string label = plan.Label;
        if (_run is not { } run)
        {
            Problem($"{label}: {_stageFailure ?? "the cast was never staged"}.");
            return;
        }

        SpellVfxRecipe recipe = SpellVfxCatalog.For(plan.Spell);
        bool shouldDraw = !(recipe.Cast.IsEmpty && recipe.Travel.IsEmpty && recipe.Impact.IsEmpty && recipe.Linger.IsEmpty);

        // Either a count rose above where it stood at the cast, or a node drew that was not drawing
        // then. The second cannot be hidden by the previous spell's effects fading meanwhile.
        bool drew = run.DrewNew || run.Peak.Exceeds(run.Baseline);
        Log.Info($"{Flag}: cast {label} spell={plan.Spell.Id} drew={drew} shouldDraw={shouldDraw} " +
                 $"baseline [{run.Baseline}] peak [{run.Peak}] windup={run.Windup:0.00}s " +
                 $"began={(run.Began >= 0)} released={(run.Released >= 0)} impactEvent={(run.Impact >= 0)}");

        if (run.Began < 0)
        {
            Problem($"{label}: {plan.Spell.Id} never began ({Describe(run)}).");
        }
        else if (run.Released < 0)
        {
            Problem($"{label}: {plan.Spell.Id} began and never released (interrupted, or its action never reached the release).");
        }
        else if (run.Impact < 0 && !run.Channeled && plan.Spell.Delivery is SpellDelivery.Projectile or SpellDelivery.Ground)
        {
            Log.Warn($"{Flag}: {label}: no hit or burst event followed the release; the impact shot was timed, and the spell may have missed.");
        }

        if (ShotStage.VfxRoot(GetTree()) == null)
        {
            Problem("no node named VfxRoot is in the tree: the effect director is missing, so no spell can draw.");
        }
        else if (shouldDraw && !drew)
        {
            Problem($"{label}: {plan.Spell.Id} has an effect recipe and added nothing under VfxRoot " +
                    $"(baseline [{run.Baseline}], peak [{run.Peak}]): nothing drew.");
        }
    }

    // --- the casting core's events: when each beat happened ---------------------------------------

    public override void _Ready()
    {
        base._Ready();
        EventBus? bus = EventBus.Instance;
        bus?.Subscribe<CastWindupStartedEvent>(OnWindupStarted);
        bus?.Subscribe<ActionReleasedEvent>(OnActionReleased);
        bus?.Subscribe<SpellHitEvent>(OnSpellHit);
        bus?.Subscribe<SpellBurstEvent>(OnSpellBurst);
    }

    public override void _ExitTree()
    {
        base._ExitTree();
        EventBus? bus = EventBus.Instance;
        bus?.Unsubscribe<CastWindupStartedEvent>(OnWindupStarted);
        bus?.Unsubscribe<ActionReleasedEvent>(OnActionReleased);
        bus?.Unsubscribe<SpellHitEvent>(OnSpellHit);
        bus?.Unsubscribe<SpellBurstEvent>(OnSpellBurst);
        ShotStage.ReleaseInputs();
    }

    private void OnWindupStarted(CastWindupStartedEvent e)
    {
        if (_run is { Pressed: >= 0 } run && ReferenceEquals(e.Caster, run.Caster))
        {
            run.Windup = e.WindupSeconds;
            if (run.Began < 0)
            {
                run.Began = Clock;
            }
        }
    }

    private void OnActionReleased(ActionReleasedEvent e)
    {
        if (_run is { Began: >= 0, Released: < 0 } run && e.Kind == ActionKind.Cast && ReferenceEquals(e.Actor, run.Caster))
        {
            run.Released = Clock;
        }
    }

    private void OnSpellHit(SpellHitEvent e) => MarkImpact(e.Caster);

    private void OnSpellBurst(SpellBurstEvent e) => MarkImpact(e.Caster);

    /// <summary>A hit or a burst is also proof of a release: the casting core delivers inside its own
    /// handler of the release event, so its hit can reach here before that event does.</summary>
    private void MarkImpact(IEntity? caster)
    {
        if (_run is not { Began: >= 0, Impact: < 0 } run || !ReferenceEquals(caster, run.Caster))
        {
            return;
        }

        run.Impact = Clock;
        if (run.Released < 0)
        {
            run.Released = Clock;
        }
    }
}
