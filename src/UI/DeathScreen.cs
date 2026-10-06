using Godot;

namespace Embervale.UI;

/// <summary>
/// The screen between the player's death and what they do about it.
///
/// Built with the session's shell (<c>UICompositionRoot.Death</c>) so the screen that fills it in
/// touches no composition code. Until then it draws nothing and <see cref="Begin"/> does nothing:
/// death is still the same-frame respawn <c>PlayerHost</c> has always done.
/// </summary>
public partial class DeathScreen : CanvasLayer
{
    public override void _Ready()
    {
        // A death screen holds the world still and has to keep answering input while it does.
        ProcessMode = ProcessModeEnum.Always;
        Layer = 9; // above the HUD and the panels, below the pause menu (10)
        Visible = false;
    }

    /// <summary>Whether the screen is up.</summary>
    public bool Active => Visible;

    /// <summary>Called when the player has died, before any respawn.</summary>
    public void Begin()
    {
    }
}
