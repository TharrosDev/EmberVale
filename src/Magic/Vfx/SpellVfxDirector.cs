using System.Collections.Generic;
using Embervale.Core.Events;
using Embervale.Core.Pooling;
using Embervale.Core.Services;
using Embervale.Settings;
using Godot;

namespace Embervale.Magic.Vfx;

/// <summary>
/// The session's owner of spell effects. Everything <see cref="SpellVfx"/> draws is a child of
/// <see cref="VfxRoot"/> and never of an actor's body, so an effect outlives the swing that made it
/// and dies with the session. The director owns:
///
/// <list type="bullet">
/// <item><b>the pools</b>: one <see cref="NodePool{T}"/> per block (and one per particle preset),
/// cleared as it leaves the tree;</item>
/// <item><b>the budget</b>: a <see cref="VfxLedger"/> of live effect groups and lit lights, the
/// queue of ground marks, and the distance rule that decides how much of an effect is drawn at all
/// (<see cref="Open"/>);</item>
/// <item><b>the camera</b>, read once a frame for every effect that needs it;</item>
/// <item><b>the screen flash</b> (<see cref="VfxScreen"/>);</item>
/// <item><b>the shader warm-up</b>: the first frames with a camera draw one of every block, tiny, in
/// front of it, so the first fireball of a session does not compile six shaders mid-fight.</item>
/// </list>
///
/// It keeps <see cref="VfxQuality"/> in step with the settings and frees every live effect when a
/// load begins. On a headless display it still exists and still binds, but <see cref="Enabled"/> is
/// false, the facade draws nothing, and no pool, texture or shader is ever built.
/// </summary>
public partial class SpellVfxDirector : Node
{
    /// <summary>Frames the warm-up blocks stay in front of the camera.</summary>
    public const int WarmupFrames = 3;

    /// <summary>The scale the warm-up draws at: on screen, and too small to see.</summary>
    public const float WarmupScale = 0.001f;

    /// <summary>Slots in the table of emitter pools: the recipe presets, then the extra emitters.</summary>
    private const int ParticleKinds = VfxBurstPresets.SlotCount;

    private readonly List<VfxEffect> _live = new();
    private readonly List<VfxHandle<VfxGroundMark>> _marks = new();
    private readonly NodePool<VfxBurst>?[] _bursts = new NodePool<VfxBurst>?[ParticleKinds];
    private NodePool<VfxFlare>? _flares;
    private NodePool<VfxBolt>? _bolts;
    private NodePool<VfxGroundMark>? _markPool;
    private NodePool<VfxDistortion>? _distortions;
    private NodePool<VfxShell>? _shells;
    private NodePool<VfxDisc>? _discs;
    private NodePool<VfxMotif>? _motifs;
    private VfxScreen? _screen;
    private int _warmFramesLeft = -1;
    private int _warmGroup;
    private bool _warmed;
    private uint _seed = 0x51ED270Bu;

    /// <summary>The parent of every spell effect.</summary>
    public Node3D VfxRoot { get; }

    /// <summary>False on a headless display (or with a shader missing): nothing is drawn, so
    /// nothing is built.</summary>
    public bool Enabled { get; private set; }

    /// <summary>True once the shader warm-up has run.</summary>
    public bool Warmed => _warmed;

    /// <summary>Live effect groups and lit lights.</summary>
    internal VfxLedger Ledger { get; } = new();

    /// <summary>Seconds this director has been processing. Pauses with the tree.</summary>
    internal double Now { get; private set; }

    internal bool HasCamera { get; private set; }

    internal Vector3 CameraPosition { get; private set; }

    internal Vector3 CameraForward { get; private set; } = Vector3.Forward;

    /// <summary>The camera's axes, for an effect anchored in the view (the first-person hand).</summary>
    internal Basis CameraBasis { get; private set; } = Basis.Identity;

    /// <summary>Builds <see cref="VfxRoot"/> here rather than in <c>_Ready</c>, so it exists for any
    /// caller that reaches the director before the tree has readied it.</summary>
    public SpellVfxDirector()
    {
        VfxRoot = new Node3D { Name = "VfxRoot" };
        AddChild(VfxRoot);
    }

