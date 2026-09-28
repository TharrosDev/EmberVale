using System.Collections.Generic;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Dialogue;
using Embervale.Player;

namespace Embervale.UI;

/// <summary>
/// Flamebearer visions (Phase 43.5, finish run): when the player closes a fallen Flamebearer's ember
/// conversation, three cards show how that champion fell. Keyed on the absorb dialogue ending rather
/// than the kill, so the vision never plays over the choice it follows. <c>flag.vision.&lt;name&gt;</c>
/// marks it seen, so it plays once per save.
/// </summary>
public partial class VisionSequence : NarrationSequence
{
    /// <summary>Absorb dialogue id → vision name (the locale keys are <c>vision.&lt;name&gt;.1..3</c>).</summary>
    public static readonly IReadOnlyDictionary<string, string> Visions = new Dictionary<string, string>
    {
        ["dialogue.iron_king_absorb"] = "iron_king",
        ["dialogue.storm_tyrant_absorb"] = "storm_tyrant",
        ["dialogue.beast_lord_absorb"] = "beast_lord",
        ["dialogue.crimson_prophet_absorb"] = "crimson_prophet",
        ["dialogue.hollow_queen_absorb"] = "hollow_queen",
        ["dialogue.ashen_knight_absorb"] = "ashen_knight",
    };

    protected override void OnReady()
    {
        EventBus.Instance?.Subscribe<DialogueEndedEvent>(OnDialogueEnded);
    }

    public override void _ExitTree()
    {
        EventBus.Instance?.Unsubscribe<DialogueEndedEvent>(OnDialogueEnded);
    }

    private void OnDialogueEnded(DialogueEndedEvent e)
    {
        if (!Visions.TryGetValue(e.Dialogue.Id, out string? name) || Flags() is not { } flags ||
            flags.Has(SeenFlag(name)))
        {
            return;
        }

        flags.Set(SeenFlag(name));
        PlayCards(new[] { $"vision.{name}.1", $"vision.{name}.2", $"vision.{name}.3" }, string.Empty);
    }

    public static string SeenFlag(string name) => $"flag.vision.{name}";

    protected override void OnSequenceFinished()
    {
    }

    private static StoryFlagsComponent? Flags() =>
        ServiceLocator.Instance is { } sl && sl.TryGet(out PlayerCharacter player)
            ? player.GetComponent<StoryFlagsComponent>()
            : null;
}
