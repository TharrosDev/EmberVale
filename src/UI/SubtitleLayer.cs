using Godot;

namespace Embervale.UI;

/// <summary>
/// Where spoken lines are captioned.
///
/// Built with the session's shell (<c>UICompositionRoot.Subtitles</c>) so the layer that fills it
/// in touches no composition code. Until then it draws nothing: no line in the game was captioned
/// before it, and <c>Show</c> keeps it that way.
/// </summary>
public partial class SubtitleLayer : CanvasLayer
{
    public override void _Ready()
    {
        // A line spoken over a paused menu is still being spoken.
        ProcessMode = ProcessModeEnum.Always;
        Layer = 5; // over the HUD, under the chapter banner (6) and every menu
    }

    /// <summary>Captions one line for <paramref name="seconds"/>. <paramref name="speaker"/> is the
    /// already-localised name, or null for a line nobody is credited with.</summary>
    public void Show(string? speaker, string text, float seconds)
    {
    }

    /// <summary>Takes down whatever is showing.</summary>
    public void Dismiss()
    {
    }
}
