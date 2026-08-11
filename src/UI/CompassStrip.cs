using System.Collections.Generic;
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

    // ⚠️ The discovered places are cached against MapService.Revision, NOT re-enumerated per frame.
    // This widget calls QueueRedraw every frame by design (the heading moves constantly), and
    // DiscoveredLocations() walks every discovered id through a database lookup — 63 of them at
    // 60 fps, to draw ticks that only change when the player discovers something. Caching on the
    // revision counter the service already maintains costs one int comparison.
    private readonly List<Vector2> _places = new();
    private int _placesRevision = -1;

    public void SetPlayer(IEntity? player) => _player = player;

    /// <summary>The tracked objective's world position, or null when there is nothing to walk toward.
    ///
    /// Exposed so the quest tracker can print the distance and bearing to the same point this strip
    /// is drawing a marker at (39.5B). Resolving it twice would mean two <see cref="ObjectiveLocator"/>
    /// scene walks per interval and — worse — two answers, which is how a tracker saying "320 m NW"
    /// ends up beside a compass marker pointing east.</summary>
    public Vector3? ObjectiveTarget => _objectiveTarget;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        CustomMinimumSize = new Vector2(320f, StripHeight);
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

        // Discovered places (small dim ticks). 39.5A: the map's own locations, not just cell centres —
        // a settlement tick is now the settlement rather than the middle of the tile it sits in, and
        // every shop and service the player has found is on the strip too.
        MapService? map = null;
        if (ServiceLocator.Instance is { } locator && locator.TryGet(out MapService resolved))
        {
            map = resolved;
            RefreshPlaces(resolved);

            foreach (Vector2 place in _places)
            {
                if (TryStripX(place.X, place.Y, origin, heading, halfWidth, centreX, out float px))
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

        // The player's own waypoint (39.5A), drawn last so it wins every overlap.
        //
        // ⚠️ This is what makes a map mark navigable. The world beacon is visible until something
        // stands between the player and it; the compass bearing never is, so the two together mean
        // "walk that way" survives a building, a hill and a turn.
        if (map?.Waypoint is { } waypoint &&
            TryStripX(waypoint.X, waypoint.Z, origin, heading, halfWidth, centreX, out float wx))
        {
            var mark = UiTheme.Adapt(UiTheme.AccentHot);
            DrawColoredPolygon(
                new[]
                {
                    new Vector2(wx - 6f, StripHeight - 12f),
                    new Vector2(wx + 6f, StripHeight - 12f),
                    new Vector2(wx, StripHeight - 1f),
                },
                mark);
            DrawLine(new Vector2(wx, 2f), new Vector2(wx, StripHeight - 12f), new Color(mark, 0.55f), 1.5f);
        }
    }

    /// <summary>Re-caches the discovered places, but only when discovery has actually changed.</summary>
    private void RefreshPlaces(MapService map)
    {
        if (_placesRevision == map.Revision)
        {
            return;
        }

        _placesRevision = map.Revision;
        _places.Clear();
        foreach (MapLocationView view in map.DiscoveredLocations())
        {
            _places.Add(new Vector2(view.Position.X, view.Position.Z));
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

    /// <summary>Tracked quest → its first incomplete objective → its nearest live world target.
    ///
    /// 39.5B: the tracked quest comes from <see cref="QuestLogComponent.Tracked"/> rather than from a
    /// first-active scan of its own. The two scans agreed only by accident of dictionary order.</summary>
    private Vector3? ResolveObjectiveTarget()
    {
        if (_player is not { } player || player.Body is not { } body || !IsInstanceValid(body) ||
            player.GetComponent<QuestLogComponent>()?.Tracked is not { } progress)
        {
            return null;
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
}
