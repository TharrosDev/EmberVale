using System.Collections.Generic;
using Godot;

namespace Embervale.Animation;

/// <summary>
/// Assembles the <see cref="AnimationTree"/> every character animates through.
///
/// <para><b>What it replaced.</b> A flat priority ladder, recomputed every frame, ending in a single
/// <c>AnimationPlayer.Play(name, customBlend: 0.15)</c>. It had no blend space, no state machine, no
/// layers and no masks, so there was exactly one clip playing at any moment and every transition was
/// a 0.15 s crossfade between two unrelated poses. Walking and running were separate clips chosen by
/// a speed threshold rather than blended, which is the whole of why locomotion read as snapping.</para>
///
/// <para><b>The shape, and why each part earns its place:</b></para>
/// <code>
/// root (BlendTree)
///   ├─ StateMachine
///   │    ├─ locomotion  BlendSpace1D over SIGNED forward speed
///   │    ├─ action      BlendTree: TimeScale -> Animation   (driven by CharacterActionComponent)
///   │    ├─ hit         Animation
///   │    └─ death       Animation
///   ├─ UpperBody        Animation  (block / cast pose)
///   └─ Layer            Blend2 with a BONE FILTER on the upper body
/// </code>
///
/// <para>⚠️ <b>The bone-filtered layer is not decoration; it is the fix for a defect that shipped.</b>
/// 39B had to refuse every full-body one-shot while mounted, because the library has no mounted
/// attack and the standing swing puts the hips half a metre above the saddle — the rider stood up
/// inside the horse for the length of every attack. The workaround cost the mounted swing its
/// animation entirely. With an upper-body mask the legs can hold the ride pose while the arms swing,
/// which is exactly what the old comment said the real fix would be.</para>
/// </summary>
public static class LocomotionTree
{
    // Godot addresses a tree's live values by string path. Every one of them is here rather than
    // spelled out at each call site: a typo'd parameter path does not throw — Set() silently does
    // nothing, and the character simply never moves.
    public const string StateMachineNode = "StateMachine";
    public const string LocomotionState = "locomotion";
    public const string ActionState = "action";
    public const string HitState = "hit";
    public const string DeathState = "death";

    public const string PlaybackParam = "parameters/StateMachine/playback";
    public const string SpeedParam = "parameters/StateMachine/locomotion/blend_position";
    public const string ActionScaleParam = "parameters/StateMachine/action/TimeScale/scale";
    public const string UpperBodyBlendParam = "parameters/Layer/blend_amount";

    private const string ActionAnimNode = "Anim";
    private const string ActionScaleNode = "TimeScale";
    private const string UpperBodyNode = "UpperBody";
    private const string LayerNode = "Layer";

    /// <summary>
    /// The blend space, in signed metres per second along the character's facing. Negative is
    /// walking backwards.
    ///
    /// ⚠️ <b>Speed is the blend axis rather than a threshold</b>, which is the point. The ladder
    /// switched clips when <c>HorizontalSpeed() > 0.6</c>, so a character accelerating from a stand
    /// popped from idle straight into a run at whatever phase that clip happened to be in.
    /// </summary>
    private static readonly (string Slot, float Speed)[] LocomotionPoints =
    {
        ("walk_back", -1.6f),
        ("idle", 0f),
        ("walk", 1.6f),
        ("run", 4.2f),
        ("sprint", 6.8f),
    };

    /// <summary>
    /// The bones the upper-body layer takes over when it is blended in.
    ///
    /// Everything from the chest up plus both arms; the spine root, hips and legs are deliberately
    /// left to locomotion, which is what lets a blocking character keep walking and a mounted one
    /// keep its seat.
    /// </summary>
    private static readonly string[] UpperBodyBones =
    {
        "Chest", "UpperChest", "Neck", "Head",
        "LeftShoulder", "LeftUpperArm", "LeftLowerArm", "LeftHand",
        "RightShoulder", "RightUpperArm", "RightLowerArm", "RightHand",
    };

