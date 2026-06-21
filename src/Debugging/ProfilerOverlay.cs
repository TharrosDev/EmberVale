using System.Text;
using Embervale.UI;
using Godot;

namespace Embervale.Debugging;

/// <summary>
/// A lightweight profiling overlay (toggled with <c>F4</c>): frame/physics time, draw calls,
/// node/orphan counts and static memory, read from Godot's <see cref="Performance"/> monitors.
/// Hidden by default and only updated while visible, so it costs nothing when off. Built
/// through <see cref="UiTheme"/> and parked top-right out of the game HUD's way.
/// </summary>
public partial class ProfilerOverlay : CanvasLayer
{
    private PanelContainer _panel = null!;
    private Label _text = null!;
    private bool _shown;

    public override void _Ready()
    {
        Layer = 8;
        Build();
        SetShown(false);
    }

    /// <summary>Shows/hides the overlay (bound to F4 by the bootstrap).</summary>
    public void Toggle() => SetShown(!_shown);

    public override void _Process(double delta)
    {
        if (!_shown)
        {
            return;
        }

        var sb = new StringBuilder();
        sb.Append($"FPS         {Engine.GetFramesPerSecond()}\n");
        sb.Append($"frame       {Ms(Performance.Monitor.TimeProcess)} ms\n");
        sb.Append($"physics     {Ms(Performance.Monitor.TimePhysicsProcess)} ms\n");
        sb.Append($"draw calls  {Get(Performance.Monitor.RenderTotalDrawCallsInFrame):0}\n");
        sb.Append($"nodes       {Get(Performance.Monitor.ObjectNodeCount):0}\n");
        sb.Append($"orphans     {Get(Performance.Monitor.ObjectOrphanNodeCount):0}\n");
        sb.Append($"static mem  {Get(Performance.Monitor.MemoryStatic) / (1024d * 1024d):0.0} MB");
        _text.Text = sb.ToString();
    }

    private void Build()
    {
        _panel = UiTheme.Panel();
        _panel.MouseFilter = Control.MouseFilterEnum.Ignore;
        _panel.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        _panel.OffsetRight = -16;
        _panel.OffsetTop = 220;
        _panel.GrowHorizontal = Control.GrowDirection.Begin;
        AddChild(_panel);

        MarginContainer pad = UiTheme.Padding(10);
        _panel.AddChild(pad);

        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 3);
        col.AddChild(UiTheme.Header("PROFILER  (F4)"));
        _text = UiTheme.Body("", UiTheme.Dim);
        col.AddChild(_text);
        pad.AddChild(col);
    }

    private void SetShown(bool shown)
    {
        _shown = shown;
        _panel.Visible = shown;
    }

    private static double Get(Performance.Monitor monitor) => Performance.GetMonitor(monitor);

    private static string Ms(Performance.Monitor monitor) => (Get(monitor) * 1000d).ToString("0.00");
}
