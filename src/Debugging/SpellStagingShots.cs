using System;
using System.Collections.Generic;
using Embervale.Combat;
using Embervale.Core;
using Embervale.Core.Diagnostics;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Entities;
using Embervale.Magic;
using Embervale.Magic.Vfx;
using Embervale.Player;
using Embervale.Save;
using Embervale.Settings;
using Embervale.Stats;
using Embervale.World;
using Godot;

namespace Embervale.Debugging;

/// <summary>What is under the effect director's <c>VfxRoot</c> on one frame: every node, the ones
/// that are visible, the particle emitters that are emitting and the lights that are lit.</summary>
internal readonly record struct VfxCensus(int Nodes, int Visible, int Emitters, int Lights)
{
    /// <summary>True when any count is above <paramref name="baseline"/>: something was added, shown
    /// or started emitting. Pooled effects are covered by the visible and emitter counts.</summary>
    public bool Exceeds(VfxCensus baseline) =>
        Nodes > baseline.Nodes || Visible > baseline.Visible || Emitters > baseline.Emitters || Lights > baseline.Lights;

    public VfxCensus Max(VfxCensus other) => new(
        Math.Max(Nodes, other.Nodes), Math.Max(Visible, other.Visible),
        Math.Max(Emitters, other.Emitters), Math.Max(Lights, other.Lights));

    public override string ToString() => $"nodes={Nodes} visible={Visible} emitters={Emitters} lights={Lights}";
}

/// <summary>A strip of open, level ground: where it starts, which way it runs and how level it is.</summary>
internal readonly record struct ShotLane(Vector3 Start, Vector3 Forward, float Spread, bool Clear)
{
    public Vector3 Right => Forward.Cross(Vector3.Up).Normalized();

    /// <summary>A point <paramref name="along"/> the lane and <paramref name="side"/> to its right, at
    /// the start's height.</summary>
    public Vector3 At(float along, float side = 0f) => Start + (Forward * along) + (Right * side);
}

/// <summary>
/// Staging shared by the spell, camera and effect-performance harnesses (<c>--spellshots</c>,
/// <c>--camshots</c>, <c>--vfxperf</c>): finding level ground, placing actors, the view and the effect
/// tier for the run, and counting what the effect director has under <c>VfxRoot</c>. Nothing here
/// writes to disk: settings are changed on the live object and announced, never saved, and autosaves
/// are turned off for the process.
/// Lives in a <c>*Shots.cs</c> file because the shipping build excludes that pattern.
/// </summary>
internal static class ShotStage
{
    public const string TierVariable = "EMBERVALE_SPELLSHOTS_TIER";
    public const string ReducedVariable = "EMBERVALE_SPELLSHOTS_REDUCED";

    private const string TargetAttributesPath = "res://data/attributes/DummyAttributes.tres";
    private const string ClearWeatherId = "weather.clear";
    private const float LevelSpread = 0.3f;
    private const float StatBoost = 1_000_000f;
    private static readonly object BoostSource = new();

    private static readonly float[] SearchRings = { 0f, 6f, 12f, 20f, 28f, 36f, 44f };

    private static Node3D? _vfxRoot;
    private static ulong _nextRootSearch;

    public static PlayerCharacter? Player() =>
        ServiceLocator.Instance is { } locator && locator.TryGet(out PlayerCharacter player) &&
        GodotObject.IsInstanceValid(player) && player.IsInsideTree()
            ? player
            : null;

    private static SettingsService? SettingsOrNull() =>
        ServiceLocator.Instance is { } locator && locator.TryGet(out SettingsService settings) ? settings : null;

    // --- the effect director, reached through the tree --------------------------------------------

