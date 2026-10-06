using System;
using Embervale.Combat;
using Embervale.Companions;
using Embervale.Core;
using Embervale.Core.Diagnostics;
using Embervale.Core.Services;
using Embervale.Player;
using Embervale.World;
using Godot;

namespace Embervale.Bootstrap;

/// <summary>
/// The loading gate.
///
/// <para>⚠️ <b>EVERY ROUTE INTO THE WORLD GOES THROUGH HERE.</b> New game, load, portal and fast
/// travel. Three of the four used to; the new-game path — the one every first-time player takes —
/// entered <c>Playing</c> the instant the world was assembled, which is before the streamer has
/// instanced a single cell. The player was handed control standing over a hole.</para>
///
/// <para>It holds <c>Loading</c> through three stages (<see cref="LoadingWait"/>): the streamer
/// reports the landing settled, the physics server reports collision under the player, and
/// <see cref="SafePlacementService"/> finds a capsule-clear spot at or near the landing. Then it
/// re-seats the party and runs the caller's completion action once.</para>
///
/// <para><b>Timing guarantees.</b> Every stage is bounded by <see cref="MaxSeconds"/>, after which
/// the gate aborts to the title rather than resuming into an incomplete world. The placement stage
/// is additionally bounded by <see cref="PlacementRetryFrames"/>: collision can be resident while
/// a building's collider is still a frame behind the terrain's, so a refused placement is retried
/// once per physics frame before aborting to the title. A stuck stage is named in the log
/// every <see cref="ProgressReportSeconds"/>, and the settle line records when each stage cleared.
/// A second <see cref="Begin"/> while the gate is open never drops the first caller's action — both
/// run, once, on the frame play resumes.</para>
/// </summary>
public sealed partial class LoadingCoordinator : Node
{
    /// <summary>Hard cap on the whole gate, seconds.</summary>
    [Export(PropertyHint.Range, "5,120,1")] public double MaxSeconds { get; set; } = 30.0d;

    /// <summary>Seconds between "still waiting on …" lines while a stage holds.</summary>
    [Export(PropertyHint.Range, "0,30,0.5")] public double ProgressReportSeconds { get; set; } = 5.0d;

    /// <summary>Physics frames the placement stage retries before aborting an unsafe landing.</summary>
    [Export(PropertyHint.Range, "1,240,1")] public int PlacementRetryFrames { get; set; } = 20;

    /// <summary>
    /// Seconds the gate keeps the loading screen up, after the landing is standable, for the rest of
    /// the realm to finish streaming in. Releasing at the landing cell alone left every other cell of
    /// the realm to instantiate one per frame through the first seconds of play, which is the hitch
    /// felt right after a load. A soft bound, never a failure: when it runs out play starts and the
    /// remainder streams in as it always did.
    /// </summary>
    [Export(PropertyHint.Range, "0,30,0.5")] public double RealmSettleSeconds { get; set; } = 10.0d;

    private const float GroundProbeUp = 1.0f;
    private const float GroundProbeDown = 3.0f;

    private double _elapsed = -1d;
    private double _lastReport;
    private double _streamerReadyAt = -1d;
    private double _groundReadyAt = -1d;
    private int _placementAttempts;
    private Action? _onSettled;

    public GameSession Session { get; init; } = null!;

    public override void _EnterTree()
    {
        // ⚠️ The gate runs WHILE THE WORLD IS PAUSED, which is the state it exists to hold. It used
        // to inherit this from the bootstrap by being its child; inheriting something load-bearing
        // from a parent's parent is how it silently stops working when the tree is rearranged.
        ProcessMode = ProcessModeEnum.Always;
    }

