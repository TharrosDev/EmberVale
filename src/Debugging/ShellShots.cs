using Embervale.Bootstrap;
using Embervale.Core;
using Embervale.Core.Events;
using Embervale.UI;
using Godot;

namespace Embervale.Debugging;

/// <summary>Rendered title-shell coverage: the authored main menu, its real settings flow, the save-slot
/// browser in both intents and the loading screen, then the front of the shell: the boot splash,
/// first-run setup, the title under each act's painting, the quit prompt, the loading screen
/// for each realm at two stages, and the credits.</summary>
public sealed partial class ShellShots : ShotHarness
{
    protected override string Flag => "--shellshots";

    protected override string OutputDir => "user://shellshots";

    public MainMenu? Menu { get; set; }

    private CharacterCreator? _creator;
    private SaveSlotPanel? _slots;
    private BootSplash? _splash;
    private FirstRunSetup? _firstRun;
    private CreditsScreen? _credits;
    private float _creditsTopOffset = float.MaxValue;

    /// <summary>The realms with a loading painting, by the slug their shots carry.</summary>
    private static readonly (string Slug, string RegionId)[] Realms =
    {
        ("ember-crown", "region.ember_crown"),
        ("frostfang", "region.frostfang_reach"),
        ("ashen-wilds", "region.ashen_wilds"),
        ("sunspire", "region.sunspire"),
        ("pale", "region.pale_concord"),
        ("celestial", "region.celestial"),
    };

    /// <summary>The two gate stages each realm's loading screen is photographed at.</summary>
    private const int EarlyStep = 1;
    private const int LateStep = 3;

    protected override void BuildShotList()
    {
        Shot("00-main-menu", () => { });

        // The creator's two halves: the race picker on top, then the background picker with a
        // non-default background chosen so its kit, perk and lean badge are all on screen.
        Shot("01-creator-races", () => _creator = Menu?.OpenCreatorForCapture());

        // Appearance (P8): three races with different picks, so the swatches filter by race and the live
        // preview (the same PlayerAppearance.Apply as the world) visibly changes.
        Shot("01a-creator-look-human", () => _creator?.SelectLookForCapture("race.human",
            "appearance.skin.tan", "appearance.hair.blonde", "appearance.eyes.blue", "appearance.build.broad"));
        Shot("01b-creator-look-umbral", () => _creator?.SelectLookForCapture("race.umbral",
            "appearance.skin.ashen", "appearance.hair.midnight", "appearance.eyes.violet", "appearance.ember.violet", "appearance.build.slim"));
        Shot("01c-creator-look-draekyn", () => _creator?.SelectLookForCapture("race.draekyn",
            "appearance.skin.scarlet", "appearance.hair.cinder", "appearance.eyes.gold", "appearance.ember.gold"));
        Shot("01d-creator-look-sylthari", () => _creator?.SelectLookForCapture("race.sylthari",
            "appearance.skin.moss", "appearance.hair.violet", "appearance.eyes.ice", "appearance.build.slim"));

        Shot("02-creator-backgrounds-default", () =>
        {
            _creator?.SelectBackgroundForCapture("background.wayfarer");
            _creator?.ScrollForCapture(toEnd: false);
        });
        Shot("03-creator-backgrounds-hunter", () =>
        {
            _creator?.SelectBackgroundForCapture("background.hunter");
            _creator?.ScrollForCapture(toEnd: true);
        });

        Shot("04-settings", () =>
        {
            _creator?.QueueFree();
            Menu?.OpenSettingsForCapture();
        });
        Shot("05-settings-middle", () => ScrollSettings(0.5f));
        Shot("06-settings-end", () => ScrollSettings(1f));

        // --- ui-upgrade: settings (one block; ValidateSettingsShot checks each state occurred) ---
        Shot("06a-settings-audio", () => SettingsUi()?.ShowTabForCapture(1));
        Shot("06b-settings-controls", () => SettingsUi()?.ShowTabForCapture(2));
        Shot("06c-settings-bindings", () => ScrollSettings(0.45f));
        Shot("06d-settings-gameplay", () => SettingsUi()?.ShowTabForCapture(3));
        Shot("06e-settings-interface", () => SettingsUi()?.ShowTabForCapture(4));
        Shot("06f-settings-accessibility", () => SettingsUi()?.ShowTabForCapture(5));
        Shot("06g-settings-remap-listening", () =>
        {
            SettingsUi()?.ShowTabForCapture(2);
            SettingsUi()?.ListenForCapture(GameInput.Jump, gamepad: false);
        });
        Shot("06h-settings-remap-conflict", () => SettingsUi()?.ShowConflictForCapture(GameInput.Jump, GameInput.Interact));
        Shot("06i-settings-narrow", () =>
        {
            SettingsUi()?.ShowTabForCapture(5);
            SettingsUi()?.SetNarrowForCapture(true);
        });
        Shot("06j-settings-wide-again", () =>
        {
            SettingsUi()?.SetNarrowForCapture(false);
            SettingsUi()?.ShowTabForCapture(0);
        });
        // --- end ui-upgrade: settings ---
        Shot("07-settings-closed", CloseSettings);
        Shot("08-save-slots-load", () => OpenSlots(SaveSlotPanel.Intent.Load));
        Shot("09-save-slots-closed", CloseSlots);
        Shot("10-save-slots-new", () => OpenSlots(SaveSlotPanel.Intent.New));
        Shot("11-save-slots-new-closed", CloseSlots);
        Shot("12-loading", ShowLoading);
        Shot("13-loading-closed", HideLoading);

        // --- ui-upgrade: shell front (ValidateFrontShot checks each state occurred) ---
        Shot("14-splash", () => _splash = Menu?.OpenSplashForCapture());
        Shot("15-first-run", () =>
        {
            _splash?.QueueFree();
            _firstRun = Menu?.OpenFirstRunForCapture();
            _firstRun?.SettleForCapture();
        });
        for (int act = 1; act <= ShellFrontRules.LastAct; act++)
        {
            int shown = act;
            Shot($"16-title-act{shown}", () =>
            {
                _firstRun?.QueueFree();
                _firstRun = null;
                if (Menu is not null)
                {
                    Menu.Visible = true;
                    Menu.SetActForCapture(shown);
                    Menu.SettleForCapture();
                }
            });
        }

        Shot("17-quit-confirm", () => Menu?.OpenQuitConfirmForCapture());
        Shot("17a-quit-confirm-closed", () => Menu?.CloseQuitConfirmForCapture());

        foreach ((string slug, string regionId) in Realms)
        {
            Shot($"18-loading-{slug}-early", () => ShowLoadingFor(regionId, EarlyStep));
            Shot($"18-loading-{slug}-late", () => ShowLoadingFor(regionId, LateStep));
        }

        Shot("19-credits-top", () =>
        {
            HideLoading();
            _credits = Menu?.OpenCreditsForCapture();
            _credits?.SetScrollForCapture(0f);
        });
        Shot("19a-credits-mid", () => _credits?.SetScrollForCapture(0.55f));
        // --- end ui-upgrade: shell front ---
    }