    public override void _Ready()
    {
        Enabled = DisplayServer.GetName() != "headless" && VfxMaterials.Load();

        // The plain flashes are drawn under VfxRoot and nowhere else, so their pool is this node's.
        SpellFlash.OpenPool();
        SpellVfx.Bind(this);

        if (ServiceLocator.Instance is { } locator && locator.TryGet(out SettingsService settings))
        {
            ApplySettings(settings.Current);
        }

        if (Enabled)
        {
            BuildPools();
            _screen = new VfxScreen();
            AddChild(_screen);
        }

        EventBus? bus = EventBus.Instance;
        bus?.Subscribe<SettingsAppliedEvent>(OnSettingsApplied);
        bus?.Subscribe<GameLoadingEvent>(OnGameLoading);
    }

    public override void _ExitTree()
    {
        EventBus? bus = EventBus.Instance;
        bus?.Unsubscribe<SettingsAppliedEvent>(OnSettingsApplied);
        bus?.Unsubscribe<GameLoadingEvent>(OnGameLoading);

        // The effects under VfxRoot go with the tree; none of them may run or reach a pool again.
        Forget();
        SpellVfx.Clear();
        SpellVfx.Unbind(this);
        ClearPools();
        SpellFlash.ClosePool();
        VfxMaterials.Release();
        VfxTextures.Release();
    }

    public override void _Process(double delta)
    {
        if (!Enabled)
        {
            return;
        }

        Now += delta;
        Camera3D? camera = GetViewport()?.GetCamera3D();
        HasCamera = camera != null && IsInstanceValid(camera) && camera.IsInsideTree();
        if (HasCamera)
        {
            CameraPosition = camera!.GlobalPosition;
            CameraBasis = camera.GlobalBasis.Orthonormalized();
            CameraForward = -CameraBasis.Z;
        }

        TickWarmup();
    }

    /// <summary>Frees every live effect. An effect belongs to the timeline that made it, and a load
    /// abandons that timeline.</summary>
    public void KillAll()
    {
        Forget();
        for (int i = VfxRoot.GetChildCount() - 1; i >= 0; i--)
        {
            VfxRoot.GetChild(i).QueueFree();
        }

        _screen?.Cut();
        SpellVfx.Clear();
    }

    // --- spawning ----------------------------------------------------------------------------------

    /// <summary>
    /// Opens one effect at <paramref name="position"/>: decides how much of it this far from the
    /// camera is drawn, makes room in the live-effect budget, and returns the spawner its blocks are
    /// built through. An <paramref name="essential"/> effect (a wind-up aura, a telegraph, a zone, a
    /// bolt in flight) is never recycled, takes no place in the budget, and is not thinned by
    /// distance unless <paramref name="measured"/> says its position is known and should be (a bolt:
    /// one launched too far away to matter is left to its plain sphere). A
    /// <paramref name="sustained"/> one that is not essential (a beam) gives way only when no
    /// one-shot is left to take.
    /// </summary>
    internal VfxSpawner Open(
        Vector3 position, bool byPlayer, bool essential = false, bool sustained = false, bool measured = false)
    {
        if (!Enabled || !IsInsideTree())
        {
            return default;
        }

        VfxDetail detail = (essential && !measured) || !HasCamera
            ? VfxDetail.Full
            : VfxBudgetRules.DetailAt(VfxQuality.Tier, position.DistanceTo(CameraPosition));
        if (detail == VfxDetail.None)
        {
            return default;
        }

        if (!essential)
        {
            MakeRoom();
        }

        return new VfxSpawner(this, Ledger.NextId(), detail, byPlayer, essential, sustained);
    }

    /// <summary>A seed for a bolt's shape. Sequential, so two bolts in one frame differ.</summary>
    internal int NextSeed() => unchecked((int)VfxBoltPath.Next(ref _seed));

    /// <summary>Metres from the camera, or 0 when there is none.</summary>
    internal float DistanceToCamera(Vector3 position) => HasCamera ? position.DistanceTo(CameraPosition) : 0f;

    internal VfxFlare AddFlare(in VfxSpawner spawner) => Add(_flares!, spawner);

    internal VfxBurst? AddBurst(VfxParticles kind, in VfxSpawner spawner) => AddBurst((int)kind, spawner);

    internal VfxBurst? AddBurst(VfxEmitter kind, in VfxSpawner spawner) => AddBurst((int)kind, spawner);