    /// <summary>
    /// Builds the tree for one character from the clips it actually resolved.
    ///
    /// Returns null when the body cannot support one — no idle, or no forward locomotion at all.
    /// <b>Null is a valid answer and the caller must handle it</b>: a prop-bodied enemy and a rig
    /// whose clips did not resolve both land here, and both are better served by the simple
    /// fallback than by a tree whose every state points at an empty clip name.
    /// </summary>
    public static AnimationRootNode? Build(
        IReadOnlyDictionary<string, string> clips, Skeleton3D? skeleton)
    {
        if (!clips.TryGetValue("idle", out string? idle) || idle.Length == 0)
        {
            return null;
        }

        var machine = new AnimationNodeStateMachine();

        var locomotion = new AnimationNodeBlendSpace1D { MinSpace = -2f, MaxSpace = 7f };
        int points = 0;
        foreach ((string slot, float speed) in LocomotionPoints)
        {
            // A slot the body has no clip for is simply not a point in the space. The space
            // interpolates across the gap, so a body without a sprint clip runs faster rather than
            // freezing — which is the correct degradation and needs no branch anywhere else.
            if (clips.TryGetValue(slot, out string? clip) && clip.Length > 0)
            {
                locomotion.AddBlendPoint(new AnimationNodeAnimation { Animation = clip }, speed);
                points++;
            }
        }

        if (points < 2)
        {
            return null;
        }

        machine.AddNode(LocomotionState, locomotion, new Vector2(200, 100));

        // The action state is a small blend tree only so its clip can be time-scaled. That scale is
        // how CharacterActionComponent makes a clip span exactly the action's authored duration.
        var action = new AnimationNodeBlendTree();
        var actionAnim = new AnimationNodeAnimation { Animation = idle };
        var timeScale = new AnimationNodeTimeScale();
        action.AddNode(ActionAnimNode, actionAnim, new Vector2(0, 0));
        action.AddNode(ActionScaleNode, timeScale, new Vector2(200, 0));
        action.ConnectNode(ActionScaleNode, 0, ActionAnimNode);
        action.ConnectNode("output", 0, ActionScaleNode);
        machine.AddNode(ActionState, action, new Vector2(500, 100));

        machine.AddNode(HitState,
            new AnimationNodeAnimation { Animation = Clip(clips, "hit", idle) }, new Vector2(500, 250));
        machine.AddNode(DeathState,
            new AnimationNodeAnimation { Animation = Clip(clips, "death", idle) }, new Vector2(500, 400));

        Connect(machine, LocomotionState, ActionState, 0.12f);
        Connect(machine, ActionState, LocomotionState, 0.18f);
        Connect(machine, LocomotionState, HitState, 0.08f);
        Connect(machine, HitState, LocomotionState, 0.15f);
        Connect(machine, ActionState, HitState, 0.08f);

        // Death is reachable from anywhere and is never left; a respawn rebuilds the state rather
        // than transitioning out, so there is no path back to fall through by accident.
        var toDeath = new AnimationNodeStateMachineTransition
        {
            XfadeTime = 0.2f,
            SwitchMode = AnimationNodeStateMachineTransition.SwitchModeEnum.Immediate,
        };
        // ⚠️ AUTO, and this is the whole difference between a working tree and a dead one. A
        // state machine sits in its "Start" node until something advances it, and a transition
        // added with the default advance mode waits for a condition that is never set — so the
        // machine parks on Start forever, plays nothing, and reports no error at all. The character
        // simply stands in its bind pose, which is indistinguishable from every other animation
        // defect in this repo. Travel() still works from there, which is what made it look like
        // "actions are slow" rather than "locomotion never started".
        machine.AddTransition("Start", LocomotionState, new AnimationNodeStateMachineTransition
        {
            AdvanceMode = AnimationNodeStateMachineTransition.AdvanceModeEnum.Auto,
            SwitchMode = AnimationNodeStateMachineTransition.SwitchModeEnum.Immediate,
        });
        machine.AddTransition(LocomotionState, DeathState, toDeath);
        machine.AddTransition(ActionState, DeathState, toDeath);
        machine.AddTransition(HitState, DeathState, toDeath);

        var root = new AnimationNodeBlendTree();
        root.AddNode(StateMachineNode, machine, new Vector2(0, 0));
        root.AddNode(UpperBodyNode,
            new AnimationNodeAnimation { Animation = Clip(clips, "block", idle) }, new Vector2(0, 220));

        var layer = new AnimationNodeBlend2 { FilterEnabled = true };
        root.AddNode(LayerNode, layer, new Vector2(320, 80));
        root.ConnectNode(LayerNode, 0, StateMachineNode);
        root.ConnectNode(LayerNode, 1, UpperBodyNode);
        root.ConnectNode("output", 0, LayerNode);

        ApplyUpperBodyFilter(layer, skeleton);
        return root;
    }

    /// <summary>
    /// Marks the upper-body bones so the layer overrides only those.
    ///
    /// ⚠️ The filter is a list of TRACK paths, and a path that matches no track is silently ignored —
    /// so a filter built against the wrong skeleton name produces a layer that overrides the whole
    /// body instead of an arm, and nothing says so. The paths are built from the live skeleton's own
    /// unique name, which is what the retargeted clips address.
    /// </summary>
    private static void ApplyUpperBodyFilter(AnimationNodeBlend2 layer, Skeleton3D? skeleton)
    {
        if (skeleton == null)
        {
            return;
        }

        string prefix = $"%{skeleton.Name}:";
        foreach (string bone in UpperBodyBones)
        {
            if (skeleton.FindBone(bone) >= 0)
            {
                layer.SetFilterPath(prefix + bone, true);
            }
        }
    }

    private static void Connect(
        AnimationNodeStateMachine machine, string from, string to, float xfade)
    {
        machine.AddTransition(from, to, new AnimationNodeStateMachineTransition
        {
            XfadeTime = xfade,
            SwitchMode = AnimationNodeStateMachineTransition.SwitchModeEnum.Immediate,
            AdvanceMode = AnimationNodeStateMachineTransition.AdvanceModeEnum.Disabled,
        });
    }

    private static string Clip(IReadOnlyDictionary<string, string> clips, string slot, string fallback) =>
        clips.TryGetValue(slot, out string? clip) && clip.Length > 0 ? clip : fallback;
}
