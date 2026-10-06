using System.Collections.Generic;
using Embervale.Appearance;
using Embervale.Core.Services;
using Embervale.Player;
using Embervale.Save;
using Embervale.UI;
using Godot;

namespace Embervale.Debugging;

/// <summary>
/// Rendered coverage of the session's meta screens: the pause sheet, the death screen in its hold and
/// with its choices, the creator at each step with its turntable and light rigs, the save-slot rows
/// (every kind, and a delete held half way) and an ending card over each painting.
/// <c>godot --path . -- --metashots</c> continues the most recent save, like the other session harnesses.
/// Run WITHOUT <c>--headless</c>.
///
/// Nothing here changes the save it runs on: the death screen is shown without a death, the slot
/// rows are fixtures and their held Delete completes to nothing, and an ending is shown without its
/// flag, so the save never records that the game was finished.
/// </summary>
public sealed partial class MetaShots : ShotHarness
{
    private const string CreatorName = "MetaCreator";
    private const string SlotsName = "MetaSaveSlots";
    private const string HeldSlot = "slot1";
    private const float TurnLeft = -70f;
    private const float TurnBack = 150f;

    /// <summary>Into the second card's hold: the card is at full strength and so is the painting.</summary>
    private const float EndingAt = OpeningTimeline.CardSeconds + OpeningTimeline.FadeSeconds + 1f;

    private CharacterCreator? _creator;
    private SaveSlotPanel? _slots;

    protected override string Flag => "--metashots";

    protected override string OutputDir => "user://metashots";

    protected override string? ValidateShotState(string name)
    {
        if (ServiceLocator.Instance is not { } locator || !locator.TryGet(out PlayerCharacter _))
        {
            return "player is not registered";
        }

        return name switch
        {
            "01-pause-sheet" when Find<PauseMenu>() is not { ShownForCapture: true } => "the pause sheet is not up",
            "03-death-hold" when Find<DeathScreen>() is not { Active: true, OptionsShown: false } =>
                "the death screen is not in its hold",
            "04-death-options" when Find<DeathScreen>() is not { Active: true, OptionsShown: true } =>
                "the death screen is not showing its choices",
            "05-death-closed" when Find<DeathScreen>() is { Active: true } => "the death screen is still up",
            _ when name.Contains("-creator-") && !name.EndsWith("-closed") => ValidateCreatorShot(name),
            _ when name.Contains("-slots-") && !name.EndsWith("-closed") => ValidateSlotsShot(name),
            "20-ending-dawnfire" or "21-ending-embers" when Find<EndingSequence>() is not { BackdropShownForCapture: true } =>
                "no painting is drawn under the ending card",
            "22-narration-paused" when Find<EndingSequence>() is not { PausedForCapture: true } =>
                "the Resume / Skip prompt is not up",
            _ => null,
        };
    }

    /// <summary>The step, framing, yaw and rig each creator shot claims to show.</summary>
    private string? ValidateCreatorShot(string name)
    {
        if (_creator == null || !IsInstanceValid(_creator))
        {
            return "the creator is not open";
        }

        CharacterPreview preview = _creator.PreviewForCapture;
        if (!preview.HasModel)
        {
            return "the preview has no model";
        }

        (CreatorStep step, PreviewFraming framing, float yaw, int rig) = name switch
        {
            "07-creator-appearance" => (CreatorStep.Appearance, PreviewFraming.Body, CreatorRules.DefaultYaw, 0),
            "08-creator-appearance-face" => (CreatorStep.Appearance, PreviewFraming.Face, CreatorRules.DefaultYaw, 0),
            "09-creator-background" => (CreatorStep.Background, PreviewFraming.Body, CreatorRules.DefaultYaw, 0),
            "10-creator-name" => (CreatorStep.Name, PreviewFraming.Body, CreatorRules.DefaultYaw, 0),
            "11-creator-turn-left" => (CreatorStep.Race, PreviewFraming.Body, TurnLeft, 0),
            "12-creator-turn-back" => (CreatorStep.Race, PreviewFraming.Body, TurnBack, 0),
            "14-creator-rig-overcast" => (CreatorStep.Race, PreviewFraming.Body, CreatorRules.DefaultYaw, 1),
            "15-creator-rig-dusk" => (CreatorStep.Race, PreviewFraming.Body, CreatorRules.DefaultYaw, 2),
            _ => (CreatorStep.Race, PreviewFraming.Body, CreatorRules.DefaultYaw, 0),
        };

        if (_creator.StepForCapture != step)
        {
            return $"the creator is on {_creator.StepForCapture}, expected {step}";
        }

        if (preview.Framing != framing)
        {
            return $"the preview frames the {preview.Framing}, expected the {framing}";
        }

        if (!Mathf.IsEqualApprox(preview.Yaw, yaw))
        {
            return $"the figure stands at {preview.Yaw:0.#} degrees, expected {yaw:0.#}";
        }

        return preview.RigIndex == rig ? null : $"light rig {preview.RigIndex} is on, expected {rig}";
    }

    private string? ValidateSlotsShot(string name)
    {
        if (_slots == null || !IsInstanceValid(_slots))
        {
            return "the slot browser is not open";
        }

        if (_slots.RowCountForCapture != SlotFixtures().Count)
        {
            return $"the list holds {_slots.RowCountForCapture} row(s), expected {SlotFixtures().Count}";
        }

        // Mid-hold: started, and not yet closed. A ring that is full has run past the frame it was for.
        if (name == "18-slots-delete-hold" && _slots.HoldProgressForCapture is not (> 0f and < 1f))
        {
            return $"the Delete hold is at {_slots.HoldProgressForCapture:0.##}, expected part way";
        }

        return null;
    }

