using System;
using System.Collections.Generic;
using Embervale.Bootstrap;
using Embervale.Core;
using Embervale.Core.Diagnostics;
using Embervale.Core.Services;
using Embervale.Player;
using Embervale.World;
using Godot;

namespace Embervale.Debugging;

/// <summary>
/// <c>--shot</c>: one picture of exactly the thing just changed, in a real session with the real
/// sky, clock and weather, instead of a whole harness. Each request (<see cref="OneShotSpec"/>, which
/// lists the flags and the JSON form) is written as <c>&lt;name&gt;.png</c> with a
/// <c>&lt;name&gt;.png.json</c> sidecar holding the request, the resolved place and the camera.
///
/// <para><b>A world view</b> hard-loads to the place (a world X,Z, a cell's centre, a map location,
/// or a region's spawn) through the same load a portal uses, waits for the streamer to settle there,
/// sets the hour and weather if asked, and photographs it from a free camera that looks at the place
/// from <c>--distance</c> metres back along <c>--yaw</c>/<c>--pitch</c> (or through the player's own
/// first- or third-person camera with <c>--view=fp|tp</c>). The HUD is hidden unless <c>--hud</c>, and
/// in the free view so is the player unless <c>--show-player</c>. A JSON file may hold many views;
/// they are taken in one launch.</para>
///
/// <para><b>A UI state or a spell phase</b> is not staged here: <c>--ui=panelshots/00-map</c> starts
/// that suite restricted to that shot, and <c>--spell=fireball --phase=impact</c> starts
/// <see cref="SpellShots"/> restricted to that cast and phase, so each is staged by the code that
/// already knows how and checked the way that suite checks it. Their files land in that suite's
/// folder.</para>
///
/// <para>Like every session harness it needs a save to continue, or <c>--new-game</c> under an
/// isolated user directory. With <c>--film</c> a view also gets a filmstrip of the frames before it.</para>
/// </summary>
public sealed partial class OneShots : TimedShots
{
    public const string ShotArgument = "--shot";

    private enum Stage
    {
        Idle,
        WaitingToLoad,
        Loading,
        Settling,
    }

    private readonly List<OneShotSpec> _specs = new();
    private readonly Dictionary<string, Dictionary<string, object?>> _resolved = new();
    private string? _specError;
    private GameSession? _session;
    private OneShotSpec? _active;
    private Stage _stage;
    private Vector3 _point;
    private RegionResource? _region;
    private int _settled;
    private Camera3D? _camera;
    private readonly List<CanvasLayer> _hiddenLayers = new();

    protected override string Flag => ShotArgument;

    protected override string OutputDir => "user://one_shots";

    /// <summary>
    /// The node <c>--shot</c> attaches: this harness for world views, or another suite restricted to
    /// one shot when the request is a UI state or a spell phase.
    /// </summary>
    internal static Node Create(GameSession session)
    {
        List<OneShotSpec> specs = ReadSpecs(out string? error);
        if (error == null && specs.Count == 1 && !specs[0].IsWorld)
        {
            OneShotSpec spec = specs[0];
            if (spec.IsSpell)
            {
                string view = spec.View is "fp" ? "fp" : "tp";
                SpellShots.FilterOverride = spec.Spell;
                SpellShots.ViewOverride = view;
                OnlyOverride = new[] { spec.Phase == "all" ? $"*_{view}_*" : $"*_{view}_{spec.Phase}" };
                return new SpellShots { Name = "SpellShots" };
            }

            foreach (SessionHarness harness in SessionHarnesses.All)
            {
                bool named = harness.Flag == spec.UiSuiteFlag || harness.Alias == spec.UiSuiteFlag;
                if (named && harness.Capture && harness.Flag != ShotArgument)
                {
                    OnlyOverride = spec.UiShots;
                    return harness.Create(session);
                }
            }

            error = $"ui '{spec.Ui}' names no session suite (--shellshots is run on its own: --shellshots --only=<shot>)";
        }
        else if (error == null && specs.Exists(spec => !spec.IsWorld))
        {
            error = "a ui or spell request must be the only one; several world views may share a file";
        }

        var shots = new OneShots { Name = "OneShots", _session = session, _specError = error };
        shots._specs.AddRange(specs);
        return shots;
    }

