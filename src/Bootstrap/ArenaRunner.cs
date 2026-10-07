using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Embervale.Combat;
using Embervale.Combat.Actions;
using Embervale.Core;
using Embervale.Core.Diagnostics;
using Embervale.Core.Events;
using Embervale.Enemies;
using Embervale.Entities;
using Embervale.Items;
using Embervale.Player;
using Embervale.Progression;
using Embervale.Stats;
using Embervale.World;
using Godot;

namespace Embervale.Bootstrap;

/// <summary>
/// <c>--arena</c>: a bot plays the player against a chosen enemy and the fight is measured.
/// Nothing else in the repository fights: the story gate publishes deaths, and the combat probes
/// deliver single packets to hand-built actors. This spawns the real enemy through
/// <see cref="EnemyTemplateRegistry"/> beside a real New Game player, lets its real AI come, and
/// drives the player through the same input actions a keyboard raises, so the input router,
/// stamina, combos, lock-on, poise and the damage pipeline are all the game's own.
///
/// <code>
/// godot --headless --fixed-fps 60 --path . -- --arena=enemy.goblin --trials=5 --level=5
/// godot --headless --fixed-fps 60 --path . -- --arena=all
/// </code>
///
/// <para><b>Roster</b> (<c>--arena=</c>): comma-separated enemy template ids, each fought on its
/// own; <c>id*3</c> is three at once; an <c>encounter.*</c> id is its template at its largest
/// count; <c>all</c> is every authored archetype but the bosses; <c>bosses</c> is the seven boss
/// templates. <c>--arena --list</c> prints the ids.</para>
///
/// <para><b>Options.</b> <c>--trials=N</c> (3; 1 for <c>all</c>/<c>bosses</c>),
/// <c>--seed=N</c> (1; trial k is seeded N+k), <c>--level=N</c>, <c>--weapon=&lt;itemId&gt;</c>,
/// <c>--equip=id,id</c>, <c>--setup="console line; console line"</c> (run through the dev
/// console: <c>learn</c>, <c>perk</c>, <c>give</c>), <c>--policy=aggressive|guard|passive</c>,
/// <c>--count=N</c>, <c>--distance=M</c> (6), <c>--max-fight-s=S</c> of game time (90),
/// <c>--speed=K</c> (4), <c>--budget-s=S</c> of real time for a sweep (480).</para>
///
/// <para><b>Time.</b> <c>--speed=K</c> multiplies both the time scale and the physics tick rate,
/// so every physics step is still 1/60 s of game time and K of them fit where one did. Launch
/// with the engine's <c>--fixed-fps 60</c> (before <c>--</c>) so frames are not tied to the wall
/// clock; that is also what makes two runs with one seed comparable. Hit-stop is removed for the
/// run: it freezes the clock for real milliseconds and changes nothing a fight is scored on.</para>
///
/// <para><b>Result.</b> One row per matchup (<see cref="ArenaMath.Summarise"/>): win rate, time to
/// kill, damage dealt and taken, swings and hits either way, and flags. <c>--json</c> adds every
/// trial. The run fails (exit 1) only on a broken fight: an enemy that neither dealt nor took
/// damage in any trial, a spawn that produced nothing, a player with no weapon. Balance is a number
/// to read, not a pass mark.</para>
///
/// <para>⚠️ The bot is one fixed policy, not a player. Its numbers compare builds with each other;
/// they are not a difficulty verdict. The fight happens where a New Game lands, so anything else
/// standing there can join in: <c>third_party</c> flags a fight that was not a duel.</para>
/// </summary>
internal static class ArenaRunner
{
    private const string Slot = "arena_probe";

    /// <summary>The bot swings when the gap between the bodies is at most this.</summary>
    private const float AttackGap = 1.3f;

    /// <summary>Physics ticks between attack presses: a press on one tick, the release on the next.</summary>
    private const int AttackCadence = 6;

    private static readonly StringName[] Held =
    {
        GameInput.MoveForward, InputActions.Attack, InputActions.Block,
    };

    private sealed record Matchup(string Label, string Template, int Count);

    private sealed record Options(
        int Trials, ulong Seed, ArenaPolicy Policy, float Distance, float MaxFightSeconds, int Speed, float BudgetSeconds);

