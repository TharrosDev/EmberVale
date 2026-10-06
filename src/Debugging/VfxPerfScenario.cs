using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Embervale.Core.Diagnostics;
using Embervale.Enemies;
using Embervale.Magic;
using Embervale.Magic.Vfx;
using Embervale.Player;
using Godot;

namespace Embervale.Debugging;

/// <summary>
/// The spell-effect performance scenario: <c>godot --path . -- --vfxperf</c>. Eight casters stand in
/// a ring around the player and cycle the eight heaviest spells at it for
/// <c>EMBERVALE_VFXPERF_SECONDS</c> (default 20), at the effect tier
/// <c>EMBERVALE_SPELLSHOTS_TIER</c> names (and reduced motion with
/// <c>EMBERVALE_SPELLSHOTS_REDUCED=1</c>). It writes <c>vfxperf_&lt;tier&gt;.json</c> to the artifacts
/// directory and logs the same JSON on one line: frame time at the median, the 95th percentile and
/// the worst, for the whole run and for the steady part after every spell has been seen once; the
/// frame time of the first cast; the worst frame while each spell was first on screen; and the peak
/// effect-node, emitter, particle and light counts. Run WITHOUT <c>--headless</c>.
///
/// <para>V-sync and the frame cap are turned off for the run (on the window, not in the settings),
/// since a capped frame time measures the cap. The casters start 0.6 s apart, each with a different
/// spell, so the hitch of an effect drawn for the first time is not buried under seven others.
/// <c>EMBERVALE_VFXPERF_VIEW</c> picks the camera: <c>wide</c> (default; a raised camera with the
/// whole ring in view, the worst case), <c>tp</c> or <c>fp</c>.</para>
///
/// <para>Exits 1 when no cast began or nothing ever appeared under the effect director's
/// <c>VfxRoot</c>: a frame time for effects that did not draw is not a measurement.</para>
/// </summary>
public sealed partial class VfxPerfScenario : Node
{
    private const string Flag = "--vfxperf";
    private const string OutputDir = "user://vfx_perf";
    private const string SecondsVariable = "EMBERVALE_VFXPERF_SECONDS";
    private const string ViewVariable = "EMBERVALE_VFXPERF_VIEW";
    private const string CasterArchetypeId = "enemy.hollow_necromancer";

    private const int CasterCount = 8;
    private const float RingRadius = 10f;
    private const float LaneLength = 26f;
    private const float LaneHalfWidth = 10f;
    private const double CalmSeconds = 1.5;
    private const double BaselineSeconds = 3.0;
    private const double StaggerSeconds = 0.6;
    private const double ChannelSeconds = 1.5;
    private const double RestSeconds = 0.35;

    /// <summary>Seconds after a spell's first cast in which its effects are first drawn: long enough
    /// for the slowest (a charged meteor: charge, wind-up, fall, then what lingers).</summary>
    private const double FirstUseWindow = 4.0;

    private const int CensusEveryFrames = 6;

    private static readonly string[] SpellIds =
    {
        "spell.sunfall", "spell.blizzard", "spell.storm_conduit", "spell.flame_lance",
        "spell.frost_nova", "spell.dragon_breath", "spell.ball_lightning", "spell.pyre_wall",
    };

    private enum Phase
    {
        Settle, Stage, Baseline, Casting, Done,
    }

    private sealed class Caster
    {
        public Caster(EnemyEntity body, SpellcastingComponent casting, int next, double startAt)
        {
            Body = body;
            Casting = casting;
            Next = next;
            StartAt = startAt;
        }

        public EnemyEntity Body { get; }

        public SpellcastingComponent Casting { get; }

        public int Next;
        public double StartAt;
        public double RestUntil;
        public double HoldUntil;
        public bool Held;
        public SpellResource? Active;
    }

    private readonly List<Caster> _casters = new();
    private readonly List<SpellResource> _spells = new();
    private readonly List<double> _baseline = new();
    private readonly List<(double At, double Ms)> _frames = new();
    private readonly Dictionary<string, double> _firstCast = new();
    private readonly Dictionary<string, int> _casts = new();

