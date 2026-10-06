using Embervale.Magic;
using Embervale.Player;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The spell wheel: a radial pick of the eight favourites, the six schools and a school's own spells,
/// drawn over the HUD while the spell-wheel button is held. It sits in <see cref="HudLayout.Overlay"/>
/// (built by <see cref="GameHud"/>) and is driven through <see cref="SpellWheelInput"/>; the geometry
/// is <see cref="SpellWheelRules"/>.
///
/// <para>It is not a <c>UiPanel</c> and never calls <c>UiState.Open</c>: the world keeps running, the
/// mouse stays captured and the player can still move, jump, dodge and sprint. Hidden through
/// <see cref="HudElement.SpellWheel"/>, it refuses to open and the button steps to the next spell.</para>
///
/// <para>Seam: nothing is drawn yet. <see cref="OpenWheel"/> refuses, so the button behaves as it
/// did before the wheel existed.</para>
/// </summary>
public partial class SpellWheel : Control, ISpellWheelView
{
    // Built in the constructor so the anchors are set before tree entry (see HudLayout).
    public SpellWheel()
    {
        Name = "SpellWheel";
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
    }

    public override void _EnterTree() => SpellWheelInput.Register(this);

    public override void _ExitTree() => SpellWheelInput.Unregister(this);

    public bool OpenWheel(SpellcastingComponent caster, bool toggled) => false;

    public void MoveCursor(Vector2 mouseDelta)
    {
    }

    public void SetStick(Vector2 stick)
    {
    }

    public void CloseWheel(bool confirm)
    {
    }
}