    /// <summary>The effect director's root, found by name so this compiles against any director. The
    /// search walks the whole tree, so a miss is retried at most once a second.</summary>
    public static Node3D? VfxRoot(SceneTree tree)
    {
        if (_vfxRoot != null && GodotObject.IsInstanceValid(_vfxRoot) && _vfxRoot.IsInsideTree())
        {
            return _vfxRoot;
        }

        _vfxRoot = null;
        ulong now = Time.GetTicksMsec();
        if (now < _nextRootSearch)
        {
            return null;
        }

        _nextRootSearch = now + 1000;
        _vfxRoot = tree.Root.FindChild("VfxRoot", recursive: true, owned: false) as Node3D;
        return _vfxRoot;
    }

    public static VfxCensus Census(Node? root)
    {
        if (root == null || !GodotObject.IsInstanceValid(root))
        {
            return default;
        }

        int nodes = 0;
        int visible = 0;
        int emitters = 0;
        int lights = 0;
        Count(root, ref nodes, ref visible, ref emitters, ref lights);
        return new VfxCensus(nodes, visible, emitters, lights);
    }

    private static void Count(Node node, ref int nodes, ref int visible, ref int emitters, ref int lights)
    {
        int count = node.GetChildCount();
        for (int i = 0; i < count; i++)
        {
            Node child = node.GetChild(i);
            nodes++;
            bool shown = child is Node3D spatial && spatial.IsVisibleInTree();
            if (shown)
            {
                visible++;
                if (child is GpuParticles3D { Emitting: true } or CpuParticles3D { Emitting: true })
                {
                    emitters++;
                }
                else if (child is Light3D)
                {
                    lights++;
                }
            }

            Count(child, ref nodes, ref visible, ref emitters, ref lights);
        }
    }

