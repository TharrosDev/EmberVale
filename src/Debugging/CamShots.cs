using Embervale.Core.Diagnostics;

namespace Embervale.Debugging;

/// <summary>
/// The camera screenshot harness: <c>godot --path . -- --camshots</c>. It is to photograph both
/// views at idle, walk, jog, sprint, strafe and backpedal, during a cast, and looking down and up.
/// Run WITHOUT <c>--headless</c>.
///
/// <para>Seam: the shot list is empty. The base harness treats an empty list as a failed run, so
/// until the first <c>Shot</c> is registered this says so and exits 0; the <c>_Ready</c> override
/// goes when the list is filled.</para>
/// </summary>
public sealed partial class CamShots : ShotHarness
{
    protected override string Flag => "--camshots";

    protected override string OutputDir => "user://cam_shots";

    protected override void BuildShotList()
    {
    }

    public override void _Ready()
    {
        Log.Info($"{Flag}: no shots are built yet; nothing to capture.");
        GetTree().Quit(0);
    }
}