    /// <summary>The requests: <c>--shot=&lt;file or JSON&gt;</c>, else the flags.</summary>
    private static List<OneShotSpec> ReadSpecs(out string? error)
    {
        string? source = HeadlessArgs.User.Value(ShotArgument);
        var specs = new List<OneShotSpec>();
        if (string.IsNullOrWhiteSpace(source))
        {
            specs.Add(OneShotSpec.FromArgs(HeadlessArgs.User));
            error = null;
        }
        else
        {
            string text = source.TrimStart();
            if (!text.StartsWith('{') && !text.StartsWith('['))
            {
                try
                {
                    text = System.IO.File.ReadAllText(source);
                }
                catch (Exception e)
                {
                    error = $"could not read the shot spec '{source}': {e.Message}";
                    return specs;
                }
            }

            specs = OneShotSpec.FromJson(text, out error);
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (OneShotSpec spec in specs)
        {
            error ??= spec.Validate() is { } invalid ? $"shot '{spec.Name}': {invalid}" : null;
            error ??= names.Add(spec.Name) ? null : $"two shots are named '{spec.Name}'";
        }

        return specs;
    }

    protected override void BuildTimedShots()
    {
        if (_specError != null)
        {
            Problem(_specError);
            return;
        }

        foreach (OneShotSpec spec in _specs)
        {
            OneShotSpec captured = spec;
            TimedShot(spec.Name, () => Begin(captured), () => _stage == Stage.Settling && _settled >= captured.Settle,
                () => Inspect(captured), timeout: spec.Timeout);
        }
    }

    /// <summary>A one-off has no suite to summarise: the summary frame would be a second copy of the view.</summary>
    protected override bool WritesImage(string name) => name != SummaryShot && base.WritesImage(name);

    protected override bool WaitsForOpeningNotices => false;

    protected override string? Fatal(string name) =>
        ShotStage.Player() == null ? "the session has no player" : null;

    // --- one view -----------------------------------------------------------------------------------

    private void Begin(OneShotSpec spec)
    {
        _active = spec;
        _stage = Stage.Idle;
        _settled = 0;
        if (_session == null || !IsInstanceValid(_session) || _session.WorldDirector.Streamer == null)
        {
            Problem($"'{spec.Name}': there is no session world to photograph.");
            return;
        }

        if (!Resolve(spec, _session, out _region, out _point, out string place, out string? failure))
        {
            Problem($"'{spec.Name}': {failure}.");
            return;
        }

        _resolved[spec.Name] = new Dictionary<string, object?>
        {
            ["region"] = _region!.Id,
            ["place"] = place,
            ["view"] = spec.View,
            ["yaw"] = spec.Yaw,
            ["pitch"] = spec.Pitch,
            ["distance"] = spec.Distance,
            ["height"] = spec.Height,
            ["settle_frames"] = spec.Settle,
        };
        _stage = Stage.WaitingToLoad;
    }

    /// <summary>Where a request points: its region and a world X,Z (Y is found once the ground is there).</summary>
    private static bool Resolve(
        OneShotSpec spec, GameSession session, out RegionResource? region, out Vector3 point, out string place,
        out string? failure)
    {
        region = null;
        point = Vector3.Zero;
        place = string.Empty;
        failure = null;
        string? regionId = spec.Region;
        Vector3? found = null;
        if (spec.Location is { } locationId)
        {
            if (MapLocationDatabase.Get(locationId) is not { } location)
            {
                failure = $"unknown map location '{locationId}'";
                return false;
            }

            RegionCellResource? cell = RegionDatabase.Cell(location.CellId);
            found = WorldPlaceIndex.Location(locationId) ?? cell?.Center;
            regionId ??= RegionOf(location.CellId);
            place = $"location {locationId}";
        }
        else if (spec.Cell is { } cellId)
        {
            if (RegionDatabase.Cell(cellId) is not { } cell)
            {
                failure = $"unknown cell '{cellId}'";
                return false;
            }

            found = cell.Center;
            regionId ??= RegionOf(cellId);
            place = $"cell {cellId}";
        }

        region = regionId == null
            ? RegionDatabase.Get(session.CurrentRegionId)
            : RegionDatabase.Get(regionId) ?? RegionDatabase.Get("region." + regionId);
        if (region == null)
        {
            failure = $"unknown region '{regionId ?? session.CurrentRegionId}'";
            return false;
        }

        if (spec.At is { } at)
        {
            found = new Vector3(at[0], 0f, at[^1]);
            place = $"at {at[0]:0.##},{at[^1]:0.##}";
        }

        if (found == null)
        {
            found = region.SpawnPoint;
            place = $"spawn of {region.Id}";
        }

        point = found.Value;
        return true;
    }

    private static string? RegionOf(string cellId)
    {
        foreach (RegionResource region in RegionDatabase.All)
        {
            foreach (RegionCellResource cell in region.Cells)
            {
                if (cell != null && cell.Id == cellId)
                {
                    return region.Id;
                }
            }
        }

        return null;
    }

    /// <summary>Moves the run through load, settle and staging. The capture itself is the base's.</summary>
    protected override void Frame(double delta)
    {
        if (_active is not { } spec || _session == null || !IsInstanceValid(_session))
        {
            return;
        }

        KeepInterfaceHidden();
        bool playing = GameManager.Instance is { IsPlaying: true };
        switch (_stage)
        {
            case Stage.WaitingToLoad when playing && ShotStage.Player() is { } player:
                ShotStage.PreparePlayer(player);
                RestoreView(player);

                // The same hard load a portal or a fast-travel jump uses: the loading gate holds play
                // until the ground under the landing exists. No autosave: nothing here is progress.
                RegionResource region = _region!;
                if (region.Id != _session.CurrentRegionId)
                {
                    // Another region first, at its own spawn, exactly as a portal arrives. The ground
                    // height of the place asked for can only be read once that region's terrain is
                    // the live one, so the jump to the place itself is the next pass through here.
                    _session.WorldDirector.PerformRegionLoad(
                        region, WorldSessionDirector.RegionSpawn(region), "Shot", autosave: false);
                    break;
                }

                _session.WorldDirector.PerformRegionLoad(
                    region, new Vector3(_point.X, WorldGround.HeightAt(_point.X, _point.Z) + 0.1f, _point.Z),
                    "Shot", autosave: false);
                _stage = Stage.Loading;
                break;

            case Stage.Loading when playing && _session.WorldDirector.Streamer is { } streamer &&
                                    streamer.IsPositionReady(_point) && streamer.IsSettled():
                Arrange(spec);
                _stage = Stage.Settling;
                _settled = 0;
                break;

            case Stage.Settling:
                _settled++;
                if (_camera != null && IsInstanceValid(_camera) && !_camera.Current)
                {
                    _camera.MakeCurrent();
                }

                break;
        }
    }

    /// <summary>Puts the sky, the HUD, the player and the camera where the request says.</summary>
    private void Arrange(OneShotSpec spec)
    {
        if (ShotStage.Player() is not { } player || _session == null)
        {
            return;
        }

        Dictionary<string, object?> facts = _resolved[spec.Name];
        if (spec.Hour is { } hour)
        {
            ShotStage.SetHour(hour);
        }

        if (spec.Weather is { } weatherId)
        {
            bool forced = ServiceLocator.Instance is { } locator && locator.TryGet(out WeatherDirector weather) &&
                          (weather.Force(weatherId) || weather.Force("weather." + weatherId));
            if (!forced)
            {
                Problem($"'{spec.Name}': weather '{weatherId}' could not be forced (unknown id, or no weather director).");
            }

            if (ServiceLocator.Instance is { } services && services.TryGet(out SkyController sky))
            {
                sky.SnapToCurrent(); // the sky blends toward a new weather over seconds; a still wants it now
            }
        }

        ShowInterface(spec.Hud);

        Vector3 ground = WorldGround.OnGround(_point, 0f);
        Vector3 target = ground + (Vector3.Up * spec.Height);
        (float fx, float fy, float fz) = spec.Forward();
        var forward = new Vector3(fx, fy, fz);
        facts["ground"] = Triple(ground);
        facts["target"] = Triple(target);

        if (spec.View == "free")
        {
            // The rig owns its camera every frame; the free camera is the harness's own, and the rig
            // and its obstruction fading are stopped so neither reacts to a camera that is not theirs.
            if (player.GetComponent<PlayerCameraRig>() is { } rig)
            {
                rig.ProcessMode = ProcessModeEnum.Disabled;
            }

            if (player.GetComponent<CameraOcclusion>() is { } occlusion)
            {
                occlusion.ProcessMode = ProcessModeEnum.Disabled;
            }

            ShotStage.Place(player, ground, new Vector3(fx, 0f, fz));
            if (player.GetNodeOrNull<Node3D>("BodyMesh") is { } body)
            {
                body.Visible = spec.ShowPlayer;
            }

            if (_camera == null || !IsInstanceValid(_camera))
            {
                _camera = new Camera3D { Name = "OneShotCamera", Near = 0.1f, Far = 6000f };
                AddChild(_camera);
            }

            Vector3 eye = target - (forward * spec.Distance);
            float floor = WorldGround.HeightAt(eye.X, eye.Z) + 0.3f;
            if (eye.Y < floor)
            {
                // Standing back along a downward pitch can put the eye inside a hillside.
                eye.Y = floor;
                facts["camera_lifted"] = true;
            }

            _camera.Fov = spec.Fov;
            _camera.GlobalPosition = eye;
            _camera.LookAt(spec.Distance > 0.01f ? target : eye + forward, Vector3.Up);
            _camera.MakeCurrent();
        }
        else
        {
            ShotStage.SetView(player, spec.View == "fp");
            ShotStage.Place(player, ground, new Vector3(fx, 0f, fz));
            ShotStage.SetPitch(player, Mathf.DegToRad(Mathf.Clamp(spec.Pitch, -89f, 89f)));
        }
    }

    /// <summary>A world view without <c>--hud</c> is the world alone: every interface layer is hidden,
    /// not just the HUD, so an act card, a discovery toast or a subtitle cannot sit over the frame.</summary>
    private void ShowInterface(bool show)
    {
        foreach (CanvasLayer layer in _hiddenLayers)
        {
            if (IsInstanceValid(layer))
            {
                layer.Visible = true;
            }
        }

        _hiddenLayers.Clear();
        if (_session != null)
        {
            _session.Ui.Hud.Visible = true;
        }

        if (!show)
        {
            HideLayers(GetTree().Root);
        }
    }

    private void HideLayers(Node node)
    {
        foreach (Node child in node.GetChildren())
        {
            if (child is CanvasLayer { Visible: true } layer)
            {
                layer.Visible = false;
                _hiddenLayers.Add(layer);
            }

            HideLayers(child);
        }
    }

    /// <summary>A layer that shows itself again (a banner starting) is hidden again.</summary>
    private void KeepInterfaceHidden()
    {
        foreach (CanvasLayer layer in _hiddenLayers)
        {
            if (IsInstanceValid(layer) && layer.Visible)
            {
                layer.Visible = false;
            }
        }
    }

    /// <summary>Hands the view back to the player's own camera before the next request.</summary>
    private void RestoreView(PlayerCharacter player)
    {
        ShowInterface(true);
        if (player.GetComponent<PlayerCameraRig>() is { } rig)
        {
            rig.ProcessMode = ProcessModeEnum.Inherit;
            rig.Camera?.MakeCurrent();
        }

        if (player.GetComponent<CameraOcclusion>() is { } occlusion)
        {
            occlusion.ProcessMode = ProcessModeEnum.Inherit;
        }

        if (player.GetNodeOrNull<Node3D>("BodyMesh") is { } body)
        {
            body.Visible = true;
        }
    }

    private void Inspect(OneShotSpec spec)
    {
        bool settled = _stage == Stage.Settling && _settled >= spec.Settle;
        if (_resolved.TryGetValue(spec.Name, out Dictionary<string, object?>? facts))
        {
            facts["settled"] = settled;
        }

        if (!settled && _stage != Stage.Idle)
        {
            Problem($"'{spec.Name}': the world did not settle at ({_point.X:0.#}, {_point.Z:0.#}) in " +
                    $"{_region?.Id} within {spec.Timeout:0}s (stopped while {_stage}); is the place inside the region?");
        }

        if (Verbose)
        {
            Log.Info($"{Flag}: '{spec.Name}' stage={_stage} settled={settled} point={_point}");
        }
    }

    protected override void Describe(string name, Dictionary<string, object?> metadata)
    {
        if (_resolved.TryGetValue(name, out Dictionary<string, object?>? facts))
        {
            metadata["shot"] = facts;
        }
    }
}
