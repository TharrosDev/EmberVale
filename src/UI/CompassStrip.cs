using Embervale.Core.Services;
using Embervale.Entities;
using Embervale.Localization;
using Embervale.Player;
using Embervale.Quests;
using Embervale.World;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The Phase 25F HUD compass: a top-of-screen strip that scrolls cardinal headings with the player's
/// facing and plots nearby discovered POIs (from <see cref="MapService"/>) plus the active quest
/// objective (resolved to a live world target by <see cref="ObjectiveLocator"/>). One self-drawn
/// <see cref="Control"/> — ticks, letters and markers are painted in <see cref="_Draw"/> rather than
/// built as a node tree. The heading/strip arithmetic is the pure, unit-tested <see cref="CompassMath"/>.
/// </summary>
public sealed partial class CompassStrip : Control
{
    private const float Fov = Mathf.Pi / 2f; // ±90° visible to either side of straight ahead
    private const float StripHeight = 26f;
    private const float ObjectiveResolveInterval = 0.4f; // re-find the objective target this often

    private static readonly (string Key, float Angle)[] Cardinals =
    {
        ("hud.compass.n", 0f),
        ("hud.compass.ne", Mathf.Pi / 4f),
        ("hud.compass.e", Mathf.Pi / 2f),
        ("hud.compass.se", 3f * Mathf.Pi / 4f),
        ("hud.compass.s", Mathf.Pi),
        ("hud.compass.sw", 5f * Mathf.Pi / 4f),
        ("hud.compass.w", 3f * Mathf.Pi / 2f),
        ("hud.compass.nw", 7f * Mathf.Pi / 4f),
    };

    private IEntity? _player;

    // ponytail: the objective target is re-resolved on a timer and cached, not searched every frame.
    private Vector3? _objectiveTarget;
    private float _resolveTimer;

    public void SetPlayer(IEntity? player) => _player = player;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        CustomMinimumSize = new Vector2(360f, StripHeight);
        Size = CustomMinimumSize;
    }

    public override void _Process(double delta)
    {
        _resolveTimer -= (float)delta;
        if (_resolveTimer <= 0f)
        {
            _resolveTimer = ObjectiveResolveInterval;
            _objectiveTarget = ResolveObjectiveTarget();
        }

        QueueRedraw(); // heading changes every frame; one widget, cheap to repaint
    }

    public override void _Draw()
    {
        float halfWidth = Size.X / 2f;
        float centreX = halfWidth;

        // Backdrop + the fixed centre tick (the player's current heading).
        DrawRect(new Rect2(0f, 0f, Size.X, StripHeight), UiTheme.PanelBg);
        DrawLine(new Vector2(centreX, 2f), new Vector2(centreX, StripHeight - 2f), UiTheme.Accent, 2f);

        if (_player?.Body is not { } body || !IsInstanceValid(body))
        {
            return;
        }

        float heading = HeadingOf(body);
        Vector3 origin = body.GlobalPosition;
        Font font = GetThemeDefaultFont();

        // Cardinal letters.
        foreach ((string key, float angle) in Cardinals)
        {
            float rel = CompassMath.Relative(angle, heading);
            if (!CompassMath.InView(rel, Fov))
            {
                continue;
            }

            float x = centreX + CompassMath.StripOffset(rel, Fov, halfWidth);
            Color colour = Mathf.IsZeroApprox(angle) ? UiTheme.Accent : UiTheme.Dim;
            DrawLabel(font, Loc.T(key), x, colour);
        }

        // Discovered POIs (small dim ticks).
        if (ServiceLocator.Instance is { } locator && locator.TryGet(out MapService map))
        {
            foreach (MapMarker poi in map.PoiMarkers())
            {
                if (TryStripX(poi.X, poi.Z, origin, heading, halfWidth, centreX, out float px))
                {
                    DrawLine(new Vector2(px, StripHeight - 9f), new Vector2(px, StripHeight - 2f), UiTheme.Dim, 2f);
                }
            }
        }

        // Active objective (a bright downward marker).
        if (_objectiveTarget is { } target &&
            TryStripX(target.X, target.Z, origin, heading, halfWidth, centreX, out float ox))
        {
            DrawColoredPolygon(
                new[]
                {
                    new Vector2(ox - 6f, 2f),
                    new Vector2(ox + 6f, 2f),
                    new Vector2(ox, 12f),
                },
                UiTheme.Good);
        }
    }

    /// <summary>The player's compass heading from its facing (forward = -Z).</summary>
    private static float HeadingOf(Node3D body)
    {
        Vector3 forward = -body.GlobalBasis.Z;
        return CompassMath.HeadingFromForward(forward.X, forward.Z);
    }

    /// <summary>Projects a world X/Z onto the strip; false when it falls outside the ±FOV window.</summary>
    private static bool TryStripX(float worldX, float worldZ, Vector3 origin, float heading,
        float halfWidth, float centreX, out float stripX)
    {
        float bearing = CompassMath.BearingTo(worldX - origin.X, worldZ - origin.Z);
        float rel = CompassMath.Relative(bearing, heading);
        if (!CompassMath.InView(rel, Fov))
        {
            stripX = 0f;
            return false;
        }

        stripX = centreX + CompassMath.StripOffset(rel, Fov, halfWidth);
        return true;
    }

    private void DrawLabel(Font font, string text, float x, Color colour)
    {
        Vector2 size = font.GetStringSize(text, HorizontalAlignment.Left, -1f, UiTheme.BodyFontSize);
        var pos = new Vector2(x - (size.X / 2f), (StripHeight + size.Y) / 2f - 2f);
        DrawString(font, pos, text, HorizontalAlignment.Left, -1f, UiTheme.BodyFontSize, colour);
    }

    /// <summary>First active quest → its first incomplete objective → its nearest live world target.</summary>
    private Vector3? ResolveObjectiveTarget()
    {
        if (_player is not { } player || player.Body is not { } body || !IsInstanceValid(body) ||
            player.GetComponent<QuestLogComponent>() is not { } log)
        {
            return null;
        }

        foreach (QuestProgress progress in log.Quests)
        {
            if (progress.Status != QuestStatus.Active)
            {
                continue;
            }

            var objectives = progress.Quest.ObjectiveList();
            for (int i = 0; i < objectives.Count; i++)
            {
                if (!progress.IsObjectiveComplete(i))
                {
                    return ObjectiveLocator.Locate(objectives[i], GetTree(), body.GlobalPosition);
                }
            }

            return null; // active quest with all objectives met (awaiting turn-in)
        }

        return null;
    }
}
