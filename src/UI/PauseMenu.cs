using Embervale.Core;
using Embervale.Localization;
using Embervale.Save;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The pause menu (Phase 18): a real modal menu on the <c>pause</c> action (Esc) — Resume,
/// quick Save / Load, and Quit — replacing the bare pause toggle. It runs with
/// <see cref="Node.ProcessModeEnum.Always"/> so its buttons work while the tree is paused,
/// dims the scene behind a backdrop, and drives the <see cref="GameManager"/> pause state
/// (which frees/recaptures the mouse through the player controller). Built via
/// <see cref="UiTheme"/>.
/// </summary>
public partial class PauseMenu : CanvasLayer
{
	private ColorRect _backdrop = null!;
	private PanelContainer _panel = null!;
	private bool _open;

	public override void _Ready()
	{
		ProcessMode = ProcessModeEnum.Always;
		Layer = 10; // above the rest of the UI
		Build();
		SetPanelVisible(false);
	}

	public override void _Process(double delta)
	{
		if (!Godot.Input.IsActionJustPressed(GameInput.Pause))
		{
			return;
		}

		// While a higher modal (the settings panel) owns the screen it sets UiState.MenuOpen and
		// consumes Esc to close itself — don't also resume the game on that same press.
		if (UiState.MenuOpen)
		{
			return;
		}

		if (_open)
		{
			Resume();
		}
		else if (GameManager.Instance is { IsPlaying: true })
		{
			Open();
		}
	}

	private void Build()
	{
		_backdrop = new ColorRect { Color = new Color(0f, 0f, 0f, 0.55f) };
		_backdrop.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_backdrop.MouseFilter = Control.MouseFilterEnum.Stop;
		AddChild(_backdrop);

		_panel = UiTheme.Panel();
		_panel.SetAnchorsPreset(Control.LayoutPreset.Center);
		// Grow from the centre anchor in both directions so the panel is truly centred
		// (the default End grow would push it toward the bottom-right of centre).
		_panel.GrowHorizontal = Control.GrowDirection.Both;
		_panel.GrowVertical = Control.GrowDirection.Both;
		_panel.CustomMinimumSize = new Vector2(280, 0);
		AddChild(_panel);

		MarginContainer pad = UiTheme.Padding(16);
		_panel.AddChild(pad);

		var col = new VBoxContainer();
		col.AddThemeConstantOverride("separation", 8);
		pad.AddChild(col);

		Label header = UiTheme.Header(Loc.T("pause.title"));
		header.HorizontalAlignment = HorizontalAlignment.Center;
		col.AddChild(header);
		col.AddChild(new HSeparator());

		col.AddChild(MenuButton(Loc.T("pause.resume"), Resume));
		col.AddChild(MenuButton(Loc.T("pause.save"), () => { if (SaveManager.Instance is { } s) { s.SaveGame(s.ActiveSlot); } }));
		col.AddChild(MenuButton(Loc.T("pause.load"), () => { if (SaveManager.Instance is { } s) { s.LoadGame(s.ActiveSlot); } }));
		col.AddChild(MenuButton(Loc.T("pause.settings"), OpenSettings));
		col.AddChild(MenuButton(Loc.T("pause.quit"), () => GetTree().Quit()));
	}

	private static Button MenuButton(string text, System.Action onPressed)
	{
		Button button = UiTheme.Action(text);
		button.CustomMinimumSize = new Vector2(0, 34);
		button.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		button.Pressed += () => onPressed();
		return button;
	}

	private void OpenSettings()
	{
		// Hide the pause panel behind the settings overlay; restore it when the player backs out.
		// The game stays paused throughout, and UiState.MenuOpen (set by the panel) keeps Esc from
		// resuming until the panel is closed.
		SetPanelVisible(false);
		SettingsPanel.Open(this, () => SetPanelVisible(true));
	}

	private void Open()
	{
		_open = true;
		SetPanelVisible(true);
		GameManager.Instance?.ChangeState(GameState.Paused);
	}

	private void Resume()
	{
		_open = false;
		SetPanelVisible(false);
		GameManager.Instance?.ChangeState(GameState.Playing);
	}

	private void SetPanelVisible(bool visible)
	{
		_backdrop.Visible = visible;
		_panel.Visible = visible;
	}
}
