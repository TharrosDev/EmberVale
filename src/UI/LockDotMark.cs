using Godot;

namespace Embervale.UI;

/// <summary>The control behind <see cref="UiTheme.LockDot"/>.</summary>
public partial class LockDotMark : Control
{
    public override void _Draw() => UiTheme.DrawLockDot(this, Size * 0.5f);
}
