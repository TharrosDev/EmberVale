namespace Embervale.Core;

/// <summary>
/// Play-time input gates that are not menus. A menu (<see cref="UiState"/>) suspends the whole
/// controller and usually pauses the world; a gate lends a few inputs to an overlay while everything
/// else stays live.
///
/// <para><see cref="WheelOpen"/> is the spell wheel: the mouse and the right stick steer the wheel
/// instead of the camera, lock-on flicks are ignored, and attack, block, cast and the lock toggle are
/// held back. Move, jump, dodge and sprint are untouched and the mouse stays captured. Its one writer
/// is <c>SpellWheelInput</c>; a menu opening and a session teardown both clear it through
/// <see cref="UiState"/>.</para>
/// </summary>
public static class PlayGate
{
    /// <summary>True while the spell wheel has the look inputs.</summary>
    public static bool WheelOpen { get; set; }
}
