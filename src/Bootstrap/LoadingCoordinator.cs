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
/// <para>It holds <c>Loading</c> until the streamer reports the region settled <em>and</em> the
/// physics server reports collision under the player, then re-seats everything on the ground and
/// runs the caller's completion action once.</para>
/// </summary>
public sealed partial class LoadingCoordinator : Node
{
    private const double MaxSeconds = 30.0d;
    private const float GroundProbeUp = 1.0f;
    private const float GroundProbeDown = 3.0f;

    private double _elapsed = -1d;
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
        _elapsed = 0d;
        _onSettled = onSettled;
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
                $"(streamer settled: {streamer?.IsSettled()}, ground under the player: {HasGroundUnderPlayer()}, " +
                $"player at {Session.Players.Player?.GlobalPosition}, ground height there " +
                $"{(Session.Players.Player is { } p ? WorldGround.HeightAt(p.GlobalPosition.X, p.GlobalPosition.Z) : 0f):F2}). " +
                "Returning to the title screen rather than resuming into an incomplete world.");
            return;
        }

        PlayerCharacter? landingPlayer = Session.Players.Player;
        if (streamer != null && landingPlayer != null &&
            !streamer.IsPositionReady(landingPlayer.GlobalPosition, requireNavigation: false))
        {
            return;
        }
        if (!HasGroundUnderPlayer())
        {
            return;
        }

        _elapsed = -1d;

        // Everything the world put down is on the ground now, so anything the load moved can be
        // re-seated against real collision rather than the heightfield alone.
        SettleActorsOnGround();
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
    /// Puts the player and the party back on the ground once the region is resident. The teleport
    /// that started the load clamped against <see cref="WorldGround"/> — the analytic field, the
    /// only thing available before the cells exist; a metre of disagreement between that field and
    /// the collision mesh it generates leaves the player embedded or hovering.
    /// </summary>
    private void SettleActorsOnGround()
    {
        if (Session.Players.Player is { } player && IsInstanceValid(player))
        {
            player.Velocity = Vector3.Zero;
            if (SafePlacementService.TryResolve(player, player.GlobalPosition, out Vector3 resolved))
            {
                player.GlobalPosition = resolved;
            }
            else
            {
                player.GlobalPosition = WorldSessionDirector.SafeLanding(player.GlobalPosition);
            }
        }

        if (ServiceLocator.Instance is { } locator && locator.TryGet(out CompanionRoster party))
        {
            party.RegroupNow();
        }
    }
}
