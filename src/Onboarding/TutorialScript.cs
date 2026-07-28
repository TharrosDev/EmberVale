using Embervale.Core;

namespace Embervale.Onboarding;

/// <summary>
/// The onboarding running order and its per-step copy (Phase 33B). Pure and Godot-free, so the
/// teaching <em>sequence</em> — which verb comes when, and what the player is asked to do — is
/// testable and lives in one readable place rather than being scattered through a state machine.
///
/// The design rule this encodes: onboarding is **observational**. Nothing here blocks input, gates a
/// door, or waits on a modal. A hint asks for a verb, the player does it (or doesn't, and wanders
/// off), and the sequence moves on when they do. A veteran can ignore every hint and lose nothing.
/// </summary>
public static class TutorialScript
{
    /// <summary>
    /// Every verb, in teaching order: the basics of moving and fighting (33B), then the verbs a
    /// player needs the moment they reach a town (33C). Interact comes first of those because it is
    /// the one a new player needs to make anything happen at all; magic comes last because it is the
    /// only optional verb in the list.
    /// </summary>
    public static readonly TutorialStep[] Steps =
    {
        TutorialStep.Look,
        TutorialStep.Move,
        TutorialStep.Sprint,
        TutorialStep.Attack,
        TutorialStep.Block,
        TutorialStep.Dodge,
        TutorialStep.Interact,
        TutorialStep.Inventory,
        TutorialStep.Journal,
        TutorialStep.Cast,
    };

    /// <summary>The first step of a fresh game.</summary>
    public static TutorialStep First => Steps.Length > 0 ? Steps[0] : TutorialStep.None;

    /// <summary>The step after <paramref name="step"/>, or <see cref="TutorialStep.None"/> when the
    /// sequence is finished. An unknown step also ends it, so bad saved state can never strand the
    /// player on a hint that no longer exists.</summary>
    public static TutorialStep Next(TutorialStep step)
    {
        int index = IndexOf(step);
        if (index < 0 || index + 1 >= Steps.Length)
        {
            return TutorialStep.None;
        }

        return Steps[index + 1];
    }

    /// <summary>Position of <paramref name="step"/> in the running order, or -1.</summary>
    public static int IndexOf(TutorialStep step)
    {
        for (int i = 0; i < Steps.Length; i++)
        {
            if (Steps[i] == step)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>The <c>Loc</c> key for a step's hint. The text carries a <c>{0}</c> the HUD fills
    /// with the live key/gamepad glyph, so a rebind never leaves the tutorial lying.</summary>
    public static string HintKey(TutorialStep step) => step switch
    {
        TutorialStep.Look => "tutorial.look",
        TutorialStep.Move => "tutorial.move",
        TutorialStep.Sprint => "tutorial.sprint",
        TutorialStep.Attack => "tutorial.attack",
        TutorialStep.Block => "tutorial.block",
        TutorialStep.Dodge => "tutorial.dodge",
        TutorialStep.Interact => "tutorial.interact",
        TutorialStep.Inventory => "tutorial.inventory",
        TutorialStep.Journal => "tutorial.journal",
        TutorialStep.Cast => "tutorial.cast",
        _ => string.Empty,
    };

    /// <summary>The input action whose glyph a step's hint shows, or empty when the step isn't bound
    /// to one action (looking is the mouse itself).</summary>
    public static string ActionFor(TutorialStep step) => step switch
    {
        TutorialStep.Move => GameInput.MoveForward,
        TutorialStep.Sprint => GameInput.Sprint,
        TutorialStep.Attack => GameInput.Attack,
        TutorialStep.Block => GameInput.Block,
        TutorialStep.Dodge => GameInput.Dodge,
        TutorialStep.Interact => GameInput.Interact,
        TutorialStep.Inventory => GameInput.Inventory,
        TutorialStep.Journal => GameInput.Journal,
        TutorialStep.Cast => GameInput.Cast,
        _ => string.Empty,
    };
}