    private VfxBurst? AddBurst(int slot, in VfxSpawner spawner) =>
        slot <= 0 || slot >= ParticleKinds || _bursts[slot] is not { } pool ? null : Add(pool, spawner);

    internal VfxBolt AddBolt(in VfxSpawner spawner) => Add(_bolts!, spawner);

    internal VfxDistortion AddDistortion(in VfxSpawner spawner) => Add(_distortions!, spawner);

    internal VfxShell AddShell(in VfxSpawner spawner) => Add(_shells!, spawner);

    internal VfxDisc AddDisc(in VfxSpawner spawner) => Add(_discs!, spawner);

    internal VfxMotif AddMotif(in VfxSpawner spawner) => Add(_motifs!, spawner);

    /// <summary>The screen-edge shimmer, within the limits in <see cref="VfxScreenRules"/>.</summary>
    internal bool ScreenEdge(Color school, float strength, float seconds) =>
        _screen != null && IsInstanceValid(_screen) && _screen.Edge(school, strength, seconds);

    /// <summary>A ground mark: not counted as a live effect, and kept within its own budget by
    /// fading the oldest mark when a new one would exceed it.</summary>
    internal VfxGroundMark? AddMark(in VfxSpawner spawner)
    {
        int allowed = VfxQuality.Budget.GroundMarks;
        if (allowed <= 0)
        {
            return null;
        }

        for (int i = _marks.Count - 1; i >= 0; i--)
        {
            if (!_marks[i].IsLive)
            {
                _marks.RemoveAt(i);
            }
        }

        while (_marks.Count >= allowed)
        {
            _marks[0].Stop();
            _marks.RemoveAt(0);
        }

        VfxGroundMark mark = _markPool!.Get();
        VfxRoot.AddChild(mark);
        mark.Begin(this, 0);
        if (spawner.Late)
        {
            mark.Defer();
        }

        _live.Add(mark);
        _marks.Add(new VfxHandle<VfxGroundMark>(mark));
        return mark;
    }

    /// <summary>The screen flash, within every limit in <see cref="VfxScreenRules"/>.</summary>
    internal bool ScreenFlash(Color school, float strength, bool byPlayer, bool hitsPlayer) =>
        _screen != null && IsInstanceValid(_screen) && _screen.Flash(school, strength, byPlayer, hitsPlayer);

    /// <summary>An effect ended: off the live list and out of the ledger. Called by the effect.</summary>
    internal void Retire(VfxEffect effect)
    {
        _live.Remove(effect);
        Ledger.BlockRemoved(effect.Group);
    }

    /// <summary>Ends every block of one effect group now.</summary>
    internal void KillGroup(int group)
    {
        if (group == 0)
        {
            return;
        }

        for (int i = _live.Count - 1; i >= 0; i--)
        {
            if (i < _live.Count && _live[i].Group == group)
            {
                _live[i].Kill();
            }
        }
    }

    /// <summary>Blocks alive now, for the perf scenario and a probe.</summary>
    public int LiveBlocks => _live.Count;

    /// <summary>Effect groups alive now, essential ones included.</summary>
    public int LiveEffects => Ledger.LiveGroups;

    /// <summary>Effect groups alive now that count against the live-effect budget.</summary>
    public int BudgetedEffects => Ledger.BudgetedGroups;

    private T Add<T>(NodePool<T> pool, in VfxSpawner spawner)
        where T : VfxEffect
    {
        T effect = pool.Get();
        VfxRoot.AddChild(effect);
        effect.Begin(this, spawner.Group, spawner.Player);
        if (spawner.Late)
        {
            effect.Defer();
        }

        _live.Add(effect);
        Ledger.BlockAdded(spawner.Group, Now, spawner.Player, spawner.Essential, spawner.Sustained);
        return effect;
    }

    private void MakeRoom()
    {
        // Bounded: a budget that cannot be met (everything left is essential) is simply exceeded.
        for (int i = 0; i < 4 && VfxBudgetRules.OverBudget(VfxQuality.Tier, Ledger.BudgetedGroups); i++)
        {
            int victim = Ledger.PickVictim(Now);
            if (victim == 0)
            {
                return;
            }

            KillGroup(victim);
        }
    }

