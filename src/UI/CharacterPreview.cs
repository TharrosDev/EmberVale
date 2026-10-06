using Embervale.Appearance;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The creator's live preview: the player's own body model in a little world of its own, drawn into a
/// <see cref="SubViewport"/>. It goes through <see cref="PlayerAppearance.Apply"/>, the same call
/// <c>PlayerFactory.Create</c> makes, so what the creator shows is what the world will draw.
///
/// A turntable: dragging across it (or <see cref="Turn"/>, which the creator feeds from the right
/// stick) turns the figure, the camera frames the whole body or the face
/// (<see cref="SetFraming"/>), and one of three light rigs is on at a time (<see cref="SetRig"/>).
///
/// The viewport is drawn only when something in it changed. It used to redraw every frame for a
/// figure that stands still; now a change asks for one frame, and it runs continuously only while
/// the camera is easing between framings or the figure is being turned. The node sleeps otherwise
/// and every path that changes the picture wakes it (<see cref="Redraw"/>).
/// </summary>
public partial class CharacterPreview : SubViewportContainer
{
    private const string ModelPath = "res://assets/models/characters/chr_player_base.glb";

    private const float CameraFov = 34f;

    /// <summary>What a light rig is: a key and a fill, each an energy, a direction and how far it
    /// is warmed or cooled from white, and the ambient level under them.</summary>
    private readonly record struct Rig(
        float KeyEnergy, Vector3 KeyRotation, Color KeyColor,
        float FillEnergy, Vector3 FillRotation, Color FillColor,
        float Ambient);

    // The model stands 1.65 m tall. The body framing holds it head to foot; the face framing is
    // head and shoulders, for the hair and eye swatches.
    private static readonly Vector3 BodyCamera = new(0f, 0.95f, 4.1f);
    private static readonly Vector3 BodyTarget = new(0f, 0.86f, 0f);
    private static readonly Vector3 FaceCamera = new(0f, 1.52f, 1.15f);
    private static readonly Vector3 FaceTarget = new(0f, 1.5f, 0f);

    // Light is not palette: a colour here is a white lamp leaned toward one of the UI's own
    // tokens, so the rigs stay inside the world's temperature range without a literal of their own.
    private static readonly Color Warm = Colors.White.Lerp(UiTheme.AccentHot, 0.28f);
    private static readonly Color Cool = Colors.White.Lerp(UiTheme.GlyphLight, 0.45f);

    private static readonly Rig[] Rigs =
    {
        // Hearth: the look the creator has always had. A high warm key, a low fill from behind.
        new(1.3f, new Vector3(-35f, 30f, 0f), Colors.White, 0.45f, new Vector3(-10f, -140f, 0f), Colors.White, 2.2f),

        // Overcast: flat and cool from the front, to judge a skin tone without a shadow on it.
        new(0.9f, new Vector3(-55f, 0f, 0f), Cool, 0.6f, new Vector3(-20f, 170f, 0f), Cool, 2.8f),

        // Ember dusk: dark, with one low warm light from the side, which is how the ember glow reads.
        new(1.5f, new Vector3(-12f, 105f, 0f), Warm, 0.25f, new Vector3(-30f, -70f, 0f), Cool, 0.7f),
    };

    private readonly SubViewport _viewport;
    private readonly Camera3D _camera;
    private readonly DirectionalLight3D _key;
    private readonly DirectionalLight3D _fill;
    private readonly Godot.Environment _environment;
    private Node3D? _model;

    private float _yaw = CreatorRules.DefaultYaw;
    private int _rig;
    private PreviewFraming _framing = PreviewFraming.Body;

    // 0 is the body framing and 1 the face; the camera eases between them.
    private float _blend;
    private float _blendFrom;
    private float _blendElapsed;
    private float _blendSeconds;
    private bool _turning;
    private bool _dragging;

    public CharacterPreview()
    {
        Stretch = true;
        CustomMinimumSize = new Vector2(UiTheme.CreatorPreviewNarrowWidth, UiTheme.CreatorBodyMinHeight);
        MouseFilter = MouseFilterEnum.Stop; // a drag turns the figure
        MouseDefaultCursorShape = CursorShape.Drag;

        _viewport = new SubViewport
        {
            OwnWorld3D = true,
            TransparentBg = false,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Once,
            Msaa3D = Viewport.Msaa.Msaa4X,
        };
        AddChild(_viewport);

        var sky = new Sky { SkyMaterial = new ProceduralSkyMaterial() };
        _environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = UiTheme.WellBg,
            Sky = sky,
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            ReflectedLightSource = Godot.Environment.ReflectionSource.Sky,
            AmbientLightSkyContribution = 1f,
        };
        _viewport.AddChild(new WorldEnvironment { Environment = _environment });

        _key = new DirectionalLight3D();
        _viewport.AddChild(_key);
        _fill = new DirectionalLight3D();
        _viewport.AddChild(_fill);

        _camera = new Camera3D { Fov = CameraFov, Current = true };
        _viewport.AddChild(_camera);