    /// <summary>True when something under <paramref name="root"/> is drawing now that was not in
    /// <paramref name="before"/> (see <see cref="Drawing"/>). Unlike a count against a baseline, this
    /// cannot be cancelled out by the last spell's effects fading on the same frames.</summary>
    public static bool AnyNewDrawing(Node? root, HashSet<ulong> before)
    {
        if (root == null || !GodotObject.IsInstanceValid(root))
        {
            return false;
        }

        int count = root.GetChildCount();
        for (int i = 0; i < count; i++)
        {
            Node child = root.GetChild(i);
            if ((Draws(child) && !before.Contains(child.GetInstanceId())) || AnyNewDrawing(child, before))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Collects the nodes under <paramref name="root"/> that are drawing: visible, and for a
    /// particle system also emitting (a pooled one may sit visible and idle).</summary>
    public static void Drawing(Node? root, HashSet<ulong> into)
    {
        if (root == null || !GodotObject.IsInstanceValid(root))
        {
            return;
        }

        int count = root.GetChildCount();
        for (int i = 0; i < count; i++)
        {
            Node child = root.GetChild(i);
            if (Draws(child))
            {
                into.Add(child.GetInstanceId());
            }

            Drawing(child, into);
        }
    }

    private static bool Draws(Node node) => node switch
    {
        GpuParticles3D gpu => gpu.Emitting && gpu.IsVisibleInTree(),
        CpuParticles3D cpu => cpu.Emitting && cpu.IsVisibleInTree(),
        Node3D spatial => spatial.IsVisibleInTree(),
        _ => false,
    };

    /// <summary>Frees every zone, wall, totem and ground telegraph a spell left standing, so the next
    /// shot starts from an empty field. Each ends its own effects as it leaves the tree. The nodes are
    /// only queued: <paramref name="freed"/> receives them so a caller can wait until they are gone.</summary>
    public static int ClearSpellNodes(SceneTree tree, List<Node>? freed = null)
    {
        int count = 0;
        foreach (Node node in tree.Root.FindChildren("*Spell*", "Node3D", recursive: true, owned: false))
        {
            if (node is SpellZone or SpellBarrier or SpellTotem or SpellGround && !node.IsQueuedForDeletion())
            {
                node.QueueFree();
                freed?.Add(node);
                count++;
            }
        }

        return count;
    }

    // --- settings for the run, never saved ---------------------------------------------------------

    /// <summary>Applies <c>EMBERVALE_SPELLSHOTS_TIER</c> and <c>EMBERVALE_SPELLSHOTS_REDUCED</c> to the
    /// live settings and announces them. Returns what the effect quality resolved to.</summary>
    public static string ApplyEffectTier()
    {
        if (SettingsOrNull() is not { } settings)
        {
            return "no settings service; effect tier left alone";
        }

        string tier = OS.GetEnvironment(TierVariable).Trim();
        if (tier.Length > 0)
        {
            if (Enum.TryParse(tier, ignoreCase: true, out VfxTier parsed) && Enum.IsDefined(parsed))
            {
                settings.Current.SpellEffects = (int)parsed;
            }
            else
            {
                Log.Warn($"{TierVariable}='{tier}' is not performance|low|medium|high|ultra; tier left alone.");
            }
        }

        if (OS.GetEnvironment(ReducedVariable).Trim() == "1")
        {
            settings.Current.ReducedMotion = true;
        }

        EventBus.Instance?.Publish(new SettingsAppliedEvent(settings.Current));
        return $"tier={VfxQuality.Tier} reducedMotion={VfxQuality.ReducedMotion} " +
               $"(SpellEffects={settings.Current.SpellEffects}, RenderQuality={settings.Current.RenderQuality})";
    }

    /// <summary>Puts the player's camera in first or third person at once. The view is a setting, so
    /// it goes through the setting (the rig re-reads it whenever settings are announced) and then
    /// snaps, skipping the blend and the wait for room behind the player.</summary>
    public static void SetView(PlayerCharacter player, bool firstPerson)
    {
        if (SettingsOrNull() is { } settings && settings.Current.ThirdPersonCamera == firstPerson)
        {
            settings.Current.ThirdPersonCamera = !firstPerson;
            EventBus.Instance?.Publish(new SettingsAppliedEvent(settings.Current));
        }

        player.GetComponent<PlayerCameraRig>()?.SetFirstPerson(firstPerson, immediate: true);
    }

    public static void SetHour(float hour)
    {
        if (ServiceLocator.Instance is { } locator && locator.TryGet(out WorldClock clock) &&
            Mathf.Abs(clock.TimeOfDay - hour) > 0.05f)
        {
            clock.SetTimeOfDay(hour);
        }
    }

    public static void ClearWeather()
    {
        if (ServiceLocator.Instance is { } locator && locator.TryGet(out WeatherDirector weather) &&
            weather.Current?.Id != ClearWeatherId)
        {
            weather.Force(ClearWeatherId);
        }
    }

    // --- the player --------------------------------------------------------------------------------

    /// <summary>Makes the session safe to drive: no autosave for the rest of the process, the mouse
    /// no longer turns the view, and health, mana and stamina pools deep enough that no shot ends in a
    /// death, a refused cast or a winded sprint.</summary>
    public static void PreparePlayer(PlayerCharacter player)
    {
        AutosaveService.Suppressed = true;
        if (player.GetComponent<PlayerLookInput>() is { } look)
        {
            look.ProcessMode = Node.ProcessModeEnum.Disabled;
        }

        Deepen(player);
    }

    /// <summary>Adds a very large flat bonus to an actor's pools (once) and fills them.</summary>
    public static void Deepen(IEntity actor)
    {
        if (actor.GetComponent<StatsComponent>() is not { } stats)
        {
            return;
        }

        foreach (StatType pool in new[] { StatType.Health, StatType.Mana, StatType.Stamina })
        {
            Stat stat = stats.GetStat(pool);
            stat.RemoveModifiersFromSource(BoostSource);
            stat.AddModifier(new StatModifier(StatBoost, ModifierType.Flat, BoostSource));
        }

        stats.RefillResources();
    }

    /// <summary>Back to a clean caster: no cast in progress, no cooldowns, no statuses, full pools.
    /// Cooldowns have no public reset, so the component's own save is loaded back without them.</summary>
    public static void ResetCaster(IEntity actor)
    {
        if (actor.GetComponent<SpellcastingComponent>() is { } casting)
        {
            casting.CancelCast();
            Godot.Collections.Dictionary state = casting.Save();
            state.Remove("cooldowns");
            casting.Load(state);
        }

        if (actor.GetComponent<StatusEffectsComponent>() is { } statuses)
        {
            var ids = new List<string>();
            foreach (StatusEffect effect in statuses.ActiveEffects)
            {
                ids.Add(effect.Definition.Id);
            }

            foreach (string id in ids)
            {
                statuses.Consume(id);
            }
        }

        actor.GetComponent<StatsComponent>()?.RefillResources();
    }

    /// <summary>Stands a body on the ground at <paramref name="feet"/> facing <paramref name="forward"/>.</summary>
    public static void Place(CharacterBody3D body, Vector3 feet, Vector3 forward)
    {
        body.GlobalPosition = feet + (Vector3.Up * 0.05f);
        body.Velocity = Vector3.Zero;
        Vector3 flat = new(forward.X, 0f, forward.Z);
        if (flat.LengthSquared() > 1e-4f)
        {
            body.LookAt(body.GlobalPosition + flat.Normalized(), Vector3.Up);
        }
    }

    /// <summary>Turns the body and tilts the view one step so the crosshair comes to rest on
    /// <paramref name="point"/>. Called every physics tick it converges in a few, in either view: in
    /// third person the camera sits over the shoulder, so facing the body at a target is not aiming
    /// at it.</summary>
    public static void AimCameraAt(PlayerCharacter player, Vector3 point)
    {
        if (player.GetComponent<PlayerCameraRig>() is not { Camera: { } camera } rig ||
            !GodotObject.IsInstanceValid(camera))
        {
            return;
        }

        Vector3 to = point - camera.GlobalPosition;
        Vector3 forward = -camera.GlobalBasis.Z;
        Vector3 toFlat = new(to.X, 0f, to.Z);
        Vector3 forwardFlat = new(forward.X, 0f, forward.Z);
        if (toFlat.LengthSquared() < 0.01f || forwardFlat.LengthSquared() < 1e-4f)
        {
            return;
        }

        player.RotateY(forwardFlat.SignedAngleTo(toFlat, Vector3.Up));
        float want = Mathf.Atan2(to.Y, toFlat.Length());
        float have = Mathf.Atan2(forward.Y, forwardFlat.Length());

        // The rig subtracts a step from the pitch (SettingsMath.ApplyPitch, not inverted).
        rig.ApplyPitchStep(have - want, invertY: false);
    }

    /// <summary>Sets the view's pitch outright, in radians (positive looks up), within the rig's limit.</summary>
    public static void SetPitch(PlayerCharacter player, float pitch)
    {
        if (player.GetComponent<PlayerCameraRig>() is { CameraPivot: { } pivot } rig)
        {
            rig.ApplyPitchStep(pivot.Rotation.X - pitch, invertY: false);
        }
    }

    /// <summary>Lets go of every action a harness holds down.</summary>
    public static void ReleaseInputs()
    {
        foreach (StringName action in new[]
                 {
                     InputActions.MoveForward, InputActions.MoveBack, InputActions.MoveLeft, InputActions.MoveRight,
                     InputActions.Sprint, InputActions.Cast, InputActions.Attack, InputActions.Block,
                 })
        {
            Godot.Input.ActionRelease(action);
        }
    }

    /// <summary>Why the input router may not be reading the player, for a log line.</summary>
    public static string ControlState() =>
        $"playing={GameManager.Instance is { IsPlaying: true }} menuOpen={UiState.MenuOpen} wheelOpen={PlayGate.WheelOpen}";

    // --- actors ------------------------------------------------------------------------------------

    /// <summary>A practice target with its feet at <paramref name="feet"/>: the developer dummy's
    /// build (stats, a combat component on its own team, statuses, a solid body and a hurtbox) with a
    /// health pool nothing in the spell list can empty.</summary>
    public static Entity SpawnTarget(Node parent, Vector3 feet)
    {
        var target = new Entity
        {
            Name = "ShotTarget",
            DisplayName = "Shot Target",
            TemplateId = "debug.shot_target",
        };
        target.AddChild(new StatsComponent
        {
            Name = "Stats",
            Attributes = ResidentResources.Load<AttributeSet>(TargetAttributesPath) ?? AttributeSet.CreateDefault(),
        });
        target.AddChild(new CombatComponent { Name = "Combat", Team = 2 });
        target.AddChild(new StatusEffectsComponent { Name = "StatusEffects" });
        target.AddChild(new StatusEffectVfxComponent { Name = "StatusVfx" });

        // The entity's origin is its capsule centre; the model's is its feet.
        if (GD.Load<PackedScene>(ModelAssets.TrainingDummy)?.Instantiate() is Node3D visual)
        {
            visual.Name = "Mesh";
            visual.Position = new Vector3(0f, -1f, 0f);
            target.AddChild(visual);
        }
        else
        {
            target.AddChild(new MeshInstance3D
            {
                Name = "Mesh",
                Mesh = new CapsuleMesh { Radius = 0.4f, Height = 1.8f },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.70f, 0.30f, 0.28f) },
            });
        }

        var collider = new StaticBody3D { Name = "Collider" };
        collider.AddChild(new CollisionShape3D { Shape = new CapsuleShape3D { Radius = 0.4f, Height = 1.8f } });
        target.AddChild(collider);

        var hurtbox = new Hurtbox { Name = "Hurtbox" };
        hurtbox.AddChild(new CollisionShape3D { Shape = new CapsuleShape3D { Radius = 0.4f, Height = 1.8f } });
        target.AddChild(hurtbox);

        parent.AddChild(target);
        target.GlobalPosition = feet + Vector3.Up;
        Deepen(target);
        return target;
    }

