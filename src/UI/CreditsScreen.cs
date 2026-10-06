using Embervale.Core;
using Embervale.Localization;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The credits, reachable from the title. A shell screen like <see cref="SettingsPanel"/>: the
/// screen that opened it hides behind it and comes back on Back.
///
/// For now it is a title and a way out; the scrolling roll is built on top of this.
/// </summary>
public partial class CreditsScreen : CanvasLayer
{
    private System.Action? _onBack;

    /// <summary>Opens the screen as a child of <paramref name="parent"/>, invoking
    /// <paramref name="onBack"/> when the player backs out.</summary>
    public static CreditsScreen Open(Node parent, System.Action? onBack = null)
    {
        var screen = new CreditsScreen { _onBack = onBack };
        parent.AddChild(screen);
        return screen;
    }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        Layer = 13; // above the main menu (11) and the slot panel (12), like the settings panel
        UiState.Open(this);
        Godot.Input.MouseMode = Godot.Input.MouseModeEnum.Visible;
        Build();
    }

    public override void _ExitTree()
    {
        UiState.Close(this);
    }

    public override void _Process(double delta)
    {
        if (Godot.Input.IsActionJustPressed(UiLive.Pause) ||
            Godot.Input.IsActionJustPressed(UiLive.UiCancel))
        {
            Back();
        }
    }

    private void Build()
    {
        (Control root, VBoxContainer column) = UiTheme.Sheet(scrimOpacity: 0.92f, centred: true);
        AddChild(root);

        column.AddChild(UiTheme.Title(Loc.T("credits.title")));
        column.AddChild(UiTheme.Divider());

        Button back = UiTheme.Action(Loc.T("common.back"), UiCue.Back);
        back.Pressed += Back;
        column.AddChild(back);

        UiFocus.GrabFirst(root);
    }

    private void Back()
    {
        System.Action? onBack = _onBack;
        QueueFree();
        onBack?.Invoke();
    }
}
