using Embervale.Bootstrap;
using Embervale.Core.Diagnostics;
using Embervale.Core.Services;
using Embervale.Player;
using Embervale.World;
using Godot;

namespace Embervale.Debugging;

/// <summary>
/// <c>-- --play --repro=&lt;name&gt;</c>: replays a repro script (<see cref="ReproHarness"/>) in a live
/// session from a shell, once the world has settled around the player. It prints one
/// <c>EMBERVALE_RESULT</c> line with gate <c>repro</c> (name, commands run, the command that stopped
/// it). A script that fails logs an error and quits with exit 1; one that passes leaves the session
/// running, so <c>--perf-report</c> or <c>--quit-after</c> can measure or end what it set up.
///
/// <para>It drives the dev console, which exists only in a developer session: with <c>--capture</c>
/// there is no console and the run fails saying so.</para>
/// </summary>
public sealed partial class ReproRun : Node
{
    public const string Flag = "--repro";

    /// <summary>Frames to wait for the world to settle before giving up.</summary>
    private const int MaxWaitFrames = 1800;

    private int _waited;

    /// <summary>The session's console (<c>session.DevTools?.Console</c>); null in a capture run.</summary>
    public DevConsole? Console { get; init; }

    public override void _Ready() => ProcessMode = ProcessModeEnum.Always;

    public override void _Process(double delta)
    {
        bool ready = ServiceLocator.Instance is { } locator && locator.TryGet(out PlayerCharacter _) &&
                     locator.TryGet(out RegionStreamer streamer) && streamer.IsSettled();
        if (!ready && ++_waited < MaxWaitFrames)
        {
            return;
        }

        SetProcess(false);
        string name = HeadlessArgs.User.Value(Flag) ?? string.Empty;
        var report = new HeadlessReport(Flag.TrimStart('-'));
        report.Fact("name", name).Fact("waited_frames", _waited);
        if (name.Length == 0)
        {
            report.Fail($"{Flag} needs a script: {Flag}=<name>, one of {string.Join(", ", ReproHarness.Names)}");
        }
        else if (!ready)
        {
            // A script run against a world that is still loading proves nothing about the script.
            report.Fail($"the session had not finished loading after {MaxWaitFrames} frames; the script was not run");
        }
        else if (Console == null)
        {
            report.Fail("there is no dev console in this run (a --capture or exported session); run without --capture");
        }
        else
        {
            ReproResult result = ReproHarness.Execute(name, Console.Run);
            report.Fact("steps", result.Steps).Fact("failed_step", result.FailedStep);
            Log.Info(result.Transcript.Replace("\n", " | "));
            report.Check(result.Passed, $"'{result.FailedStep}' failed");
        }

        // Printed, not written to --report: that file belongs to whatever ends the run.
        int code = report.Emit(line => GD.Print(line), null, (long)Time.GetTicksMsec());
        if (code != 0)
        {
            Log.Error($"{Flag}: '{name}' failed: {string.Join("; ", report.Failures)}");
            GetTree().Quit(code);
        }
    }
}