    /// <summary>Drops every live effect from the books without touching a pool: their nodes are
    /// about to be freed.</summary>
    private void Forget()
    {
        for (int i = 0; i < _live.Count; i++)
        {
            if (IsInstanceValid(_live[i]))
            {
                _live[i].Abandon();
            }
        }

        _live.Clear();
        _marks.Clear();
        Ledger.Forget();
        _warmFramesLeft = -1;
        _warmGroup = 0;
    }

    // --- pools -------------------------------------------------------------------------------------

    private void BuildPools()
    {
        NodePool<VfxFlare> flares = null!;
        flares = new NodePool<VfxFlare>(() => new VfxFlare { Reclaim = e => flares.Return((VfxFlare)e) }, prewarm: 6, maxRetained: 48);
        _flares = flares;

        for (int kind = 1; kind < ParticleKinds; kind++)
        {
            if (VfxBurstPresets.ForSlot(kind).Amount <= 0)
            {
                continue;
            }

            int slot = kind;
            NodePool<VfxBurst> bursts = null!;
            bursts = new NodePool<VfxBurst>(
                () => NewBurst(slot, e => bursts.Return((VfxBurst)e)), prewarm: 1, maxRetained: 16);
            _bursts[kind] = bursts;
        }

        NodePool<VfxBolt> bolts = null!;
        bolts = new NodePool<VfxBolt>(() => new VfxBolt { Reclaim = e => bolts.Return((VfxBolt)e) }, prewarm: 2, maxRetained: 24);
        _bolts = bolts;

        NodePool<VfxGroundMark> marks = null!;
        marks = new NodePool<VfxGroundMark>(
            () => new VfxGroundMark { Reclaim = e => marks.Return((VfxGroundMark)e) }, prewarm: 0, maxRetained: 24);
        _markPool = marks;

        NodePool<VfxDistortion> distortions = null!;
        distortions = new NodePool<VfxDistortion>(
            () => new VfxDistortion { Reclaim = e => distortions.Return((VfxDistortion)e) }, prewarm: 0, maxRetained: 6);
        _distortions = distortions;

        NodePool<VfxShell> shells = null!;
        shells = new NodePool<VfxShell>(() => new VfxShell { Reclaim = e => shells.Return((VfxShell)e) }, prewarm: 2, maxRetained: 16);
        _shells = shells;

        NodePool<VfxDisc> discs = null!;
        discs = new NodePool<VfxDisc>(() => new VfxDisc { Reclaim = e => discs.Return((VfxDisc)e) }, prewarm: 1, maxRetained: 16);
        _discs = discs;

        NodePool<VfxMotif> motifs = null!;
        motifs = new NodePool<VfxMotif>(() => new VfxMotif { Reclaim = e => motifs.Return((VfxMotif)e) }, prewarm: 1, maxRetained: 16);
        _motifs = motifs;
    }

    private static VfxBurst NewBurst(int slot, System.Action<VfxEffect> reclaim)
    {
        VfxBurst burst = slot < (int)VfxEmitter.Flame ? new VfxBurst((VfxParticles)slot) : new VfxBurst((VfxEmitter)slot);
        burst.Reclaim = reclaim;
        return burst;
    }

    private void ClearPools()
    {
        _flares?.Clear();
        _flares = null;
        for (int i = 0; i < _bursts.Length; i++)
        {
            _bursts[i]?.Clear();
            _bursts[i] = null;
        }

        _bolts?.Clear();
        _bolts = null;
        _markPool?.Clear();
        _markPool = null;
        _distortions?.Clear();
        _distortions = null;
        _shells?.Clear();
        _shells = null;
        _discs?.Clear();
        _discs = null;
        _motifs?.Clear();
        _motifs = null;
    }

    // --- shader warm-up ----------------------------------------------------------------------------

    /// <summary>
    /// One of every block, in the frustum, at <see cref="WarmupScale"/>, for
    /// <see cref="WarmupFrames"/> frames: every shader, every mesh it is drawn on and every particle
    /// preset reaches the renderer before a fight needs it. Runs once, on the first frames the
    /// session has a camera (world entry).
    /// </summary>
    private void TickWarmup()
    {
        if (_warmed || !HasCamera)
        {
            return;
        }

        if (_warmFramesLeft < 0)
        {
            SpawnWarmup();
            _warmFramesLeft = WarmupFrames;
            return;
        }

        // Kept in front of a camera that may be moving.
        Vector3 at = CameraPosition + (CameraForward * 2f);
        for (int i = 0; i < _live.Count; i++)
        {
            if (_live[i].Group == _warmGroup && _live[i] is not VfxBolt)
            {
                _live[i].GlobalPosition = at;
            }
        }

        if (--_warmFramesLeft <= 0)
        {
            KillGroup(_warmGroup);
            for (int i = _marks.Count - 1; i >= 0; i--)
            {
                _marks[i].Kill();
            }

            _marks.Clear();
            _warmGroup = 0;
            _warmed = true;
        }
    }