    // --- ui-upgrade: settings ---

    private SettingsPanel? SettingsUi() => QuestShotFixtures.FindFirst<SettingsPanel>(GetTree().Root);

    protected override string? ValidateShotState(string name) =>
        name.Contains("-settings-", System.StringComparison.Ordinal) ? ValidateSettingsShot(name) : ValidateFrontShot(name);

    /// <summary>The tab, prompt and layout each settings shot claims to show.</summary>
    private string? ValidateSettingsShot(string name)
    {
        if (name.EndsWith("-closed", System.StringComparison.Ordinal))
        {
            return null;
        }

        if (SettingsUi() is not { } settings)
        {
            return "the settings panel is not open";
        }

        (int tab, bool listening, bool prompt, bool narrow) = name switch
        {
            "06a-settings-audio" => (1, false, false, false),
            "06b-settings-controls" or "06c-settings-bindings" => (2, false, false, false),
            "06d-settings-gameplay" => (3, false, false, false),
            "06e-settings-interface" => (4, false, false, false),
            "06f-settings-accessibility" => (5, false, false, false),
            "06g-settings-remap-listening" => (2, true, true, false),
            "06h-settings-remap-conflict" => (2, false, true, false),
            "06i-settings-narrow" => (5, false, false, true),
            _ => (0, false, false, false),
        };

        if (settings.TabForCapture != tab)
        {
            return $"settings tab is {settings.TabForCapture}, expected {tab}";
        }

        if (settings.ListeningForCapture != listening)
        {
            return listening ? "the panel is not listening for a binding" : "the panel is still listening for a binding";
        }

        if (settings.PromptOpenForCapture != prompt)
        {
            return prompt ? "no prompt is open" : "a prompt is still open";
        }

        // Only asked of the shot that forces it: on a handheld-sized run every shot is one column.
        if (narrow && !settings.NarrowForCapture)
        {
            return "the sheet is not in its one-column layout";
        }

        return QuestShotFixtures.FindFirst<ScrollContainer>(settings) is null ? "the option list is missing" : null;
    }

    // --- end ui-upgrade: settings ---

    private void ScrollSettings(float fraction)
    {
        if (QuestShotFixtures.FindFirst<SettingsPanel>(GetTree().Root) is { } settings &&
            QuestShotFixtures.FindFirst<ScrollContainer>(settings) is { } scroll)
        {
            scroll.ScrollVertical = (int)(scroll.GetVScrollBar().MaxValue * fraction);
        }
    }

