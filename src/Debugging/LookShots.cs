using Embervale.Appearance;
using Embervale.Core.Services;
using Embervale.Corruption;
using Embervale.Player;
using Embervale.Races;
using Godot;

namespace Embervale.Debugging;

/// <summary>
/// In-world proof of the character look (P8): <c>godot --path . -- --look-shots</c> continues the most recent
/// save and photographs the player's real body at eye level with a chosen look, uncorrupted and then at two
/// corruption tiers, so the tint, the build and the corruption ash/ember glow (which must still show on the
/// masked body shader) can be looked at. It leaves the loaded save untouched on disk.
/// </summary>
public sealed partial class LookShots : ShotHarness
{
    private static readonly string[] HumanLook =
    {
        "appearance.skin.tan", "appearance.hair.blonde", "appearance.eyes.blue", "appearance.build.broad",
    };

    private static readonly string[] UmbralLook =
    {
        "appearance.skin.ashen", "appearance.hair.midnight", "appearance.eyes.violet", "appearance.ember.violet", "appearance.build.slim",
    };

    private static Camera3D? _camera;

    protected override string Flag => "--look-shots";

    protected override string OutputDir => "user://look_shots";

    protected override void BuildShotList()
    {
        Shot("00-default-tier0", () => Pose("race.human", System.Array.Empty<string>(), corruption: 0));
        Shot("01-human-tier0", () => Pose("race.human", HumanLook, corruption: 0));
        Shot("02-umbral-tier0", () => Pose("race.umbral", UmbralLook, corruption: 0));
        Shot("03-umbral-marked", () => Pose("race.umbral", UmbralLook, corruption: 50));
        Shot("04-umbral-embers", () => Pose("race.umbral", UmbralLook, corruption: 90));
        Shot("05-human-deep-pale-hair", () => Pose("race.human", new[] { "appearance.skin.deep", "appearance.hair.white", "appearance.eyes.green" }, corruption: 0));
        Shot("06-human-pale-ginger", () => Pose("race.human", new[] { "appearance.skin.pale", "appearance.hair.ginger", "appearance.eyes.amber" }, corruption: 0));
    }

    private static void Pose(string raceId, string[] ids, int corruption)
    {
        if (ServiceLocator.Instance is not { } locator ||
            !locator.TryGet(out PlayerCharacter player) ||
            player.GetNodeOrNull<Node3D>("BodyMesh") is not { } body ||
            player.GetParent() == null)
        {
            return;
        }

        PlayerAppearance.Apply(body, PlayerAppearance.Resolve(RaceDatabase.Get(raceId), ids));
        player.GetComponent<CorruptionComponent>()?.Set(corruption);

        // The glb faces its local +Z and the movement code turns the body, so stand in front of the body, not of
        // the player node. The player's own camera is steered by its rig every frame,
        // so photograph from a camera of the harness's own, in front of the body at head height.
        _camera ??= new Camera3D { Fov = 40f };
        if (_camera.GetParent() == null)
        {
            player.GetParent().AddChild(_camera);
        }

        Vector3 forward = body.GlobalTransform.Basis.Z;
        Vector3 focus = player.GlobalPosition + new Vector3(0f, 1.45f, 0f);
        _camera.GlobalPosition = focus + forward * 1.6f + new Vector3(0f, 0.1f, 0f);
        _camera.LookAt(focus, Vector3.Up);
        _camera.Current = true;
    }
}