    private Phase _phase = Phase.Settle;
    private int _settle;
    private double _seconds = 20.0;
    private double _clock;
    private ulong _lastTick;
    private ulong _samplingUsec;
    private double _baselineFrom;
    private double _baselineUntil;
    private double _castStart;
    private double _firstCastAt = -1;
    private int _frameIndex;
    private VfxCensus _peak;
    private int _peakParticles;
    private double _peakDrawCalls;
    private string _effects = string.Empty;
    private string _view = "wide";
    private bool _levelGround;
    private Camera3D? _camera;

    public override void _Ready()
    {
        // Pause-immune, as the capture harnesses are: nothing here should stall behind a menu.
        ProcessMode = ProcessModeEnum.Always;
        if (DisplayServer.GetName() == "headless")
        {
            Log.Error($"{Flag}: rendering-capable display required; do not use --headless");
            _phase = Phase.Done;
            GetTree().Quit(2);
            return;
        }

        DisplayServer.WindowSetSize(WindowSize());
        _settle = int.TryParse(OS.GetEnvironment("EMBERVALE_FRAMES"), out int frames) ? Math.Max(1, frames) : 90;
        if (double.TryParse(OS.GetEnvironment(SecondsVariable), NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds))
        {
            _seconds = Math.Clamp(seconds, 2.0, 600.0);
        }

        string view = OS.GetEnvironment(ViewVariable).Trim().ToLowerInvariant();
        _view = view is "tp" or "fp" ? view : "wide";
        Log.Info($"{Flag}: {CasterCount} casters for {_seconds:0.#} s, view={_view}.");
    }

    /// <summary>The capture harnesses' window size, so a frame time here is for the frame they photograph.</summary>
    private static Vector2I WindowSize()
    {
        string[] parts = OS.GetEnvironment("EMBERVALE_RES").Split('x');
        return parts.Length == 2 && int.TryParse(parts[0], out int w) && int.TryParse(parts[1], out int h) && w >= 640 && h >= 360
            ? new Vector2I(w, h)
            : new Vector2I(1280, 720);
    }

    // --- staging -----------------------------------------------------------------------------------

    public override void _PhysicsProcess(double delta)
    {
        if (_phase == Phase.Stage)
        {
            try
            {
                Stage();
            }
            catch (Exception e)
            {
                Fail($"staging threw {e.GetType().Name}: {e.Message}");
            }

            return;
        }

        if (_phase == Phase.Casting && ShotStage.Player() is { } player)
        {
            Vector3 chest = player.GlobalPosition + Vector3.Up;
            foreach (Caster caster in _casters)
            {
                Drive(caster, chest, delta);
            }
        }
    }