    private void CloseSettings()
    {
        QuestShotFixtures.FindFirst<SettingsPanel>(GetTree().Root)?.QueueFree();
        if (Menu is not null)
        {
            Menu.Visible = true;
        }
    }

    // Opened the way the title opens it: the menu hidden, its painting kept behind the sheet.
    private void OpenSlots(SaveSlotPanel.Intent intent) => _slots = Menu?.OpenSlotsForCapture(intent);

    private void CloseSlots()
    {
        if (_slots is { } slots && IsInstanceValid(slots))
        {
            slots.QueueFree();
        }

        _slots = null;
        if (Menu is not null)
        {
            Menu.Visible = true;
        }
    }

    private void ShowLoading()
    {
        GetTree().Root.AddChild(new LoadingScreen { Name = "AuditLoading" });
        EventBus.Instance?.Publish(new GameStateChangedEvent(GameState.MainMenu, GameState.Loading));
    }

    private void HideLoading() => GetTree().Root.GetNodeOrNull("AuditLoading")?.QueueFree();

    // --- ui-upgrade: shell front ---

    /// <summary>The loading screen dressed for a load into <paramref name="regionId"/> with
    /// <paramref name="step"/> stages cleared, driven the way the gate drives it: the state
    /// change first, then the progress events.</summary>
    private void ShowLoadingFor(string regionId, int step)
    {
        if (GetTree().Root.GetNodeOrNull("AuditLoading") is null)
        {
            ShowLoading();
        }

        EventBus.Instance?.Publish(new LoadingProgressEvent(regionId, step));
    }

    /// <summary>The state each shell-front shot claims to show. Shots from before this block
    /// (and the "closed" ones) claim nothing here.</summary>
    private string? ValidateFrontShot(string name)
    {
        if (name == "14-splash")
        {
            return _splash is { } splash && IsInstanceValid(splash) && splash.WaitingForCapture
                ? null : "the boot splash is not up and waiting";
        }

        if (name == "15-first-run")
        {
            return _firstRun is { } setup && IsInstanceValid(setup) && setup.ReadyForCapture
                ? null : "first-run setup is not open with its questions built";
        }

        if (name.StartsWith("16-title-act", System.StringComparison.Ordinal))
        {
            if (Menu is not { Visible: true } menu)
            {
                return "the title is not showing";
            }

            int act = int.Parse(name[^1..]);
            return menu.ActForCapture == act ? null : $"the title painting is act {menu.ActForCapture}, expected {act}";
        }

        if (name == "17-quit-confirm")
        {
            return Menu is { QuitConfirmOpenForCapture: true } ? null : "the quit prompt is not open";
        }

        if (name.StartsWith("18-loading-", System.StringComparison.Ordinal))
        {
            return ValidateLoadingShot(name);
        }

        if (name is "19-credits-top" or "19a-credits-mid")
        {
            return ValidateCreditsShot(name);
        }

        return null;
    }

    private string? ValidateCreditsShot(string name)
    {
        if (_credits is not { } credits || !IsInstanceValid(credits) || credits.LineCountForCapture == 0)
        {
            return "the credits are not open";
        }

        if (name == "19-credits-top")
        {
            _creditsTopOffset = credits.OffsetForCapture;
            return null;
        }

        // The mid-scroll shot has to have moved: the same frame twice proves nothing.
        return credits.OffsetForCapture > _creditsTopOffset
            ? null : "the credits roll did not move from its opening position";
    }

    private string? ValidateLoadingShot(string name)
    {
        if (GetTree().Root.GetNodeOrNull<LoadingScreen>("AuditLoading") is not { ShownForCapture: true } loading)
        {
            return "the loading screen is not up";
        }

        foreach ((string slug, string regionId) in Realms)
        {
            if (!name.StartsWith($"18-loading-{slug}-", System.StringComparison.Ordinal))
            {
                continue;
            }

            int step = name.EndsWith("-early", System.StringComparison.Ordinal) ? EarlyStep : LateStep;
            if (loading.RegionForCapture != regionId)
            {
                return $"the loading screen is dressed for '{loading.RegionForCapture}', expected '{regionId}'";
            }

            if (loading.StepForCapture != step)
            {
                return $"the loading screen is on step {loading.StepForCapture}, expected {step}";
            }

            return loading.PaintingForCapture == LoadingCardRules.Painting(regionId)
                ? null : $"the painting is '{loading.PaintingForCapture}', expected '{LoadingCardRules.Painting(regionId)}'";
        }

        return "no realm matches this shot's name";
    }

    // --- end ui-upgrade: shell front ---
}
