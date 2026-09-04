using Embervale.Core.Services;
using Embervale.Corruption;
using Embervale.Entities;
using Embervale.Player;
using Embervale.Shrines;
using Embervale.World;
using Godot;

namespace Embervale.Debugging;

/// <summary>
/// Live-world visual proof for 41.5B/C — <c>godot --path . -- --shrine-shots</c>. It drives the
/// real Solaryn placement through the corruption gate (refused while tainted, blessed once clean),
/// proves the persistent claim still replaces on load, then captures every final shrine from eye
/// level on both sides in its populated cell.
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
            player.GetComponent<PlayerCameraRig>() is not { } controller ||
            controller.Camera is not { } camera ||
            FindShrine(shrineNodeName) is not { } shrine)
        {
            return;
        }

        if (claim)
        {
            VerifyCorruptionRefusal(player, shrine);
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

    /// <summary>Drives the corruption gate through the real interactable (41.5C). Raises the player
    /// above Solaryn's authored threshold, presses the shrine, and proves the refusal claimed
    /// nothing and moved no armour; then drops corruption back and proves the same press blesses.
    /// Corruption is restored to whatever the save carried, so the capture pass that follows is not
    /// looking at a world the harness quietly changed.</summary>
    private static void VerifyCorruptionRefusal(PlayerCharacter player, Entity shrine)
    {
        if (shrine.GetComponent<ShrineComponent>() is not { } caller ||
            player.GetComponent<BlessingComponent>() is not { } blessings ||
            player.GetComponent<CorruptionComponent>() is not { } corruption ||
            player.GetComponent<Stats.StatsComponent>() is not { } stats ||
            ShrineDatabase.Get(caller.ShrineId) is not { } resource)
        {
            GD.PushError("--shrine-shots: could not reach the Solaryn caller, blessings, corruption or stats.");
            return;
        }

        int original = corruption.Value;
        float baseline = stats.GetStat(resource.Stat).Value;

        corruption.Set(Mathf.Min(resource.RefusalCorruption + 5, CorruptionTiers.Max));
        caller.Interact(player);
        bool refusedClaim = !blessings.HasClaimed(resource.Id);
        bool refusedModifier = Mathf.IsEqualApprox(stats.GetStat(resource.Stat).Value, baseline);

        corruption.Set(Mathf.Max(resource.RefusalCorruption - 5, CorruptionTiers.Min));
        caller.Interact(player);
        bool blessedClaim = blessings.HasClaimed(resource.Id);
        bool blessedModifier = stats.GetStat(resource.Stat).Value > baseline;

        corruption.Set(original);

        if (!refusedClaim || !refusedModifier || !blessedClaim || !blessedModifier)
        {
            GD.PushError(
                $"--shrine-shots: corruption gate failed (refusedClaim={refusedClaim}, " +
                $"refusedModifier={refusedModifier}, blessedClaim={blessedClaim}, blessedModifier={blessedModifier}).");
            return;
        }

        GD.Print(
            $"--shrine-shots: '{resource.Id}' refused at corruption {resource.RefusalCorruption + 5} " +
            $"(no claim, {resource.Stat} unchanged at {baseline}) and blessed at " +
            $"{resource.RefusalCorruption - 5} ({resource.Stat} now {stats.GetStat(resource.Stat).Value}).");
    }

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