    public static async void Run(ApplicationRoot root, SessionLifecycleCoordinator lifecycle)
    {
        HeadlessReport report = HeadlessGate.Begin("arena");
        HeadlessStory.UseReport(report);
        SceneTree tree = root.GetTree();

        if (HeadlessArgs.Has("--list"))
        {
            report.Fact("enemies", Archetypes()).Fact("bosses", HeadlessStory.Bosses).Fact("encounters", Encounters());
            HeadlessGate.Finish(tree, report, legacyLine: false);
            return;
        }

        string rosterValue = HeadlessArgs.Value(HeadlessArena.FlagArgument) ?? string.Empty;
        bool sweep = rosterValue is "all" or "bosses";
        if (!ArenaMath.TryParsePolicy(HeadlessArgs.Value("--policy"), out ArenaPolicy policy))
        {
            report.Refuse($"--policy={HeadlessArgs.Value("--policy")} is not a policy: aggressive, guard or passive.");
        }

        List<Matchup> matchups = Roster(rosterValue, report);
        if (!report.Passed)
        {
            HeadlessGate.Finish(tree, report);
            return;
        }

        var options = new Options(
            Trials: Math.Clamp(HeadlessArgs.Int("--trials", sweep ? 1 : 3), 1, 200),
            Seed: ulong.TryParse(HeadlessArgs.Value(HeadlessGate.SeedArgument), out ulong seed) ? seed : 1UL,
            Policy: policy,
            Distance: Math.Clamp(HeadlessArgs.Float("--distance", 6f), 2f, 40f),
            MaxFightSeconds: Math.Clamp(HeadlessArgs.Float("--max-fight-s", 90f), 5f, 1800f),
            Speed: Math.Clamp(HeadlessArgs.Int("--speed", 4), 1, 16),
            BudgetSeconds: HeadlessArgs.Float("--budget-s", 480f));

        report.Fact("policy", policy.ToString().ToLowerInvariant()).Fact("trials", options.Trials)
            .Fact("seed", options.Seed.ToString()).Fact("speed", options.Speed);

        HeadlessGate.IsolateFromPlayerSaves("arena");
        HeadlessGate.ArmTimeLimit(root, report);

        int ticksBefore = Engine.PhysicsTicksPerSecond;
        int stepsBefore = Engine.MaxPhysicsStepsPerFrame;
        int sleepBefore = OS.LowProcessorUsageModeSleepUsec;
        var rows = new List<object?>();
        var remaining = new List<string>();
        try
        {
            var plan = new RunPlan
            {
                Name = "arena", Order = Array.Empty<string>(), Picks = new Dictionary<string, string[]>(),
                ExpectedForks = Array.Empty<string>(),
            };
            var session = new StoryPlaythrough(root, lifecycle, Slot, plan);
            if (await session.StartNewGameAsync() && await session.OpeningAsync() &&
                lifecycle.Session?.Players.Player is { } player && Prepare(lifecycle.Session, player, report))
            {
                await HeadlessLifecycle.Frames(root, 30);
                Vector3 origin = player.GlobalPosition;
                Basis facing = player.GlobalBasis;

                // The navigation map is synchronised off the main thread and is some real time
                // behind the session: searched once, a run found ground or not by luck, and two
                // runs of one seed fought in different places. Wait for it.
                (Vector3 Origin, Vector3 Forward)? found = FindDuelGround(player, options.Distance);
                for (int waited = 0; found == null && waited < GroundWaitFrames; waited += 10)
                {
                    await HeadlessLifecycle.Frames(root, 10);
                    found = FindDuelGround(player, options.Distance);
                }

                if (found is { } ground)
                {
                    origin = ground.Origin;
                    facing = Basis.LookingAt(ground.Forward, Vector3.Up);
                    report.Fact("ground", $"{origin.X:0.#},{origin.Y:0.#},{origin.Z:0.#}");
                }
                else
                {
                    report.Warn("no walkable, open ground for a duel was found near the New Game spot; " +
                                "an enemy that has to walk to the player may never arrive.");
                }

                // Every step stays 1/60 s of game time; there are just more of them per frame.
                Engine.PhysicsTicksPerSecond = ticksBefore * options.Speed;
                Engine.MaxPhysicsStepsPerFrame = Math.Max(stepsBefore, options.Speed * 2);
                // Both: the clock now, and the value hit-stop and a boss's slow beat hand it back at.
                Embervale.Combat.HitStopDirector.BaseTimeScale = options.Speed;
                Engine.TimeScale = options.Speed;
                OS.LowProcessorUsageModeSleepUsec = 1;

                ulong began = Time.GetTicksMsec();
                foreach (Matchup matchup in matchups)
                {
                    if (options.BudgetSeconds > 0f && (Time.GetTicksMsec() - began) / 1000.0 > options.BudgetSeconds)
                    {
                        remaining.Add(matchup.Label);
                        continue;
                    }

                    var trials = new List<ArenaTrial>();
                    for (int k = 0; k < options.Trials; k++)
                    {
                        GD.Seed(options.Seed + (ulong)k);
                        ArenaTrial? trial = await FightAsync(root, lifecycle, player, origin, facing, matchup, options, report);
                        if (trial == null)
                        {
                            break;
                        }

                        trials.Add(trial);
                    }

                    Dictionary<string, object?> row = ArenaMath.Summarise(trials);
                    row["enemy"] = matchup.Label;
                    if (HeadlessGate.Json)
                    {
                        var each = new List<object?>();
                        foreach (ArenaTrial trial in trials)
                        {
                            each.Add(ArenaMath.Row(trial));
                        }

                        row["each"] = each;
                    }

                    rows.Add(row);
                    if (row["flags"] is List<string> flags && flags.Contains("no_contact"))
                    {
                        report.Fail($"{matchup.Label}: nothing fought. Over {trials.Count} trial(s) the enemy neither dealt nor took damage " +
                                    "(it never engaged, or it cannot be reached or hit).");
                    }

                    HeadlessGate.Say($"arena: {matchup.Label} {Json(row)}");
                }
            }
        }
        catch (Exception ex)
        {
            report.Fail($"the arena threw: {ex}");
        }
        finally
        {
            Release();
            Embervale.Combat.HitStopDirector.BaseTimeScale = 1f;
            Engine.TimeScale = 1.0;
            Engine.PhysicsTicksPerSecond = ticksBefore;
            Engine.MaxPhysicsStepsPerFrame = stepsBefore;
            OS.LowProcessorUsageModeSleepUsec = sleepBefore;
        }

        report.Fact("matchups", rows);
        if (remaining.Count > 0)
        {
            report.Fact("partial", true).Fact("remaining", string.Join(",", remaining));
            report.Warn($"--budget-s ran out with {remaining.Count} matchup(s) unfought; continue with --arena=<remaining>.");
        }

        Save.SaveManager.Instance?.DeleteSlot(Slot);
        HeadlessGate.Finish(tree, report);
    }