    /// <summary>Opens the gate: shows the loading screen and holds <see cref="GameState.Loading"/>
    /// until the world under the player is real. <paramref name="onSettled"/> runs once, on the
    /// frame play resumes.</summary>
    public void Begin(string message, Action? onSettled)
    {
        GameManager.Instance?.ChangeState(GameState.Loading);
        if (_elapsed >= 0d && _onSettled != null)
        {
            // Re-opened before the last load settled (a portal requested from a load's completion,
            // a restore on top of a travel). The first caller was promised its action; keep it.
            Log.Info("LoadingCoordinator: gate re-opened while pending; both completion actions will run.");
            _onSettled += onSettled;
        }
        else
        {
            _onSettled = onSettled;
        }
        _elapsed = 0d;
        _lastReport = 0d;
        _streamerReadyAt = -1d;
        _groundReadyAt = -1d;
        _placementAttempts = 0;
        if (Session.Players.Player is { } player)
        {
            Session.WorldDirector.Streamer?.RequirePosition(player.GlobalPosition);
        }
        SetPhysicsProcess(true);
        Log.Info(message);
    }

    /// <summary>
    /// The gate's tick. In <c>_PhysicsProcess</c> rather than <c>_Process</c> because it casts a
    /// ray, and the direct space state may only be queried inside a physics step.
    /// </summary>
    public override void _PhysicsProcess(double delta)
    {
        if (_elapsed < 0d)
        {
            return;
        }

        _elapsed += delta;
        RegionStreamer? streamer = Session.WorldDirector.Streamer;

        // A cell that has exhausted its retries means the region can never be whole. Waiting out the
        // cap would only delay the same verdict, so say it now.
        if (streamer != null && streamer.HasFailedCells())
        {
            Abort(
                "The world could not be assembled: " +
                $"cell(s) {string.Join(", ", streamer.FailedCellIds)} failed to load. " +
                "Returning to the title screen rather than resuming into an incomplete world.");
            return;
        }

        if (_elapsed >= MaxSeconds)
        {
            Abort(
                $"The world did not finish loading within {MaxSeconds:0} s " +
                $"(stuck on: {CurrentWait()}, streamer settled: {streamer?.IsSettled()}, " +
                $"ground under the player: {HasGroundUnderPlayer()}, " +
                $"player at {Session.Players.Player?.GlobalPosition}, ground height there " +
                $"{(Session.Players.Player is { } p ? WorldGround.HeightAt(p.GlobalPosition.X, p.GlobalPosition.Z) : 0f):F2}). " +
                "Returning to the title screen rather than resuming into an incomplete world.");
            return;
        }

        PlayerCharacter? landingPlayer = Session.Players.Player;
        if (streamer != null && landingPlayer != null &&
            !streamer.IsPositionReady(landingPlayer.GlobalPosition, requireNavigation: false))
        {
            ReportProgress(LoadingWait.Streamer);
            return;
        }
        MarkCleared(ref _streamerReadyAt);

        if (!HasGroundUnderPlayer())
        {
            ReportProgress(LoadingWait.Collision);
            return;
        }
        MarkCleared(ref _groundReadyAt);

        // The landing is real; give the rest of the realm a bounded moment to arrive behind the
        // loading screen, where the streamer runs its instantiate and activation stages together.
        if (streamer != null && _elapsed - _groundReadyAt < RealmSettleSeconds && !streamer.IsSettled())
        {
            return;
        }

        // Everything the world put down is on the ground now, so anything the load moved can be
        // re-seated against real collision rather than the heightfield alone.
        if (!SettlePlayer())
        {
            ReportProgress(LoadingWait.Placement);
            return;
        }

        _elapsed = -1d;
        RegroupParty();
        streamer?.ReleaseRequiredPosition();

        GameManager.Instance?.ChangeState(GameState.Playing);
        Action? settled = _onSettled;
        _onSettled = null;
        settled?.Invoke();
    }

    private void Abort(string reason)
    {
        _elapsed = -1d;
        _onSettled = null;
        Session.Lifecycle.AbortToTitle(reason);
    }

    private void MarkCleared(ref double stageClearedAt)
    {
        if (stageClearedAt < 0d)
        {
            stageClearedAt = _elapsed;
        }
    }

    /// <summary>The stage the gate is holding on, from what it has recorded so far.</summary>
    private LoadingWait CurrentWait() =>
        LoadingGateRules.Pending(_streamerReadyAt >= 0d, _groundReadyAt >= 0d, placed: false);

