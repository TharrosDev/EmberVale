using Embervale.Core;
using Embervale.Localization;
using Embervale.Save;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The title / main menu (Phase 24A): the first thing shown on launch, before any world is
/// built. <see cref="Bootstrap.GameShellController"/> boots into <see cref="GameState.MainMenu"/> and shows this
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
        Texture2D? art = GD.Load<Texture2D>("res://assets/ui/backgrounds/menu_ashen_causeway.png");
        var image = new TextureRect
        {
            Texture = art,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        image.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(image);

        var backdrop = UiTheme.Scrim(0.46f);
        backdrop.MouseFilter = Control.MouseFilterEnum.Stop;
        AddChild(backdrop);

        PanelContainer panel = UiTheme.Panel();
        panel.AnchorLeft = 0.64f;
        panel.AnchorRight = 0.94f;
        panel.AnchorTop = 0.15f;
        panel.AnchorBottom = 0.85f;
        panel.GrowHorizontal = Control.GrowDirection.Both;
        panel.GrowVertical = Control.GrowDirection.Both;
        panel.CustomMinimumSize = new Vector2(360, 420);
        AddChild(panel);
        _panel = panel;

        MarginContainer pad = UiTheme.Padding(UiTheme.SpaceXl);
        panel.AddChild(pad);

        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", UiTheme.SpaceSm);
        pad.AddChild(col);

        TextureRect seal = new()
        {
            Texture = GD.Load<Texture2D>("res://assets/ui/emblems/embervale_seal.png"),
            CustomMinimumSize = new Vector2(96f, 96f),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        col.AddChild(seal);

        // The title screen and the spellbook are the only two surfaces that get the shimmer (see
        // the ornament budget in UiOrnament). The label and the sweep are stacked in a fixed-height
        // Control so the sweep spans the title rather than the whole column.
        var titleStack = new Control { CustomMinimumSize = new Vector2(0f, 40f) };
        Label title = UiTheme.Display(Loc.T("menu.title"));
        title.HorizontalAlignment = HorizontalAlignment.Left;
        title.VerticalAlignment = VerticalAlignment.Center;
        title.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        titleStack.AddChild(title);
        titleStack.AddChild(UiOrnament.InkShimmer(UiTheme.Accent, period: 8f, intensity: 0.45f));
        col.AddChild(titleStack);

        Label subtitle = UiTheme.Prose(Loc.T("menu.subtitle"), UiTheme.Dim);
        subtitle.HorizontalAlignment = HorizontalAlignment.Left;
        subtitle.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        col.AddChild(subtitle);

        col.AddChild(UiTheme.Divider());
        col.AddChild(new Control { CustomMinimumSize = new Vector2(0f, UiTheme.SpaceSm) });

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

    /// <summary>Deterministic screenshot entry point; follows the same settings path as the menu button.</summary>
    public void OpenSettingsForCapture() => OpenSettings();

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
        button.CustomMinimumSize = new Vector2(0, 46);
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