    // --- roster --------------------------------------------------------------------------

    private static List<string> Archetypes()
    {
        // Archetype rows and the templates registered in code (the goblin has no archetype row):
        // everything --arena=<id> accepts, so the list and the "all" sweep leave nothing out.
        var ids = new SortedSet<string>(EnemyTemplateRegistry.TemplateIds, StringComparer.Ordinal);
        foreach (EnemyArchetypeResource archetype in EnemyArchetypeDatabase.All)
        {
            ids.Add(archetype.Id);
        }

        ids.ExceptWith(HeadlessStory.Bosses);
        return new List<string>(ids);
    }

    private static List<string> Encounters()
    {
        var ids = new List<string>();
        foreach (EncounterResource encounter in EncounterDatabase.All)
        {
            ids.Add(encounter.Id);
        }

        ids.Sort(StringComparer.Ordinal);
        return ids;
    }

    private static List<Matchup> Roster(string value, HeadlessReport report)
    {
        var matchups = new List<Matchup>();
        int count = Math.Clamp(HeadlessArgs.Int("--count", 1), 1, 12);
        if (value is "all" or "bosses")
        {
            IEnumerable<string> ids = value == "all" ? Archetypes() : HeadlessStory.Bosses;
            foreach (string id in ids)
            {
                matchups.Add(new Matchup(count > 1 ? $"{id}*{count}" : id, id, count));
            }

            return matchups;
        }

        string? refusal = ArenaMath.ParseRoster(value, count, out List<(string Id, int Count)> roster);
        if (refusal != null)
        {
            report.Refuse($"--arena: {refusal} Ids: --arena --list.");
            return matchups;
        }

        foreach ((string id, int each) in roster)
        {
            if (EncounterDatabase.Get(id) is { } encounter)
            {
                int most = Math.Clamp(Math.Max(encounter.MinCount, encounter.MaxCount), 1, 12);
                matchups.Add(new Matchup($"{id}({encounter.EnemyTemplateId}*{most})", encounter.EnemyTemplateId, most));
            }
            else if (EnemyTemplateRegistry.IsRegistered(id))
            {
                matchups.Add(new Matchup(each > 1 ? $"{id}*{each}" : id, id, each));
            }
            else
            {
                report.Refuse($"--arena: '{id}' is not an enemy template or an encounter. Ids: --arena --list.");
            }
        }

        return matchups;
    }

