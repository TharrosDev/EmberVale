using Embervale.Combat;
using Embervale.Core;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Entities;
using Embervale.Settings;
using Godot;

namespace Embervale.Player;

/// <summary>
/// Turning the camera, and nothing else. Mouse motion is event-driven; the right stick is a held
/// deflection and so is polled and integrated against frame time. Both write the same yaw (on the
/// body) and pitch (on the camera rig), including the lock-on rule that only pitch is player-driven
/// while a target is held.
///
/// <para>It also owns mouse capture, because mouse mode is what "look" means at the OS level.</para>
/// </summary>
[GlobalClass]
public partial class PlayerLookInput : EntityComponent
{
    /// <summary>Base radians-per-pixel look sensitivity; the player's settings multiplier scales
    /// this at runtime.</summary>
    [Export]
    public float MouseSensitivity { get; set; } = 0.0028f;

    /// <summary>Stick-look rate at full deflection (radians/second), before the sensitivity setting.
    /// A stick is a held deflection rather than a delta, so it turns at a rate where the mouse turns
    /// by distance — see <see cref="SettingsMath.StickLookStep"/>.</summary>
    private const float StickLookRate = 2.6f;

    /// <summary>Matches the deadzone <see cref="GameInput"/> sets on the look actions; the raw axis
    /// is read here (not through an action) so the response curve can shape the whole travel.</summary>
    private const float StickDeadzone = 0.15f;

    private Node3D _yaw = null!;
    private PlayerCameraRig? _rig;
    private LockOnComponent? _lockOn;
    private SettingsService? _settings;

    protected override void OnInitialize()
    {
        _yaw = Entity!.Body;
        _rig = Entity.GetComponent<PlayerCameraRig>();
        _lockOn = Entity.GetComponent<LockOnComponent>();
        _settings = ServiceLocator.Instance is { } locator && locator.TryGet(out SettingsService settings)
            ? settings
            : null;

        EventBus.Instance?.Subscribe<GameStateChangedEvent>(OnGameStateChanged);
        CaptureMouse(true);
    }

    protected override void OnTeardown()
    {
        EventBus.Instance?.Unsubscribe<GameStateChangedEvent>(OnGameStateChanged);
    }

    /// <summary>Right-stick look, polled by the input router in the same frame order the mouse-driven
    /// path used to run in.</summary>
    public void TickStickLook(double delta)
    {
        Vector2 look = Godot.Input.GetVector(
            GameInput.LookLeft, GameInput.LookRight, GameInput.LookUp, GameInput.LookDown);
        if (look == Vector2.Zero)
        {
            return;
        }

        float multiplier = _settings?.Current.MouseSensitivity ?? 1f;
        bool invertY = _settings?.Current.InvertY ?? false;
        float dt = (float)delta;

        if (_lockOn?.Target == null)
        {
            float yawStep = SettingsMath.StickLookStep(look.X, StickDeadzone, StickLookRate, dt, multiplier);
            if (yawStep != 0f)
            {
                _yaw.RotateY(-yawStep);
            }
        }

        float pitchStep = SettingsMath.StickLookStep(look.Y, StickDeadzone, StickLookRate, dt, multiplier);
        if (pitchStep != 0f)
        {
            _rig?.ApplyPitchStep(pitchStep, invertY);
        }
    }

    public override void _Input(InputEvent @event)
    {
        // ⚠️ A CINEMATIC LOCK HAS TO STOP MOUSE-LOOK HERE, NOT IN THE INPUT ROUTER. Movement, guard,
        // attacks, casting, interaction and dodge are all suspended by the router's UiState.MenuOpen
        // branch — but mouse-look is event-driven and this method never asked. A cinematic lock (boss
        // intro, prologue) leaves GameState.Playing and the mouse captured, so the one input the
        // player was supposedly not holding was the one that still worked: they could spin the
        // camera, and the yaw node they were spinning is the body's.
        if (@event is not InputEventMouseMotion motion ||
            Godot.Input.MouseMode != Godot.Input.MouseModeEnum.Captured ||
            GameManager.Instance is { IsPlaying: false } ||
            UiState.MenuOpen)
        {
            return;
        }

        float multiplier = _settings?.Current.MouseSensitivity ?? 1f;
        bool invertY = _settings?.Current.InvertY ?? false;

        // While locked on, the body auto-faces the target — mouse only pitches.
        if (_lockOn?.Target == null)
        {
            _yaw.RotateY(-SettingsMath.LookStep(motion.Relative.X, MouseSensitivity, multiplier));
        }

        _rig?.ApplyPitchStep(
            SettingsMath.LookStep(motion.Relative.Y, MouseSensitivity, multiplier), invertY);
    }

    private void OnGameStateChanged(GameStateChangedEvent e) => CaptureMouse(e.Current == GameState.Playing);

    private static void CaptureMouse(bool captured)
    {
        Godot.Input.MouseMode = captured
            ? Godot.Input.MouseModeEnum.Captured
            : Godot.Input.MouseModeEnum.Visible;
    }
}
