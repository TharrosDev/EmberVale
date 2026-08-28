using Embervale.Core.Services;
using Embervale.Entities;
using Embervale.Player;
using Embervale.Shrines;
using Embervale.World;
using Godot;

namespace Embervale.Debugging;

/// <summary>
/// Live-world visual proof for 41.5A — <c>godot --path . -- --shrine-shots</c>. It drives the
/// actual shrine interactable, then frames the spawned shrine from eye level front and back while
/// the player, training dummy, tomes, pickups and streamed town remain around it. The six final
/// authored shrine locations belong to 41.5B; this verifies the core's real caller now.
/// </summary>
public sealed partial class ShrineShots : ShotHarness
{
    protected override string Flag => "--shrine-shots";

    protected override string OutputDir => "user://shrine_shots";

    protected override void BuildShotList()
    {
        Shot("01-day-front-claimed", () => Frame(front: true, hour: 12, claim: true));
        Shot("02-day-back", () => Frame(front: false, hour: 12, claim: false));
        Shot("03-dusk-front", () => Frame(front: true, hour: 19, claim: false));
        Shot("04-dusk-back", () => Frame(front: false, hour: 19, claim: false));
    }

    private static void Frame(bool front, int hour, bool claim)
    {
        if (ServiceLocator.Instance is not { } locator ||
            !locator.TryGet(out PlayerCharacter player) ||
            player.GetComponent<PlayerController>() is not { } controller ||
            controller.Camera is not { } camera ||
            FindShrine() is not { } shrine)
        {
            return;
        }

        if (claim)
        {
            shrine.GetComponent<ShrineComponent>()?.Interact(player);
            VerifyLoadReplaces(player);
        }

        if (locator.TryGet(out WorldClock clock))
        {
            clock.SetTimeOfDay(hour);
        }

        // Freeze only the player's input rig; the live streamed world, people and effects continue
        // to update while the harness holds the frame. The vector is intentionally close and at an
        // eye-level camera height, not a catalogue orbit that would hide collision/scale problems.
        controller.ProcessMode = ProcessModeEnum.Disabled;
        Vector3 offset = front ? new Vector3(2.7f, 1.7f, 2.6f) : new Vector3(-2.7f, 1.7f, -2.6f);
        camera.GlobalPosition = shrine.GlobalPosition + offset;
        camera.LookAt(shrine.GlobalPosition + new Vector3(0f, 0.85f, 0f), Vector3.Up);
    }

    private static Entity? FindShrine() =>
        Engine.GetMainLoop() is SceneTree tree
            ? tree.Root.FindChild("SolarynShrine", recursive: true, owned: false) as Entity
            : null;

    /// <summary>Runs the actual component load path without touching a user save slot: claim the
    /// shrine, replace the live set with an empty snapshot, then restore the captured snapshot. A
    /// merged load would leave the claim live through the empty restore and fail this witness.</summary>
    private static void VerifyLoadReplaces(PlayerCharacter player)
    {
        if (player.GetComponent<BlessingComponent>() is not { } blessings)
        {
            return;
        }

        Godot.Collections.Dictionary saved = blessings.Save();
        blessings.Load(new Godot.Collections.Dictionary { ["claims"] = new Godot.Collections.Array() });
        bool cleared = !blessings.HasClaimed(Embervale.Core.GameIds.Shrines.Solaryn);
        blessings.Load(saved);
        bool restored = blessings.HasClaimed(Embervale.Core.GameIds.Shrines.Solaryn);

        if (!cleared || !restored)
        {
            GD.PushError($"--shrine-shots: blessing load replacement failed (cleared={cleared}, restored={restored}).");
            return;
        }

        GD.Print("--shrine-shots: blessing Load replaced an in-memory claim set and restored it.");
    }
}