    public static void Free(Node? node)
    {
        if (node != null && GodotObject.IsInstanceValid(node) && !node.IsQueuedForDeletion())
        {
            node.QueueFree();
        }
    }

    // --- ground ------------------------------------------------------------------------------------

    /// <summary>
    /// Finds a level, unobstructed strip <paramref name="length"/> metres long and
    /// <paramref name="halfWidth"/> to either side, as near <paramref name="near"/> as there is one.
    /// The nearest strip that is level within 0.3 m, has nothing standing in it and nothing over it
    /// wins; failing that the best one found is returned with <see cref="ShotLane.Clear"/> false, so a
    /// run still photographs something and its log says the ground was a compromise.
    ///
    /// Reads the physics space, so call it from a physics tick.
    /// </summary>
    public static ShotLane FindLane(CollisionObject3D near, float length, float halfWidth)
    {
        PhysicsDirectSpaceState3D space = near.GetWorld3D().DirectSpaceState;
        var query = new PhysicsRayQueryParameters3D
        {
            CollisionMask = CombatLayers.WorldStatic,
            CollideWithAreas = false,
            CollideWithBodies = true,
            Exclude = new Godot.Collections.Array<Rid> { near.GetRid() },
        };

        Vector3 origin = near.GlobalPosition;
        ShotLane best = new(origin, -near.GlobalBasis.Z.Normalized(), float.MaxValue, false);
        float bestScore = float.MaxValue;

        foreach (float ring in SearchRings)
        {
            int spots = ring <= 0f ? 1 : 8;
            for (int spot = 0; spot < spots; spot++)
            {
                Vector3 centre = origin + (Vector3.Forward.Rotated(Vector3.Up, spot * Mathf.Tau / 8f) * ring);
                for (int turn = 0; turn < 8; turn++)
                {
                    Vector3 forward = Vector3.Forward.Rotated(Vector3.Up, turn * Mathf.Tau / 8f);
                    Vector3 start = centre - (forward * (length * 0.5f));
                    if (Survey(space, query, start, forward, length, halfWidth, origin.Y) is not { } lane)
                    {
                        continue;
                    }

                    if (lane.Clear && lane.Spread <= LevelSpread)
                    {
                        return lane;
                    }

                    float score = lane.Spread + (lane.Clear ? 0f : 5f);
                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = lane;
                    }
                }
            }
        }