    private void SpawnWarmup()
    {
        var spawner = new VfxSpawner(this, Ledger.NextId(), VfxDetail.Full, byPlayer: true, essential: true, sustained: true);
        _warmGroup = spawner.Group;
        Vector3 at = CameraPosition + (CameraForward * 2f);
        VfxSchoolColors colors = VfxPalette.For(Combat.DamageType.Fire).Scaled(0.02f, 0.02f);
        float tiny = WarmupScale;

        // The ground patterns are drawn in code on first use; draw them now, not under a telegraph.
        _ = VfxTextures.Pattern(VfxDiscPattern.Frost);
        _ = VfxTextures.Pattern(VfxDiscPattern.Cracks);
        _ = VfxTextures.Pattern(VfxDiscPattern.Roots);

        VfxFlareSpec flare = VfxFlareSpec.At(at, tiny, colors);
        flare.Sustain = true;
        flare.Ring = true;
        flare.RingRadius = tiny;
        flare.Streak = true;
        flare.Light = true;
        flare.LightRange = 0.05f;
        spawner.Flare(flare);

        // The rays are a one-shot's: a second, brief flare so their texture reaches the renderer too.
        VfxFlareSpec rays = VfxFlareSpec.At(at, tiny, colors);
        rays.Rays = true;
        rays.Life = 0.2f;
        spawner.Flare(rays);

        for (int kind = 1; kind < ParticleKinds; kind++)
        {
            VfxBurstSpec burst = VfxBurstSpec.At(at, colors, 0.05f);
            burst.SizeScale = tiny;
            burst.SpeedScale = 0.01f;
            burst.Extents = Vector3.One * tiny;
            burst.Continuous = true;
            if (kind < (int)VfxEmitter.Flame)
            {
                spawner.Burst((VfxParticles)kind, burst);
            }
            else
            {
                spawner.Burst((VfxEmitter)kind, burst);
            }
        }

        spawner.Bolt(new VfxBoltSpec
        {
            Mode = VfxBoltMode.Beam,
            From = at,
            To = at + (Vector3.Up * tiny * 4f),
            Colors = colors,
            Width = tiny,
            Segments = 3,
            Seed = 1,
        });

        VfxShellSpec sphere = VfxShellSpec.Sphere(at, tiny, colors);
        sphere.Sustain = true;
        spawner.Shell(sphere);
        VfxShellSpec sheet = VfxShellSpec.Sheet(at, tiny, tiny, colors);
        spawner.Shell(sheet);
        VfxShellSpec ice = VfxShellSpec.IceWall(at, tiny, tiny, colors);
        spawner.Shell(ice);
        VfxShellSpec post = VfxShellSpec.Sphere(at, tiny, colors);
        post.Shape = VfxShellShape.Post;
        post.Size = Vector3.One * tiny;
        post.Sustain = true;
        spawner.Shell(post);

        VfxDiscSpec disc = VfxDiscSpec.At(at, tiny, colors);
        disc.Sustain = true;
        disc.Rune = true;
        spawner.Disc(disc);

        // The instanced sprites of a motif, and the shaped sprites only a motif or a school's
        // variant of a preset draws (painted in code on first use, like the ground patterns).
        VfxMotifSpec motif = VfxMotifSpec.At(at, colors, VfxSprite.Crystal, VfxMotion.Orbit, 3, tiny, tiny);
        motif.Sustain = true;
        motif.TrueSize = true;
        spawner.Motif(motif);
        _ = VfxTextures.Spark;
        _ = VfxTextures.Flake;
        _ = VfxTextures.Glint;
        _ = VfxTextures.Leaf;

        if (VfxQuality.Budget.Distortion)
        {
            spawner.Distortion(new VfxDistortionSpec { Position = at, Radius = tiny, Sustain = true, Strength = 0.0001f });
        }

        for (int mark = 1; mark <= (int)VfxMark.Roots; mark++)
        {
            spawner.Mark(new VfxGroundMarkSpec
            {
                Mark = (VfxMark)mark,
                Position = at,
                Size = tiny,
                Colors = colors,
                Sustain = true,
                Reach = tiny,
            });
        }
    }

