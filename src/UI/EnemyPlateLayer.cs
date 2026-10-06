using Godot;

namespace Embervale.UI;

/// <summary>
/// World-anchored health plates over the enemies the player is fighting.
///
/// A child of <see cref="CombatFeedbackOverlay"/>, beside the damage numbers and the lock cues, so
/// it shares their canvas layer and their visibility rule. Empty until the plates are built: it
/// draws nothing and does not tick.
/// </summary>
public sealed partial class EnemyPlateLayer : Control
{
    /// <summary>The most plates on screen at once.</summary>
    public const int MaxPlates = 8;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
        SetProcess(false);
    }
}
