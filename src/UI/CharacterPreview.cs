using Embervale.Appearance;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The creator's live preview: the player's own body model in a little world of its own, drawn into a
/// <see cref="SubViewport"/>. It goes through <see cref="PlayerAppearance.Apply"/>, the same call
/// <c>PlayerFactory.Create</c> makes, so what the creator shows is what the world will draw.
/// </summary>
public partial class CharacterPreview : SubViewportContainer
{
    private const string ModelPath = "res://assets/models/characters/chr_player_base.glb";

    // The model stands 1.65 m tall; the camera frames it from the thighs up, turned a little, so the face,
    // hair and one shoulder (the parts the swatches change) read at the panel's small size.
    private const float ModelYawDegrees = 22f;
    private static readonly Vector3 CameraPosition = new(0f, 1.25f, 2.5f);
    private static readonly Vector3 CameraTarget = new(0f, 1.1f, 0f);

    private Node3D? _model;

    public CharacterPreview()
    {
        Stretch = true;
        CustomMinimumSize = new Vector2(240f, 340f);
        MouseFilter = MouseFilterEnum.Ignore;

        var viewport = new SubViewport
        {
            OwnWorld3D = true,
            TransparentBg = false,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            Msaa3D = Viewport.Msaa.Msaa4X,
        };
        AddChild(viewport);

        var sky = new Sky { SkyMaterial = new ProceduralSkyMaterial() };
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = UiTheme.WellBg,
            Sky = sky,
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            ReflectedLightSource = Godot.Environment.ReflectionSource.Sky,
            AmbientLightSkyContribution = 1f,
            AmbientLightEnergy = 2.2f,
        };
        viewport.AddChild(new WorldEnvironment { Environment = environment });

        var sun = new DirectionalLight3D { LightEnergy = 1.3f, RotationDegrees = new Vector3(-35f, 30f, 0f) };
        viewport.AddChild(sun);
        var fill = new DirectionalLight3D { LightEnergy = 0.45f, RotationDegrees = new Vector3(-10f, -140f, 0f) };
        viewport.AddChild(fill);

        var camera = new Camera3D { Fov = 34f, Current = true };
        viewport.AddChild(camera);
        camera.Position = CameraPosition;
        camera.LookAt(CameraTarget, Vector3.Up);

        if (GD.Load<PackedScene>(ModelPath)?.Instantiate() is Node3D model)
        {
            model.RotationDegrees = new Vector3(0f, ModelYawDegrees, 0f);
            viewport.AddChild(model);
            _model = model;
        }
    }

    /// <summary>Redraws the model with <paramref name="picks"/> (one option per slot, as
    /// <see cref="PlayerAppearance.Resolve(Embervale.Races.RaceResource?, System.Collections.Generic.IEnumerable{string}?)"/> returns).</summary>
    public void SetLook(System.Collections.Generic.IReadOnlyList<AppearanceOptionResource?> picks)
    {
        if (_model != null)
        {
            PlayerAppearance.Apply(_model, picks);
        }
    }
}