        if (GD.Load<PackedScene>(ModelPath)?.Instantiate() is Node3D model)
        {
            model.RotationDegrees = new Vector3(0f, _yaw, 0f);
            _viewport.AddChild(model);
            _model = model;
        }

        ApplyRig();
        PlaceCamera();
        Resized += Redraw; // the viewport follows the container's size
    }

    public override void _Ready()
    {
        SetProcess(false);
        Redraw();
    }

    /// <summary>The figure's yaw, in degrees.</summary>
    public float Yaw => _yaw;

    /// <summary>Which light rig is on, 0 to <see cref="CreatorRules.LightRigCount"/> - 1.</summary>
    public int RigIndex => _rig;

    /// <summary>What the camera is framing (or easing toward).</summary>
    public PreviewFraming Framing => _framing;

    /// <summary>Whether the model loaded. Read by the screenshot harness.</summary>
    public bool HasModel => _model != null;

    /// <summary>Redraws the model with <paramref name="picks"/> (one option per slot, as
    /// <see cref="PlayerAppearance.Resolve(Embervale.Races.RaceResource?, System.Collections.Generic.IEnumerable{string}?)"/> returns).</summary>
    public void SetLook(System.Collections.Generic.IReadOnlyList<AppearanceOptionResource?> picks)
    {
        if (_model != null)
        {
            PlayerAppearance.Apply(_model, picks);
            Redraw();
        }
    }

    /// <summary>Turns the figure by <paramref name="degrees"/>. Fed each frame the stick is held
    /// over, so the viewport runs for as long as it keeps being called.</summary>
    public void Turn(float degrees)
    {
        if (Mathf.IsZeroApprox(degrees))
        {
            return;
        }

        SetYaw(_yaw + degrees);
        _turning = true;
        Wake();
    }

    /// <summary>Stands the figure at <paramref name="degrees"/>.</summary>
    public void SetYaw(float degrees)
    {
        _yaw = CreatorRules.WrapYaw(degrees);
        if (_model != null)
        {
            _model.RotationDegrees = new Vector3(0f, _yaw, 0f);
        }

        Redraw();
    }

    /// <summary>Switches to light rig <paramref name="index"/> (wrapped into range).</summary>
    public void SetRig(int index)
    {
        _rig = ((index % Rigs.Length) + Rigs.Length) % Rigs.Length;
        ApplyRig();
        Redraw();
    }

    /// <summary>Eases the camera to <paramref name="framing"/>; at once under reduced motion.</summary>
    public void SetFraming(PreviewFraming framing)
    {
        if (framing == _framing)
        {
            return;
        }

        _framing = framing;
        _blendFrom = _blend;
        _blendElapsed = 0f;
        _blendSeconds = UiTheme.Duration(UiTheme.DurationSlow);
        if (_blendSeconds <= 0f || !IsInsideTree())
        {
            _blend = Target;
            PlaceCamera();
            Redraw();
            return;
        }

        Wake();
    }

    private float Target => _framing == PreviewFraming.Face ? 1f : 0f;

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left } button)
        {
            _dragging = button.Pressed;
            AcceptEvent();
        }
        else if (_dragging && @event is InputEventMouseMotion motion)
        {
            SetYaw(_yaw + (motion.Relative.X * CreatorRules.TurnDegreesPerPixel));
            AcceptEvent();
        }
    }

    public override void _Process(double delta)
    {
        bool easing = !Mathf.IsEqualApprox(_blend, Target);
        if (easing)
        {
            _blendElapsed += (float)delta;
            float t = UiMotion.EaseOut(UiMotion.Progress(_blendElapsed, _blendSeconds));
            _blend = t >= 1f ? Target : Mathf.Lerp(_blendFrom, Target, t);
            PlaceCamera();
        }

        // One more frame after the last change, then back to drawing on request.
        if (easing || _turning)
        {
            _turning = false;
            _viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
            return;
        }

        _viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
        SetProcess(false);
    }

    /// <summary>Asks for one frame of the viewport. Every change to the picture ends here.</summary>
    private void Redraw()
    {
        if (_viewport.RenderTargetUpdateMode != SubViewport.UpdateMode.Always)
        {
            _viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
        }
    }

    /// <summary>Runs the viewport until the easing or turning that woke it stops.</summary>
    private void Wake()
    {
        _viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
        SetProcess(true);
    }

    private void PlaceCamera()
    {
        // Built as a transform rather than with LookAt, which needs the camera in the scene tree:
        // this runs from the constructor too.
        Vector3 position = BodyCamera.Lerp(FaceCamera, _blend);
        Vector3 target = BodyTarget.Lerp(FaceTarget, _blend);
        _camera.Transform = new Transform3D(Basis.LookingAt(target - position, Vector3.Up), position);
    }

    private void ApplyRig()
    {
        Rig rig = Rigs[_rig];
        _key.LightEnergy = rig.KeyEnergy;
        _key.RotationDegrees = rig.KeyRotation;
        _key.LightColor = rig.KeyColor;
        _fill.LightEnergy = rig.FillEnergy;
        _fill.RotationDegrees = rig.FillRotation;
        _fill.LightColor = rig.FillColor;
        _environment.AmbientLightEnergy = rig.Ambient;
    }
}
