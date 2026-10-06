using Embervale.Core.Diagnostics;

namespace Embervale.Debugging;

/// <summary>
/// The spell-effect performance scenario: <c>godot --path . -- --vfxperf</c>. It is to stand eight
/// casters firing on a loop for a fixed time and log, for each effect tier, the frame time at the
/// median and the 95th percentile, the frame time of the first cast and the particle count. Run
/// WITHOUT <c>--headless</c>.
///
/// <para>Seam: it measures nothing yet. It is a <see cref="ShotHarness"/> only so it is wired and
/// excluded from a shipping build like the capture harnesses; the base treats an empty shot list as
/// a failed run, so this says so and exits 0. The <c>_Ready</c> override goes when the scenario is
/// built.</para>
/// </summary>
public sealed partial class VfxPerfScenario : ShotHarness
{
    protected override string Flag => "--vfxperf";

    protected override string OutputDir => "user://vfx_perf";

    protected override void BuildShotList()
    {
    }

    public override void _Ready()
    {
        Log.Info($"{Flag}: the scenario is not built yet; nothing to measure.");
        GetTree().Quit(0);
    }
}
