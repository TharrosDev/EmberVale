using Embervale.Core.Services;
using Embervale.Entities;
using Embervale.Player;
using Embervale.Shrines;
using Embervale.World;
using Godot;

namespace Embervale.Debugging;

/// <summary>
/// Live-world visual proof for 41.5B — <c>godot --path . -- --shrine-shots</c>. It drives the
/// real Solaryn placement, proves the persistent claim still replaces on load, then captures every
/// final shrine from eye level on both sides in its populated cell.
/// </summary>
public sealed partial class ShrineShots : ShotHarness
{
    protected override string Flag => "--shrine-shots";

    protected override string OutputDir => "user://shrine_shots";

    protected override void BuildShotList()
    {
        AddPair("01-solaryn", "ShrineSolaryn", claim: true);
        AddPair("02-veyra", "ShrineVeyra");
        AddPair("03-tharos", "ShrineTharos");
        AddPair("04-nyth", "ShrineNyth");
        AddPair("05-drakar", "ShrineDrakar");
        AddPair("06-elyndra", "ShrineElyndra");
    }

    private void AddPair(string prefix, string shrineNodeName, bool claim = false)
    {
        Shot($"{prefix}-front", () => Frame(shrineNodeName, front: true, claim: claim));
        Shot($"{prefix}-back", () => Frame(shrineNodeName, front: false, claim: false));
    }

    private static void Frame(string shrineNodeName, bool front, bool claim)
    {
        if (ServiceLocator.Instance is not { } locator ||
            !locator.TryGet(out PlayerCharacter player) ||
            player.GetComponent<PlayerController>() is not { } controller ||
            controller.Camera is not { } camera ||
            FindShrine(shrineNodeName) is not { } shrine)
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
            clock.SetTimeOfDay(12);
        }

        // Freeze only the player's input rig; the live streamed world, people and effects continue
        // to update while the harness holds the frame. The vector is intentionally close and at an
        // eye-level camera height, not a catalogue orbit that would hide collision/scale problems.
        controller.ProcessMode = ProcessModeEnum.Disabled;
        Vector3 offset = front ? new Vector3(4.6f, 1.9f, 4.4f) : new Vector3(-4.6f, 1.9f, -4.4f);
        camera.GlobalPosition = shrine.GlobalPosition + offset;
        camera.LookAt(shrine.GlobalPosition + new Vector3(0f, 0.85f, 0f), Vector3.Up);
    }

    private static Entity? FindShrine(string shrineNodeName) =>
        Engine.GetMainLoop() is SceneTree tree
            ? tree.Root.FindChild(shrineNodeName, recursive: true, owned: false) as Entity
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