    private void OnSettingsApplied(SettingsAppliedEvent e) => ApplySettings(e.Current);

    private void OnGameLoading(GameLoadingEvent _) => KillAll();

    private static void ApplySettings(Settings.Settings settings) =>
        VfxQuality.Apply(settings.SpellEffects, settings.RenderQuality, settings.ReducedMotion);
}

/// <summary>
/// The way one effect's blocks are built. <see cref="SpellVfxDirector.Open"/> hands one out per
/// effect, already carrying what the budget decided: how much detail this far from the camera, whose
/// effect it is, and the ledger group every block it builds is counted under.
///
/// <para>Each method builds one block and returns a handle to it, or an empty handle when that block
/// is not drawn (the spawner is empty, the distance leaves only flares, the tier has no ground marks
/// or no distortion). The caller never has to ask first: an empty handle ignores every call.</para>
///
/// <para>This is the whole block API. A recipe interpreter, a special case for one spell and the
/// shader warm-up all build effects through these same methods.</para>
/// </summary>
internal readonly struct VfxSpawner
{
    private readonly SpellVfxDirector? _director;

    internal VfxSpawner(
        SpellVfxDirector director, int group, VfxDetail detail, bool byPlayer, bool essential, bool sustained,
        bool late = false, Combat.DamageType? school = null)
    {
        _director = director;
        Group = group;
        Detail = detail;
        Player = byPlayer;
        Essential = essential;
        Sustained = sustained;
        Late = late;
        School = school;
    }

    /// <summary>The school this effect belongs to, when its opener said (<see cref="ForSchool"/>).
    /// A recipe preset thrown through a spawner that knows its school is drawn with that school's
    /// shape (<see cref="VfxBurstPresets.SchoolSprite"/>): thrown lightning is jagged, a mote of
    /// frost is a snowflake.</summary>
    public Combat.DamageType? School { get; }

    /// <summary>The ledger group of this effect.</summary>
    public int Group { get; }

    public VfxDetail Detail { get; }

    public bool Player { get; }

    public bool Essential { get; }

    public bool Sustained { get; }

    /// <summary>Blocks are held back until their first frame (see <see cref="VfxEffect.Defer"/>).</summary>
    public bool Late { get; }

    /// <summary>Nothing is drawn through this spawner: no director, headless, or too far away.</summary>
    public bool IsNone => _director == null || !GodotObject.IsInstanceValid(_director);

    /// <summary>Everything a plan asks for may be drawn (as opposed to the flare alone).</summary>
    public bool Full => !IsNone && Detail == VfxDetail.Full;

    public SpellVfxDirector? Director => IsNone ? null : _director;

    /// <summary>The same effect, with every block from here on held back until its first frame. For
    /// effects that follow a node its caller positions just after telling the effect layer.</summary>
    public VfxSpawner AsLate() =>
        IsNone ? this : new VfxSpawner(_director!, Group, Detail, Player, Essential, Sustained, late: true, School);

    /// <summary>The same effect, knowing which school it belongs to.</summary>
    public VfxSpawner ForSchool(Combat.DamageType school) =>
        IsNone ? this : new VfxSpawner(_director!, Group, Detail, Player, Essential, Sustained, Late, school);

    public VfxHandle<VfxFlare> Flare(VfxFlareSpec spec)
    {
        if (IsNone)
        {
            return default;
        }

        if (Detail != VfxDetail.Full)
        {
            // At a distance the flash is the whole effect: no ring to resolve, no light to pay for.
            spec.Ring = false;
            spec.Light = false;
            spec.Streak = false;
            if (spec.NoCore)
            {
                return default;
            }
        }

        VfxFlare flare = _director!.AddFlare(this);
        flare.Arm(spec);
        return new VfxHandle<VfxFlare>(flare);
    }

    public VfxHandle<VfxBurst> Burst(VfxParticles kind, in VfxBurstSpec spec)
    {
        if (!Full || kind == VfxParticles.None || spec.Density <= 0f)
        {
            return default;
        }

        VfxBurst? burst = _director!.AddBurst(kind, this);
        if (burst == null)
        {
            return default;
        }

        if (spec.Sprite == null && School is { } school)
        {
            VfxBurstSpec shaped = spec;
            shaped.Sprite = VfxBurstPresets.SchoolSprite(kind, school);
            burst.Arm(shaped);
        }
        else
        {
            burst.Arm(spec);
        }

        return new VfxHandle<VfxBurst>(burst);
    }

    /// <summary>One of the extra emitters (<see cref="VfxEmitter"/>): flame that cools to smoke,
    /// debris, ground mist, ice crystals, glints.</summary>
    public VfxHandle<VfxBurst> Burst(VfxEmitter kind, in VfxBurstSpec spec)
    {
        if (!Full || kind == VfxEmitter.None || spec.Density <= 0f)
        {
            return default;
        }

        VfxBurst? burst = _director!.AddBurst(kind, this);
        if (burst == null)
        {
            return default;
        }

        burst.Arm(spec);
        return new VfxHandle<VfxBurst>(burst);
    }

    public VfxHandle<VfxBolt> Bolt(VfxBoltSpec spec)
    {
        if (IsNone)
        {
            return default;
        }

        if (Detail != VfxDetail.Full)
        {
            spec.Branches = 0;
            spec.Segments = Mathf.Min(spec.Segments, 6);
        }

        VfxBolt bolt = _director!.AddBolt(this);
        bolt.Arm(spec);
        return new VfxHandle<VfxBolt>(bolt);
    }

    public VfxHandle<VfxGroundMark> Mark(in VfxGroundMarkSpec spec)
    {
        if (!Full || spec.Mark == VfxMark.None)
        {
            return default;
        }

        VfxGroundMark? mark = _director!.AddMark(this);
        if (mark == null)
        {
            return default;
        }

        mark.Arm(spec);
        return new VfxHandle<VfxGroundMark>(mark);
    }

    public VfxHandle<VfxDistortion> Distortion(in VfxDistortionSpec spec)
    {
        if (!Full || !VfxQuality.Budget.Distortion || VfxQuality.ReducedMotion)
        {
            return default;
        }

        VfxDistortion distortion = _director!.AddDistortion(this);
        distortion.Arm(spec);
        return new VfxHandle<VfxDistortion>(distortion);
    }

    public VfxHandle<VfxShell> Shell(in VfxShellSpec spec)
    {
        if (!Full)
        {
            return default;
        }

        VfxShell shell = _director!.AddShell(this);
        shell.Arm(spec);
        return new VfxHandle<VfxShell>(shell);
    }

    public VfxHandle<VfxDisc> Disc(in VfxDiscSpec spec)
    {
        if (!Full)
        {
            return default;
        }

        VfxDisc disc = _director!.AddDisc(this);
        disc.Arm(spec);
        return new VfxHandle<VfxDisc>(disc);
    }

    /// <summary>A handful of shaped sprites moving in a pattern (<see cref="VfxMotif"/>): flames in
    /// a hand, orbiting shards, crackling arcs, the shaped head of a bolt. One draw call.</summary>
    public VfxHandle<VfxMotif> Motif(in VfxMotifSpec spec)
    {
        if (!Full || spec.Count <= 0)
        {
            return default;
        }

        VfxMotif motif = _director!.AddMotif(this);
        motif.Arm(spec);
        return new VfxHandle<VfxMotif>(motif);
    }

    /// <summary>
    /// A shimmer in <paramref name="school"/>'s colour around the edges of the screen for
    /// <paramref name="seconds"/>, leaving the middle clear: what the local player sees of an effect
    /// that is on their own body in first person (a ward, a self-cast), where a shell or a ring would
    /// be a band across the view. Subject to the comfort limits; returns whether it was shown.
    /// </summary>
    public bool ScreenEdge(Color school, float strength, float seconds = 0.9f) =>
        !IsNone && _director!.ScreenEdge(school, strength, seconds);

    /// <summary>A screen flash of <paramref name="strength"/> (0..1). Subject to every comfort
    /// limit; returns whether it was shown.</summary>
    public bool Screen(Color school, float strength, bool hitsPlayer) =>
        !IsNone && _director!.ScreenFlash(school, strength, Player, hitsPlayer);
}