    private void Stage()
    {
        if (ShotStage.Player() is not { } player || GetTree().CurrentScene is not { } scene)
        {
            Fail("the player or the scene is missing");
            return;
        }

        if (EnemyArchetypeDatabase.Get(CasterArchetypeId) is not { } archetype)
        {
            Fail($"no archetype '{CasterArchetypeId}' to cast with");
            return;
        }

        foreach (string id in SpellIds)
        {
            if (SpellDatabase.Get(id) is { } spell)
            {
                _spells.Add(spell);
            }
            else
            {
                Log.Warn($"{Flag}: no spell '{id}'; the mix runs without it.");
            }
        }

        if (_spells.Count == 0)
        {
            Fail("none of the scenario's spells exist");
            return;
        }

        ShotStage.PreparePlayer(player);
        _effects = ShotStage.ApplyEffectTier();

        // After the settings are announced, and on the window only: a capped frame measures the cap.
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        Engine.MaxFps = 0;

        ShotLane lane = ShotStage.FindLane(player, LaneLength, LaneHalfWidth);
        _levelGround = lane.Clear;
        if (!lane.Clear)
        {
            Log.Warn($"{Flag}: no level, open ground {LaneLength:0} m across within reach of the save; using the best found " +
                     $"(level within {lane.Spread:0.00} m).");
        }

        Vector3 centre = ShotStage.OnGround(player, lane.At(LaneLength * 0.5f));
        ShotStage.ClearSpellNodes(GetTree());
        ShotStage.ResetCaster(player);
        ShotStage.ClearWeather();
        ShotStage.Place(player, centre, lane.Forward);
        ShotStage.SetPitch(player, 0f);
        if (_view == "wide")
        {
            // Near enough that the far side of the ring is inside the lowest tier's full-detail
            // distance (25 m): a camera further back would have the cheap tiers drawing flares only.
            _camera = new Camera3D { Name = "VfxPerfCamera", Fov = 75f };
            scene.AddChild(_camera);
            _camera.GlobalPosition = centre - (lane.Forward * (RingRadius + 3f)) + (Vector3.Up * 9f);
            _camera.LookAt(centre + Vector3.Up, Vector3.Up);
            _camera.MakeCurrent();
        }
        else
        {
            ShotStage.SetView(player, _view == "fp");
        }

        for (int i = 0; i < CasterCount; i++)
        {
            Vector3 outward = lane.Forward.Rotated(Vector3.Up, (i + 0.5f) * Mathf.Tau / CasterCount);
            Vector3 feet = ShotStage.OnGround(player, centre + (outward * RingRadius));
            EnemyEntity body = EnemyArchetypeFactory.Create(archetype, feet + (Vector3.Up * 0.04f));
            scene.AddChild(body);
            body.LookAt(new Vector3(centre.X, body.GlobalPosition.Y, centre.Z), Vector3.Up);
            if (body.GetNodeOrNull<Node>("AI") is { } ai)
            {
                ai.ProcessMode = ProcessModeEnum.Disabled;
            }

            if (body.GetComponent<SpellcastingComponent>() is not { } casting)
            {
                Fail($"'{CasterArchetypeId}' has no spellbook");
                return;
            }

            foreach (SpellResource spell in _spells)
            {
                casting.Teach(spell);
            }

            ShotStage.Deepen(body);
            _casters.Add(new Caster(body, casting, i % _spells.Count, i * StaggerSeconds));
        }

        _baselineFrom = _clock + CalmSeconds;
        _baselineUntil = _baselineFrom + BaselineSeconds;
        _phase = Phase.Baseline;
        Log.Info($"{Flag}: staged at {centre:0.0}; effects {_effects}; {ShotStage.ControlState()}.");
    }

    /// <summary>One caster's turn of the loop: hold a charge or a channel for its time, let it go,
    /// rest a moment, then begin the next spell in the mix that is off cooldown.</summary>
    private void Drive(Caster caster, Vector3 target, double delta)
    {
        if (!IsInstanceValid(caster.Body) || !IsInstanceValid(caster.Casting))
        {
            return;
        }

        SpellcastingComponent casting = caster.Casting;
        if (casting.AimNode is { } aim && IsInstanceValid(aim) && aim.GlobalPosition.DistanceSquaredTo(target) > 0.01f)
        {
            aim.LookAt(target, Vector3.Up);
        }

        double now = _clock - _castStart;
        if (caster.Active != null)
        {
            if (caster.Held)
            {
                casting.UpdateCast(delta);
                bool dropped = !casting.IsCharging && !casting.IsChanneling && casting.PendingSpell == null;
                if (now >= caster.HoldUntil || dropped)
                {
                    casting.EndCast();
                    caster.Held = false;
                }
            }

            if (!caster.Held && !casting.SelectionLocked)
            {
                caster.Active = null;
                caster.RestUntil = now + RestSeconds;
            }

            return;
        }

        if (now < caster.StartAt || now < caster.RestUntil)
        {
            return;
        }

        for (int i = 0; i < _spells.Count; i++)
        {
            int index = (caster.Next + i) % _spells.Count;
            SpellResource spell = _spells[index];
            if (!casting.CanCast(spell) || !casting.BeginCastById(spell.Id))
            {
                continue;
            }

            caster.Next = (index + 1) % _spells.Count;
            caster.Active = spell;
            caster.Held = spell.CastMode != CastMode.Instant;
            caster.HoldUntil = now + (spell.CastMode == CastMode.Charged ? spell.ChargeTime : ChannelSeconds);
            _casts[spell.Id] = _casts.GetValueOrDefault(spell.Id) + 1;
            _firstCast.TryAdd(spell.Id, now);
            if (_firstCastAt < 0)
            {
                _firstCastAt = now;
            }

            return;
        }
    }

