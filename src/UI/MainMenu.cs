using Embervale.Core;
using Embervale.Localization;
using Embervale.Save;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The title / main menu (Phase 24A): the first thing shown on launch, before any world is
/// built. <see cref="GameBootstrap"/> boots into <see cref="GameState.MainMenu"/> and shows this
/// instead of constructing the sandbox. <b>New Game</b> and <b>Load Game</b> open the
/// <see cref="SaveSlotPanel"/> to pick a slot (24C), <b>Continue</b> resumes the most-recent save,
/// and <b>Quit</b> exits. <b>Settings</b> opens the <see cref="SettingsPanel"/> (24F). Built in code
/// through <see cref="UiTheme"/>, mirroring <see cref="PauseMenu"/>.
/// </summary>
public partial class MainMenu : CanvasLayer
{
    /// <summary>Invoked with the chosen slot and the created character when the player starts a new game.</summary>
    public System.Action<string, Races.CharacterProfile>? NewCharacterRequested { get; set; }

    /// <summary>Invoked with the chosen slot when the player loads/continues a save.</summary>
    public System.Action<string>? LoadGameRequested { get; set; }

    private PanelContainer _panel = null!;

    public override void _Ready()
    {
        Layer = 11; // above the (not-yet-built) HUD and the pause menu
        // No world/player yet on the title screen, so make sure the cursor is free.
        Godot.Input.MouseMode = Godot.Input.MouseModeEnum.Visible;
        Build();

        // Gamepad/keyboard start on the first button, and re-land there whenever a sub-screen
        // (slots, creator, settings) restores the menu (30.5J).
        UiFocus.GrabFirst(_panel);
        VisibilityChanged += () =>
        {
            if (Visible)
            {
                UiFocus.GrabFirst(_panel);
            }
        };
    }

    private void Build()
    {
        var backdrop = UiTheme.Scrim(1f);
        backdrop.MouseFilter = Control.MouseFilterEnum.Stop;
        AddChild(backdrop);

        PanelContainer panel = UiTheme.Panel();
        panel.SetAnchorsPreset(Control.LayoutPreset.Center);
        panel.GrowHorizontal = Control.GrowDirection.Both;
        panel.GrowVertical = Control.GrowDirection.Both;
        panel.CustomMinimumSize = new Vector2(320, 0);
        AddChild(panel);
        _panel = panel;

        MarginContainer pad = UiTheme.Padding(20);
        panel.AddChild(pad);
        panel.AddChild(UiOrnament.CornerBrass(arm: 20f, thickness: 2f, inset: 5f));

        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 8);
        pad.AddChild(col);

        // The title screen and the spellbook are the only two surfaces that get the shimmer (see
        // the ornament budget in UiOrnament). The label and the sweep are stacked in a fixed-height
        // Control so the sweep spans the title rather than the whole column.
        var titleStack = new Control { CustomMinimumSize = new Vector2(0f, 40f) };
        Label title = UiTheme.Display(Loc.T("menu.title"));
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.VerticalAlignment = VerticalAlignment.Center;
        title.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        titleStack.AddChild(title);
        titleStack.AddChild(UiOrnament.InkShimmer(UiTheme.Accent, period: 8f, intensity: 0.45f));
        col.AddChild(titleStack);

        Label subtitle = UiTheme.Body(Loc.T("menu.subtitle"), UiTheme.Dim);
        subtitle.HorizontalAlignment = HorizontalAlignment.Center;
        subtitle.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        col.AddChild(subtitle);

        col.AddChild(UiTheme.Divider());

        bool hasSaves = (SaveManager.Instance?.ListSlots().Count ?? 0) > 0;

        col.AddChild(MenuButton(Loc.T("menu.new_game"), () => OpenSlotPanel(SaveSlotPanel.Intent.New)));
        col.AddChild(MenuButton(Loc.T("menu.continue"), hasSaves ? ContinueMostRecent : null));
        col.AddChild(MenuButton(Loc.T("menu.load_game"), hasSaves ? () => OpenSlotPanel(SaveSlotPanel.Intent.Load) : null));
        col.AddChild(MenuButton(Loc.T("menu.settings"), OpenSettings));
        col.AddChild(MenuButton(Loc.T("menu.quit"), () => GetTree().Quit()));
    }

    private void OpenSlotPanel(SaveSlotPanel.Intent mode)
    {
        var panel = new SaveSlotPanel();
        System.Action<string> chosen = mode == SaveSlotPanel.Intent.New
            ? OpenCreator
            : slot => LoadGameRequested?.Invoke(slot);

        // Hide the menu behind the panel; restore it if the player backs out.
        Visible = false;
        panel.Configure(mode, chosen, () => Visible = true);
        AddChild(panel);
    }

    /// <summary>After a New-Game slot is picked, run the character creator (Phase 26D); confirm starts
    /// the game with the created profile, back returns to the title screen.</summary>
    private void OpenCreator(string slot)
    {
        var creator = new CharacterCreator();
        creator.Configure(
            profile => NewCharacterRequested?.Invoke(slot, profile),
            () => Visible = true);
        AddChild(creator);
    }

    private void OpenSettings()
    {
        // Hide the menu behind the settings panel; restore it when the player backs out.
        Visible = false;
        SettingsPanel.Open(this, () => Visible = true);
    }

    private void ContinueMostRecent()
    {
        if (SaveManager.Instance is not { } manager)
        {
            return;
        }

        SaveSlotInfo? latest = null;
        foreach (SaveSlotInfo info in manager.ListSlots())
        {
            if (latest == null || info.TimestampUnix > latest.TimestampUnix)
            {
                latest = info;
            }
        }

        if (latest != null)
        {
            LoadGameRequested?.Invoke(latest.Slot);
        }
    }

    private static Button MenuButton(string text, System.Action? onPressed)
    {
        Button button = UiTheme.Action(text);
        button.CustomMinimumSize = new Vector2(0, 36);
        button.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        if (onPressed == null)
        {
            button.Disabled = true;
            button.TooltipText = Loc.T("menu.coming_soon");
        }
        else
        {
            button.Pressed += () => onPressed();
        }

        return button;
    }
}