        return best with { Clear = false };
    }

    private static ShotLane? Survey(
        PhysicsDirectSpaceState3D space, PhysicsRayQueryParameters3D query,
        Vector3 start, Vector3 forward, float length, float halfWidth, float nearHeight)
    {
        Vector3 right = forward.Cross(Vector3.Up).Normalized();
        float low = float.MaxValue;
        float high = float.MinValue;
        float startHeight = 0f;
        for (float along = 0f; along <= length + 0.01f; along += 2f)
        {
            for (int side = -1; side <= 1; side++)
            {
                if (GroundHeight(space, query, start + (forward * along) + (right * (side * halfWidth))) is not { } height)
                {
                    return null;
                }

                if (along == 0f && side == 0)
                {
                    startHeight = height;
                }

                low = Mathf.Min(low, height);
                high = Mathf.Max(high, height);
            }
        }

        // A strip far above or below where the player stands is a rooftop, a cellar or a lake bed.
        if (Mathf.Abs(startHeight - nearHeight) > 6f)
        {
            return null;
        }

        Vector3 from = new(start.X, startHeight, start.Z);
        bool clear = true;
        foreach (float side in new[] { -halfWidth * 0.5f, 0f, halfWidth * 0.5f })
        {
            foreach (float height in new[] { 0.6f, 1.2f, 1.9f })
            {
                Vector3 a = from + (right * side) + (Vector3.Up * (height + (high - startHeight)));
                clear &= !Hits(space, query, a + (forward * 0.3f), a + (forward * length));
            }
        }

        Vector3 middle = from + (forward * (length * 0.5f)) + (Vector3.Up * ((high - startHeight) + 0.5f));
        clear &= !Hits(space, query, middle, middle + (Vector3.Up * 14f));
        return new ShotLane(from, forward, high - low, clear);
    }

    /// <summary>The ground height under a point, looking down from 6 m above it; null over a void.</summary>
    public static float? GroundHeight(PhysicsDirectSpaceState3D space, PhysicsRayQueryParameters3D query, Vector3 point)
    {
        query.From = point + (Vector3.Up * 6f);
        query.To = point + (Vector3.Down * 12f);
        Godot.Collections.Dictionary hit = space.IntersectRay(query);
        return hit.Count > 0 && hit.TryGetValue("position", out Variant position) ? position.AsVector3().Y : null;
    }

    /// <summary>A lane point dropped onto the ground under it (the lane's own height over a void).</summary>
    public static Vector3 OnGround(CollisionObject3D context, Vector3 point)
    {
        var query = new PhysicsRayQueryParameters3D
        {
            CollisionMask = CombatLayers.WorldStatic,
            CollideWithAreas = false,
            CollideWithBodies = true,
            Exclude = new Godot.Collections.Array<Rid> { context.GetRid() },
        };
        float? height = GroundHeight(context.GetWorld3D().DirectSpaceState, query, point);
        return new Vector3(point.X, height ?? point.Y, point.Z);
    }

    private static bool Hits(PhysicsDirectSpaceState3D space, PhysicsRayQueryParameters3D query, Vector3 from, Vector3 to)
    {
        query.From = from;
        query.To = to;
        return space.IntersectRay(query).Count > 0;
    }
}