    protected override void BuildShotList()
    {
        Shot("00-world", () => { });

        Shot("01-pause-sheet", () => Find<PauseMenu>()?.OpenForCapture());
        Shot("02-pause-closed", () => Find<PauseMenu>()?.CloseForCapture());

        // The hold first (the line alone, the choices not yet offered), then the choices.
        Shot("03-death-hold", () => Find<DeathScreen>()?.BeginForCapture());
        Shot("04-death-options", () => Find<DeathScreen>()?.ShowOptionsForCapture());
        Shot("05-death-closed", () => Find<DeathScreen>()?.EndForCapture());

        // The creator, over the running session: the same screen a New Game opens on the title.
        Shot("06-creator-race", OpenCreator);
        Shot("07-creator-appearance", () => _creator?.ShowStepForCapture(CreatorStep.Appearance));
        Shot("08-creator-appearance-face", () => _creator?.FocusSlotForCapture(AppearanceSlot.Hair));
        Shot("09-creator-background", () => _creator?.SelectBackgroundForCapture("background.hunter"));
        Shot("10-creator-name", () => _creator?.ShowStepForCapture(CreatorStep.Name));

        Shot("11-creator-turn-left", () =>
        {
            _creator?.ShowStepForCapture(CreatorStep.Race);
            _creator?.SetYawForCapture(TurnLeft);
        });
        Shot("12-creator-turn-back", () => _creator?.SetYawForCapture(TurnBack));

        Shot("13-creator-rig-hearth", () =>
        {
            _creator?.SetYawForCapture(CreatorRules.DefaultYaw);
            _creator?.SetRigForCapture(0);
        });
        Shot("14-creator-rig-overcast", () => _creator?.SetRigForCapture(1));
        Shot("15-creator-rig-dusk", () => _creator?.SetRigForCapture(2));
        Shot("16-creator-closed", CloseCreator);

        Shot("17-slots-variants", OpenSlots);
        Shot("18-slots-delete-hold", () => _slots?.HoldDeleteForCapture(HeldSlot));
        Shot("19-slots-closed", CloseSlots);

        Shot("20-ending-dawnfire", () => ShowEnding(dawnfire: true));
        Shot("21-ending-embers", () => ShowEnding(dawnfire: false));
        Shot("22-narration-paused", () => Find<EndingSequence>()?.PauseForCapture());
        Shot("23-narration-closed", () => Find<EndingSequence>()?.EndForCapture());
    }

    private T? Find<T>()
        where T : Node => QuestShotFixtures.FindFirst<T>(GetTree().Root);

    private void OpenCreator()
    {
        _creator = new CharacterCreator { Name = CreatorName };
        _creator.Configure(_ => { }, () => { });
        GetTree().Root.AddChild(_creator);
    }

    private void CloseCreator()
    {
        GetTree().Root.GetNodeOrNull(CreatorName)?.QueueFree();
        _creator = null;
    }

    private void OpenSlots()
    {
        _slots = new SaveSlotPanel { Name = SlotsName };
        _slots.Configure(SaveSlotPanel.Intent.Load, _ => { }, () => { });
        GetTree().Root.AddChild(_slots);
        _slots.ShowRowsForCapture(SlotFixtures());
    }

    private void CloseSlots()
    {
        _slots?.ReleaseHoldForCapture();
        GetTree().Root.GetNodeOrNull(SlotsName)?.QueueFree();
        _slots = null;
    }

    /// <summary>The second card of an ending, held, over its painting. The script is the real one
    /// (<see cref="EndingSequence.Script"/>); only the flag that would start it is left unset.</summary>
    private void ShowEnding(bool dawnfire)
    {
        if (Find<EndingSequence>() is not { } ending)
        {
            return;
        }

        ending.EndForCapture();
        ending.ShowCardForCapture(EndingSequence.Script(dawnfire, absorbed: 0), string.Empty, EndingAt);
    }

    /// <summary>One row of every kind the browser draws: a manual, a quick and an autosave across the
    /// corruption tiers, one read from its backup, one damaged and one from a newer build.</summary>
    private static List<SaveSlotInfo> SlotFixtures() => new()
    {
        Fixture(HeldSlot, SaveKind.Manual, "Aldric", "Ember Crown", 14, "Untainted", hours: 9.4),
        Fixture(SaveSlots.Quick, SaveKind.Quick, "Aldric", "Frostfang Reach", 15, "Marked", hours: 11.1),
        Fixture("auto1", SaveKind.Auto, "Seren", "Ashen Wilds", 27, "Embers", hours: 31.7, backup: true),
        Fixture("slot2", SaveKind.Manual, "Seren", "Sunspire Dominion", 22, "Touched", hours: 24.2, health: SaveHealth.Corrupt),
        Fixture("slot3", SaveKind.Manual, "Wanderer", "Ember Crown", 3, "Untainted", hours: 0.6, health: SaveHealth.Newer),
    };

    private static SaveSlotInfo Fixture(
        string slot, SaveKind kind, string name, string region, int level, string tier, double hours,
        SaveHealth health = SaveHealth.Ok, bool backup = false) => new()
    {
        Slot = slot,
        Kind = kind,
        CharacterName = name,
        Region = region,
        Level = level,
        CorruptionTier = tier,
        PlaytimeSeconds = hours * 3600d,
        TimestampUnix = Time.GetUnixTimeFromSystem() - (hours * 600d),
        Health = health,
        PrimaryHealth = backup ? SaveHealth.Corrupt : health,
        RecoveredFromBackup = backup,
        FormatVersion = health == SaveHealth.Newer ? SaveManager.CurrentFormatVersion + 1 : SaveManager.CurrentFormatVersion,
    };
}