    private void ReportProgress(LoadingWait waiting)
    {
        if (!LoadingGateRules.ReportDue(_elapsed, _lastReport, ProgressReportSeconds))
        {
            return;
        }
        _lastReport = _elapsed;
        string detail = waiting switch
        {
            LoadingWait.Streamer => "the streamer to make the landing cell active",
            LoadingWait.Collision => "collision under the player",
            LoadingWait.Placement =>
                $"a capsule-clear landing ({_placementAttempts}/{PlacementRetryFrames} placement attempts)",
            _ => "nothing",
        };
        Log.Info($"LoadingCoordinator: still waiting on {detail} after {_elapsed:0.0} s " +
                 $"(player at {Session.Players.Player?.GlobalPosition}).");
    }

    /// <summary>
    /// Is there standable collision under the player right now?
    ///
    /// ⚠️ <b>THE HEIGHTFIELD IS NOT AN ANSWER TO THIS.</b> <see cref="WorldGround"/> is a pure
    /// function of the region resource and returns a height whether or not a single collider has
    /// been instanced — which is exactly why a spawn validated against it could still be standing
    /// over a void. This asks the physics server, which can only answer yes once the cell carrying
    /// the terrain collider is in the tree.
    /// </summary>
    private bool HasGroundUnderPlayer()
    {
        PlayerCharacter? player = Session.Players.Player;
        if (player == null || !IsInstanceValid(player))
        {
            return true; // nothing to protect; the gate is not the place to invent a player
        }

        Vector3 origin = player.GlobalPosition;
        var query = PhysicsRayQueryParameters3D.Create(
            origin + (Vector3.Up * GroundProbeUp),
            origin + (Vector3.Down * GroundProbeDown),
            CombatLayers.World);
        query.Exclude = new Godot.Collections.Array<Rid> { player.GetRid() };

        // The player's world, not this node's: the gate is a plain Node so it can sit anywhere in
        // the session, and the only 3D world that matters here is the one the player stands in.
        return player.GetWorld3D().DirectSpaceState.IntersectRay(query).Count > 0;
    }

    /// <summary>
    /// Puts the player on the ground once the region is resident. The teleport that started the load
    /// clamped against <see cref="WorldGround"/> — the analytic field, the only thing available
    /// before the cells exist; a metre of disagreement between that field and the collision mesh it
    /// generates leaves the player embedded or hovering.
    ///
    /// Returns true only once the player is placed on a validated spot. A refusal returns false
    /// while the next frame can retry; exhausting <see cref="PlacementRetryFrames"/> aborts the
    /// session with the search's diagnosis and never releases play or its completion action.
    /// </summary>
    private bool SettlePlayer()
    {
        string placement;
        if (Session.Players.Player is not { } player || !IsInstanceValid(player))
        {
            return true;
        }

        player.Velocity = Vector3.Zero;
        var report = new SafePlacementReport();
        if (SafePlacementService.TryResolve(player, player.GlobalPosition, out Vector3 resolved, report: report))
        {
            player.GlobalPosition = resolved;
            placement = report.Summary();
            Log.Info($"LoadingCoordinator: world settled in {_elapsed:0.00} s (streamer {_streamerReadyAt:0.00} s, " +
                     $"collision {_groundReadyAt:0.00} s, retries {_placementAttempts}; {placement}).");
            return true;
        }

        _placementAttempts++;
        if (!LoadingGateRules.PlacementExhausted(_placementAttempts, PlacementRetryFrames))
        {
            return false;
        }

        placement = report.Summary();
        Abort($"LoadingCoordinator: no capsule-clear landing near {player.GlobalPosition} after " +
              $"{_placementAttempts} frame(s) — {placement} (mostly {report.Dominant}). " +
              "Returning to the title screen instead of resuming into blocked geometry.");
        return false;
    }

    private static void RegroupParty()
    {
        if (ServiceLocator.Instance is { } locator && locator.TryGet(out CompanionRoster party))
        {
            party.RegroupNow();
        }
    }
}