    // --- measuring ---------------------------------------------------------------------------------

    public override void _Process(double delta)
    {
        ulong tick = Time.GetTicksUsec();

        // The scenario's own counting (a walk of everything under VfxRoot, every few frames) is taken
        // back out of the frame it ran in: at one frame in six it would otherwise sit in the 95th
        // percentile it is here to measure.
        ulong span = tick - _lastTick;
        double ms = _lastTick == 0 ? 0.0 : (span - Math.Min(_samplingUsec, span)) / 1000.0;
        _lastTick = tick;
        _samplingUsec = 0;
        _clock += delta;

        switch (_phase)
        {
            case Phase.Settle:
                if (--_settle <= 0)
                {
                    _phase = Phase.Stage;
                }

                break;
            case Phase.Baseline:
                if (_clock >= _baselineFrom)
                {
                    _baseline.Add(ms);
                }

                if (_clock >= _baselineUntil)
                {
                    _castStart = _clock;
                    _phase = Phase.Casting;
                    Log.Info($"{Flag}: baseline taken over {_baseline.Count} frame(s); casting.");
                }

                break;
            case Phase.Casting:
                _frames.Add((_clock - _castStart, ms));
                if (++_frameIndex % CensusEveryFrames == 0)
                {
                    ulong began = Time.GetTicksUsec();
                    Node3D? root = ShotStage.VfxRoot(GetTree());
                    _peak = _peak.Max(ShotStage.Census(root));
                    _peakParticles = Math.Max(_peakParticles, root != null ? Particles(root) : 0);
                    _peakDrawCalls = Math.Max(_peakDrawCalls, Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame));
                    _samplingUsec = Time.GetTicksUsec() - began;
                }

                if (_clock - _castStart >= _seconds)
                {
                    Finish();
                }

                break;
        }
    }

    /// <summary>Particles the emitting systems under a node are asked to keep alive.</summary>
    private static int Particles(Node node)
    {
        int total = 0;
        int count = node.GetChildCount();
        for (int i = 0; i < count; i++)
        {
            Node child = node.GetChild(i);
            if (child is GpuParticles3D { Emitting: true } gpu && gpu.IsVisibleInTree())
            {
                total += Mathf.RoundToInt(gpu.Amount * gpu.AmountRatio);
            }
            else if (child is CpuParticles3D { Emitting: true } cpu && cpu.IsVisibleInTree())
            {
                total += cpu.Amount;
            }

            total += Particles(child);
        }

        return total;
    }

    private void Finish()
    {
        _phase = Phase.Done;
        foreach (Caster caster in _casters)
        {
            if (IsInstanceValid(caster.Casting))
            {
                caster.Casting.CancelCast();
            }
        }

        // The steady part starts once the last caster's first spell has had its first-use window.
        double steadyFrom = ((CasterCount - 1) * StaggerSeconds) + FirstUseWindow;
        List<double> all = _frames.Select(f => f.Ms).ToList();
        List<double> steady = _frames.Where(f => f.At >= steadyFrom).Select(f => f.Ms).ToList();
        List<double> firstPass = _frames.Where(f => f.At < steadyFrom).Select(f => f.Ms).ToList();

        // The cast is begun in a physics tick; the frame it lands in is timed at the next frame, so
        // the worst of the three frames after it is the first cast's.
        double firstCastMs = _firstCastAt < 0
            ? 0.0
            : _frames.Where(f => f.At > _firstCastAt).Take(3).Select(f => f.Ms).DefaultIfEmpty(0.0).Max();

        var firstUse = new Dictionary<string, double>();
        foreach ((string id, double at) in _firstCast)
        {
            firstUse[id] = Round(_frames.Where(f => f.At > at && f.At <= at + FirstUseWindow).Select(f => f.Ms).DefaultIfEmpty(0.0).Max());
        }

        int casts = _casts.Values.Sum();
        string tier = VfxQuality.Tier.ToString().ToLowerInvariant();
        Vector2I size = DisplayServer.WindowGetSize();
        var result = new Dictionary<string, object>
        {
            ["suite"] = Flag,
            ["tier"] = tier,
            ["reducedMotion"] = VfxQuality.ReducedMotion,
            ["effects"] = _effects,
            ["view"] = _view,
            ["seconds"] = _seconds,
            ["resolution"] = new[] { size.X, size.Y },
            ["vsync"] = false,
            ["levelGround"] = _levelGround,
            ["casters"] = _casters.Count,
            ["casts"] = casts,
            ["castsBySpell"] = _casts,
            ["frames"] = all.Count,
            ["baselineMs"] = Stats(_baseline),
            ["frameMs"] = Stats(all),
            ["steadyMs"] = Stats(steady),
            ["firstPassMs"] = Stats(firstPass),
            ["firstCastFrameMs"] = Round(firstCastMs),
            ["firstUseMaxMsBySpell"] = firstUse,
            ["peakVfxNodes"] = _peak.Nodes,
            ["peakVfxVisible"] = _peak.Visible,
            ["peakParticleEmitters"] = _peak.Emitters,
            ["peakParticles"] = _peakParticles,
            ["peakVfxLights"] = _peak.Lights,
            ["peakDrawCalls"] = _peakDrawCalls,
            ["vfxRootFound"] = ShotStage.VfxRoot(GetTree()) != null,
            ["godot"] = Engine.GetVersionInfo()["string"].AsString(),
        };

        Log.Info($"{Flag}: result {System.Text.Json.JsonSerializer.Serialize(result)}");
        string artifacts = OS.GetEnvironment("EMBERVALE_ARTIFACTS");
        string directory = string.IsNullOrEmpty(artifacts) ? OutputDir : System.IO.Path.Combine(artifacts, Flag.TrimStart('-'));
        string path = $"{directory}/vfxperf_{tier}{(VfxQuality.ReducedMotion ? "_reduced" : string.Empty)}.json";
        bool written = false;
        if (DirAccess.MakeDirRecursiveAbsolute(directory) == Error.Ok)
        {
            using FileAccess? file = FileAccess.Open(path, FileAccess.ModeFlags.Write);
            if (file != null)
            {
                file.StoreString(System.Text.Json.JsonSerializer.Serialize(
                    result, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                written = true;
            }
        }

        if (!written)
        {
            Fail($"could not write {ProjectSettings.GlobalizePath(path)}");
            return;
        }

        Log.Info($"{Flag}: wrote {ProjectSettings.GlobalizePath(path)}");
        if (casts == 0)
        {
            Fail($"no cast began in {_seconds:0.#} s ({ShotStage.ControlState()})");
        }
        else if (!_peak.Exceeds(default))
        {
            Fail("nothing ever appeared under the effect director's VfxRoot: the frame times are for effects that did not draw");
        }
        else
        {
            GetTree().Quit(0);
        }
    }

    private static Dictionary<string, double> Stats(List<double> samples)
    {
        if (samples.Count == 0)
        {
            return new Dictionary<string, double> { ["p50"] = 0.0, ["p95"] = 0.0, ["max"] = 0.0, ["frames"] = 0.0 };
        }

        var sorted = new List<double>(samples);
        sorted.Sort();
        return new Dictionary<string, double>
        {
            ["p50"] = Round(sorted[(int)((sorted.Count - 1) * 0.5)]),
            ["p95"] = Round(sorted[(int)((sorted.Count - 1) * 0.95)]),
            ["max"] = Round(sorted[^1]),
            ["frames"] = sorted.Count,
        };
    }

    private static double Round(double value) => Math.Round(value, 2);

    private void Fail(string message)
    {
        _phase = Phase.Done;
        Log.Error($"{Flag}: FAILED: {message}");
        GetTree().Quit(1);
    }
}
