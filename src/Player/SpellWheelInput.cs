using Embervale.Core;
using Embervale.Magic;
using Godot;

namespace Embervale.Player;

/// <summary>
/// What the spell wheel's control has to answer to be driven by play input. The HUD's
/// <c>SpellWheel</c> implements it and registers with <see cref="SpellWheelInput"/>.
/// </summary>
public interface ISpellWheelView
{
    /// <summary>
    /// Shows the wheel for <paramref name="caster"/>. <paramref name="toggled"/> is true when it was
    /// opened by a press that stays open (presses in place of holds) rather than by a held button.
    /// Return false to refuse (nothing to show, the element is hidden); the press then steps to the
    /// next spell as it always did.
    /// </summary>
    bool OpenWheel(SpellcastingComponent caster, bool toggled);

    /// <summary>Mouse travel in pixels while the wheel is open (<c>InputEventMouseMotion.Relative</c>).</summary>
    void MoveCursor(Vector2 mouseDelta);

    /// <summary>The right stick, every physics tick while the wheel is open. Zero when it is at rest.</summary>
    void SetStick(Vector2 stick);

    /// <summary>Hides the wheel. With <paramref name="confirm"/> it selects what the cursor is over
    /// (nothing, inside the dead zone); without, it selects nothing.</summary>
    void CloseWheel(bool confirm);
}

/// <summary>
/// The seam between play input and the spell wheel. <see cref="PlayerInputRouter"/> opens and closes
/// the wheel and lends it the right stick, <see cref="PlayerLookInput"/> lends it the mouse, and the
/// wheel's control receives all of it through <see cref="ISpellWheelView"/>. It is the one writer of
/// <see cref="PlayGate.WheelOpen"/>.
///
/// <para>Anything else that has to close the wheel (a stagger, a death, leaving play) calls
/// <see cref="Cancel"/>. A menu opening clears the gate itself; the router notices on its next tick
/// and cancels here.</para>
///
/// <para>The view is a session node held in a static, so it unregisters itself as it leaves the
/// tree; a freed one is dropped rather than called.</para>
/// </summary>
public static class SpellWheelInput
{
    private static ISpellWheelView? _view;
    private static bool _open;

    /// <summary>True while a wheel control is registered.</summary>
    public static bool HasView => View() != null;

    /// <summary>True while the wheel is open and still holds the gate.</summary>
    public static bool IsOpen => _open && PlayGate.WheelOpen;

    /// <summary>Makes <paramref name="view"/> the wheel. A wheel left open by an earlier view is cancelled.</summary>
    public static void Register(ISpellWheelView view)
    {
        if (!ReferenceEquals(_view, view))
        {
            Cancel();
            _view = view;
        }
    }

    /// <summary>Drops <paramref name="view"/> if it is the registered one, closing the gate with it.</summary>
    public static void Unregister(ISpellWheelView view)
    {
        if (ReferenceEquals(_view, view))
        {
            _view = null;
            _open = false;
            PlayGate.WheelOpen = false;
        }
    }

    /// <summary>Opens the wheel for <paramref name="caster"/>. False when there is no wheel or it
    /// refused, and then nothing is gated.</summary>
    public static bool Open(SpellcastingComponent caster, bool toggled)
    {
        if (_open)
        {
            Cancel();
        }

        if (View() is not { } view || !view.OpenWheel(caster, toggled))
        {
            return false;
        }

        _open = true;
        PlayGate.WheelOpen = true;
        return true;
    }

    /// <summary>Closes the wheel, selecting what the cursor is over when <paramref name="confirm"/> is
    /// set. Does nothing when it is not open.</summary>
    public static void Close(bool confirm)
    {
        if (!_open)
        {
            return;
        }

        // A gate something else already cleared (a menu opened) is never a confirm.
        bool select = confirm && PlayGate.WheelOpen;
        _open = false;
        PlayGate.WheelOpen = false;
        View()?.CloseWheel(select);
    }

    /// <summary>Closes the wheel without selecting.</summary>
    public static void Cancel() => Close(confirm: false);

    /// <summary>Mouse motion from the look input. True when the wheel took it, so the camera must not.</summary>
    public static bool Motion(Vector2 relative)
    {
        if (!IsOpen || View() is not { } view)
        {
            return false;
        }

        view.MoveCursor(relative);
        return true;
    }

    /// <summary>The right stick this tick. Ignored while the wheel is closed.</summary>
    public static void Stick(Vector2 stick)
    {
        if (IsOpen)
        {
            View()?.SetStick(stick);
        }
    }

    private static ISpellWheelView? View()
    {
        if (_view is GodotObject node && !GodotObject.IsInstanceValid(node))
        {
            _view = null;
            _open = false;
        }

        return _view;
    }
}