    // --- the player ----------------------------------------------------------------------

    /// <summary>Level, loadout and console set-up, and the things a fight must not be scored
    /// against. False when the player cannot fight at all.</summary>
    private static bool Prepare(GameSession session, PlayerCharacter player, HeadlessReport report)
    {
        // Hit-stop freezes the clock for real milliseconds per blow. It is presentation, and at
        // speed it would be most of the run.
        foreach (HitStopDirector hitStop in FindAll<HitStopDirector>(session.GetTree().Root))
        {
            hitStop.QueueFree();
        }

        session.DevTools?.Console?.Execute("tutorial skip");

        int level = HeadlessArgs.Int("--level", 0);
        if (level > 0 && player.GetComponent<ProgressionComponent>() is { } progression)
        {
            for (int guard = 0; guard < 400 && progression.Level < level && !progression.IsMaxLevel; guard++)
            {
                progression.AddXp(Math.Max(1, progression.XpToNext - progression.CurrentXp));
            }
        }

        var gear = new List<string>();
        if (HeadlessArgs.Value("--weapon") is { Length: > 0 } weaponId)
        {
            gear.Add(weaponId);
        }

        gear.AddRange(HeadlessArgs.List("--equip"));
        foreach (string itemId in gear)
        {
            if (ItemDatabase.Get(itemId) is not { } item ||
                player.GetComponent<InventoryComponent>() is not { } pack ||
                player.GetComponent<EquipmentComponent>() is not { } equipment)
            {
                report.Refuse($"'{itemId}' is not an item (--state --ids=items --match=...).");
                return false;
            }

            pack.AddItem(item, 1);
            if (pack.FirstInstanceOf(itemId) is not { } instance || !equipment.Equip(instance))
            {
                report.Refuse($"the player could not equip '{itemId}' (level {player.GetComponent<ProgressionComponent>()?.Level}).");
                return false;
            }
        }

        foreach (string line in (HeadlessArgs.Value("--setup") ?? string.Empty).Split(
                     ';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (session.DevTools?.Console is not { } console)
            {
                report.Warn("--setup was ignored: this run has no dev console (a capture or export run).");
                break;
            }

            Log.Info($"arena: setup '{line}' -> {console.Execute(line)}");
        }

        WeaponResource? weapon = player.GetComponent<CharacterActionComponent>()?.Weapon;
        if (weapon == null)
        {
            report.Fail("the player has no weapon: nothing to fight with.");
            return false;
        }

        if (weapon.IsRanged)
        {
            report.Warn("the player holds a ranged weapon; the bot only knows how to close and swing.");
        }

        player.GetComponent<StatsComponent>()?.RefillResources();
        report.Fact("level", player.GetComponent<ProgressionComponent>()?.Level ?? 0)
            .Fact("weapon", weapon.ResourcePath)
            .Fact("player_health", Math.Round(player.GetComponent<StatsComponent>()?.GetMax(StatType.Health) ?? 0f, 1));
        return true;
    }

    // --- the ground ----------------------------------------------------------------------

    /// <summary>
    /// Where the fight is staged: the nearest spot to the New Game landing from which an enemy
    /// <paramref name="distance"/> metres away has a straight walk to the player and a clear line
    /// to them. The landing spot itself fails that: the navigation mesh there has no path across
    /// the six metres in front of the player, so a spawned enemy saw them, entered combat and stood
    /// still for the whole fight. Null when nothing within reach qualifies.
    /// </summary>
    private const int GroundWaitFrames = 600;

    private static (Vector3 Origin, Vector3 Forward)? FindDuelGround(PlayerCharacter player, float distance)
    {
        Rid map = player.GetWorld3D().NavigationMap;
        PhysicsDirectSpaceState3D space = player.GetWorld3D().DirectSpaceState;
        var exclude = new Godot.Collections.Array<Rid> { player.GetRid() };
        Vector3 landing = player.GlobalPosition;
        Vector3 eye = Vector3.Up * 1.2f;

        for (int ring = 0; ring <= 12; ring++)
        {
            int spots = ring == 0 ? 1 : 8;
            for (int spot = 0; spot < spots; spot++)
            {
                Vector3 offset = Vector3.Forward.Rotated(Vector3.Up, Mathf.Tau * spot / spots) * (ring * 4f);
                Vector3 origin = NavigationServer3D.MapGetClosestPoint(map, landing + offset);
                if (origin.DistanceTo(landing + offset) > 2f)
                {
                    continue;
                }

                for (int turn = 0; turn < 8; turn++)
                {
                    Vector3 forward = Vector3.Forward.Rotated(Vector3.Up, Mathf.Tau * turn / 8);
                    Vector3 far = NavigationServer3D.MapGetClosestPoint(map, origin + (forward * distance));

                    // Level, on the mesh, and a walk that is the straight line and not a detour.
                    if (Mathf.Abs(far.Y - origin.Y) > 1f || far.DistanceTo(origin + (forward * distance)) > 1f)
                    {
                        continue;
                    }

                    Vector3[] path = NavigationServer3D.MapGetPath(map, far, origin, true);
                    float walked = 0f;
                    for (int i = 1; i < path.Length; i++)
                    {
                        walked += path[i - 1].DistanceTo(path[i]);
                    }

                    if (path.Length < 2 || path[^1].DistanceTo(origin) > 0.75f || walked > (distance * 1.15f) + 0.5f)
                    {
                        continue;
                    }

                    var ray = PhysicsRayQueryParameters3D.Create(origin + eye, far + eye);
                    ray.Exclude = exclude;
                    if (space.IntersectRay(ray).Count == 0)
                    {
                        return (origin, forward);
                    }
                }
            }
        }

        return null;
    }

    // --- one fight -----------------------------------------------------------------------

    /// <summary>Spawns the matchup, plays it to its end and returns what happened. Null when the
    /// fight could not be staged at all (already recorded as a failure).</summary>
    private static async Task<ArenaTrial?> FightAsync(
        ApplicationRoot root,
        SessionLifecycleCoordinator lifecycle,
        PlayerCharacter player,
        Vector3 origin,
        Basis facing,
        Matchup matchup,
        Options options,
        HeadlessReport report)
    {
        if (!GodotObject.IsInstanceValid(player) || player.GetParent() is not { } world ||
            player.GetComponent<StatsComponent>() is not { } stats)
        {
            report.Fail($"{matchup.Label}: the player is gone.");
            return null;
        }

        LockOnComponent? lockOn = player.GetComponent<LockOnComponent>();
        if (lockOn?.Target != null)
        {
            lockOn.ToggleNearest();
        }

        player.Velocity = Vector3.Zero;
        player.GlobalPosition = origin;
        player.GlobalBasis = facing;
        stats.RefillResources();

        // In an arc ahead of the player, a little above the ground, as the console's spawn does.
        Vector3 forward = -facing.Z;
        forward.Y = 0f;
        forward = forward.LengthSquared() < 1e-4f ? Vector3.Forward : forward.Normalized();
        var enemies = new List<EnemyEntity>();
        for (int i = 0; i < matchup.Count; i++)
        {
            float angle = matchup.Count == 1 ? 0f : Mathf.DegToRad(-40f + (80f * i / (matchup.Count - 1)));
            Vector3 at = origin + (forward.Rotated(Vector3.Up, angle) * options.Distance) + new Vector3(0f, 0.5f, 0f);
            EnemyEntity enemy = EnemyTemplateRegistry.Create(matchup.Template, at);
            world.AddChild(enemy);

            // Facing the player. A spawn keeps the identity rotation, which here is the enemy's back:
            // outside its proximity bubble it never saw a player who stood still, and it only ever
            // fought because the bot walked into it.
            enemy.LookAt(new Vector3(origin.X, enemy.GlobalPosition.Y, origin.Z), Vector3.Up);
            enemies.Add(enemy);
        }

        if (enemies.Count == 0)
        {
            report.Fail($"{matchup.Label}: the spawn produced nothing.");
            return null;
        }

        double seconds = 0d;
        double dealt = 0d;
        double taken = 0d;
        double other = 0d;
        double firstContact = -1d;
        double windingUntil = -1d;
        int swings = 0;
        int hits = 0;
        int enemyAttacks = 0;
        int enemyHits = 0;
        int blocked = 0;
        int staggers = 0;
        int parries = 0;
        bool playerDied = false;
        var dead = new HashSet<ulong>();

        bool IsEnemy(IEntity? entity)
        {
            foreach (EnemyEntity enemy in enemies)
            {
                if (ReferenceEquals(enemy, entity))
                {
                    return true;
                }
            }

            return false;
        }

        Action<DamageDealtEvent> onDamage = e =>
        {
            if (IsEnemy(e.Target))
            {
                if (ReferenceEquals(e.Source, player))
                {
                    dealt += e.Amount;
                    hits++;
                }
                else
                {
                    other += e.Amount;
                }
            }
            else if (ReferenceEquals(e.Target, player))
            {
                taken += e.Amount;
                if (IsEnemy(e.Source))
                {
                    enemyHits++;
                }

                if (e.IsBlocked)
                {
                    blocked++;
                }
            }
            else
            {
                return;
            }

            if (firstContact < 0d && e.Amount > 0f)
            {
                firstContact = seconds;
            }
        };
        Action<AttackPerformedEvent> onAttack = e =>
        {
            if (ReferenceEquals(e.Attacker, player))
            {
                swings++;
            }
            else if (IsEnemy(e.Attacker))
            {
                enemyAttacks++;

                // The wind-up, and the moment after it in which the blow lands.
                windingUntil = Math.Max(windingUntil, seconds + e.WindupSeconds + 0.3d);
            }
        };
        Action<EntityDiedEvent> onDied = e =>
        {
            if (ReferenceEquals(e.Entity, player))
            {
                playerDied = true;
            }
            else if (IsEnemy(e.Entity))
            {
                dead.Add(e.Entity.RuntimeId);
            }
        };
        Action<EntityStaggeredEvent> onStagger = e =>
        {
            if (IsEnemy(e.Entity))
            {
                staggers++;
            }
        };
        Action<EntityParriedEvent> onParry = e =>
        {
            if (ReferenceEquals(e.Defender, player))
            {
                parries++;
            }
        };

        EventBus bus = EventBus.Instance!;
        bus.Subscribe(onDamage);
        bus.Subscribe(onAttack);
        bus.Subscribe(onDied);
        bus.Subscribe(onStagger);
        bus.Subscribe(onParry);

        string outcome = ArenaMath.Timeout;
        int tick = 0;
        try
        {
            while (true)
            {
                await HeadlessLifecycle.Frames(root, 1);

                seconds += Engine.TimeScale / Engine.PhysicsTicksPerSecond;
                tick++;

                if (playerDied || !GodotObject.IsInstanceValid(player))
                {
                    outcome = ArenaMath.Loss;
                    break;
                }

                EnemyEntity? target = null;
                float nearest = float.MaxValue;
                foreach (EnemyEntity enemy in enemies)
                {
                    if (dead.Contains(enemy.RuntimeId) || !GodotObject.IsInstanceValid(enemy) || !enemy.IsInsideTree() ||
                        enemy.GetComponent<StatsComponent>() is not { IsAlive: true })
                    {
                        continue;
                    }

                    Vector3 to = enemy.GlobalPosition - player.GlobalPosition;
                    to.Y = 0f;
                    if (to.Length() < nearest)
                    {
                        nearest = to.Length();
                        target = enemy;
                    }
                }

                if (target == null)
                {
                    outcome = ArenaMath.Win;
                    break;
                }

                // The tick cap is for a clock something has stopped: game time would never arrive.
                if (seconds >= options.MaxFightSeconds || tick > options.MaxFightSeconds * 600f)
                {
                    break;
                }

                // A dialogue a death opened would hold the tree paused for the rest of the fight.
                if (lifecycle.Session?.Ui.Dialogue is { IsOpen: true } dialogue)
                {
                    dialogue.EndConversation();
                }

                if (GameManager.Instance is not { IsPlaying: true } || UiState.MenuOpen)
                {
                    Release();
                    continue;
                }

                if (lockOn != null && lockOn.Target == null)
                {
                    lockOn.ToggleNearest();
                }

                // No lock (nothing in its range yet, or no lock component): face the target the way
                // the lock would, so "forward" still means "at it".
                if (lockOn?.Target == null && options.Policy != ArenaPolicy.Passive && nearest > 0.1f)
                {
                    player.LookAt(
                        new Vector3(target.GlobalPosition.X, player.GlobalPosition.Y, target.GlobalPosition.Z), Vector3.Up);
                }

                float gap = nearest - BodyMetrics.CapsuleRadius(target, 0.4f) - BodyMetrics.CapsuleRadius(player, 0.4f);
                ArenaIntent intent = ArenaMath.Decide(
                    options.Policy,
                    new ArenaSense(gap, seconds < windingUntil, stats.GetNormalized(StatType.Stamina)),
                    AttackGap);

                Hold(GameInput.MoveForward, intent.Forward);
                Hold(InputActions.Block, intent.Block);

                // A tap: down on one tick, up on the next. From rest that is a light swing; during
                // a swing the press chains the combo.
                Hold(InputActions.Attack, intent.Attack && tick % AttackCadence == 0);
            }
        }
        finally
        {
            Release();
            bus.Unsubscribe(onDamage);
            bus.Unsubscribe(onAttack);
            bus.Unsubscribe(onDied);
            bus.Unsubscribe(onStagger);
            bus.Unsubscribe(onParry);
        }

        double healthLeft = outcome == ArenaMath.Loss ? 0d : stats.GetNormalized(StatType.Health);

        // Let go of the lock before its target is freed: a timed-out enemy is still alive, and the
        // camera would follow a disposed body for the frames until the lock noticed.
        if (lockOn?.Target != null)
        {
            lockOn.ToggleNearest();
        }

        foreach (EnemyEntity enemy in enemies)
        {
            if (GodotObject.IsInstanceValid(enemy) && !enemy.IsQueuedForDeletion())
            {
                enemy.QueueFree();
            }
        }

        // Let the frees land, and let a boss's defeat beat (slow motion, then its conversation) pass.
        for (int frame = 0; frame < 240; frame++)
        {
            await HeadlessLifecycle.Frames(root, 1);
            if (lifecycle.Session?.Ui.Dialogue is { IsOpen: true } dialogue)
            {
                dialogue.EndConversation();
            }

            if (frame >= 8 && Engine.TimeScale >= 0.99 && !UiState.MenuOpen)
            {
                break;
            }
        }

        return new ArenaTrial(
            outcome, seconds, dealt, taken, healthLeft, swings, hits, enemyAttacks, enemyHits, blocked, staggers,
            parries, firstContact, other);
    }

    // --- input ---------------------------------------------------------------------------

    /// <summary>Holds or lets go of an input action, the way a key would. Called from the
    /// physics-frame signal, which is raised before any node's physics step, so the router sees a
    /// press as just-pressed on the same tick.</summary>
    private static void Hold(StringName action, bool down)
    {
        if (down == Input.IsActionPressed(action))
        {
            return;
        }

        if (down)
        {
            Input.ActionPress(action);
        }
        else
        {
            Input.ActionRelease(action);
        }
    }

    private static void Release()
    {
        foreach (StringName action in Held)
        {
            Hold(action, false);
        }
    }

    private static List<T> FindAll<T>(Node root)
        where T : Node
    {
        var found = new List<T>();
        var pending = new Stack<Node>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            Node node = pending.Pop();
            if (node is T match)
            {
                found.Add(match);
            }

            foreach (Node child in node.GetChildren())
            {
                pending.Push(child);
            }
        }

        return found;
    }

    private static string Json(Dictionary<string, object?> row)
    {
        var report = new HeadlessReport("row");
        report.Fact("row", row);
        return report.ToJson(0);
    }
}
