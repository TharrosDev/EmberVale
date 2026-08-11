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

        // ⚠️ THE BACKDROP FADES AT BOTH ENDS RATHER THAN STOPPING (39.5B).
        //
        // It was one flat `DrawRect`, which gave the strip two hard vertical edges — so a heading
        // scrolling past them *popped* in and out, and the widget read as a grey box pasted on the
        // world rather than as a band of the horizon. The fade is what makes it read as a window onto
        // something continuous, and it costs eight quads.
        DrawBackdrop();

        // The centre mark: a downward wedge over a full-height hairline. A bare 2 px line was
        // ambiguous about which pixel it meant at the exact moment precision matters.
        var mark = UiTheme.Adapt(UiTheme.Accent);
        DrawLine(new Vector2(centreX, 3f), new Vector2(centreX, StripHeight - 3f), new Color(mark, 0.55f), 1f);
        DrawColoredPolygon(
            new[]
            {
                new Vector2(centreX - 5f, 0f),
                new Vector2(centreX + 5f, 0f),
                new Vector2(centreX, 7f),
            },
            mark);

        if (_player?.Body is not { } body || !IsInstanceValid(body))
        {
            return;
        }

        float heading = HeadingOf(body);
        Vector3 origin = body.GlobalPosition;
        Font font = GetThemeDefaultFont();

        // Minor graduations every 15°, so the strip has something to scroll against between letters.
        // Without them a slow turn looks like nothing is happening until a cardinal drifts into view.
        for (int degrees = 0; degrees < 360; degrees += 15)
        {
            if (degrees % 45 == 0)
            {
                continue; // a lettered heading draws its own, taller tick
            }

            float rel = CompassMath.Relative(Mathf.DegToRad(degrees), heading);
            if (!CompassMath.InView(rel, Fov))
            {
                continue;
            }

            float x = centreX + CompassMath.StripOffset(rel, Fov, halfWidth);
            DrawLine(new Vector2(x, StripHeight - 6f), new Vector2(x, StripHeight - 2f),
                new Color(UiTheme.Dim, EdgeFade(x, halfWidth) * 0.5f), 1f);
        }

        // Cardinal letters. The four true cardinals carry more weight than the diagonals, so a glance
        // lands on N/E/S/W and the intercardinals fill in — a strip where all eight shout equally is
        // eight things to read instead of one.
        foreach ((string key, float angle) in Cardinals)
        {
            float rel = CompassMath.Relative(angle, heading);
            if (!CompassMath.InView(rel, Fov))
            {
                continue;
            }

            float x = centreX + CompassMath.StripOffset(rel, Fov, halfWidth);
            bool major = Mathf.IsZeroApprox(Mathf.PosMod(angle + 0.001f, Mathf.Pi / 2f) - 0.001f);
            bool north = Mathf.IsZeroApprox(angle);

            float fade = EdgeFade(x, halfWidth);
            Color colour = north ? UiTheme.Accent : major ? UiTheme.Text : UiTheme.Dim;
            DrawLine(new Vector2(x, StripHeight - (major ? 9f : 7f)), new Vector2(x, StripHeight - 2f),
                new Color(colour, fade * 0.7f), major ? 1.5f : 1f);
            DrawLabel(font, Loc.T(key), x, new Color(colour, fade));
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
            var waypointMark = UiTheme.Adapt(UiTheme.AccentHot);
            DrawColoredPolygon(
                new[]
                {
                    new Vector2(wx - 6f, StripHeight - 12f),
                    new Vector2(wx + 6f, StripHeight - 12f),
                    new Vector2(wx, StripHeight - 1f),
                },
                waypointMark);
            DrawLine(new Vector2(wx, 2f), new Vector2(wx, StripHeight - 12f),
                new Color(waypointMark, 0.55f), 1.5f);
        }
    }

    /// <summary>
    /// The strip's ground: opaque in the middle, transparent at both ends.
    ///
    /// Drawn as a handful of vertical bands rather than a gradient texture — the strip is 320 px
    /// wide and repaints every frame, so a dozen `DrawRect`s is cheaper than allocating and sampling
    /// an image, and it needs no asset.
    /// </summary>
    private void DrawBackdrop()
    {
        const int bands = 16;
        float bandWidth = Size.X / bands;
        float halfWidth = Size.X / 2f;

        for (int i = 0; i < bands; i++)
        {
            float x = i * bandWidth;
            float alpha = EdgeFade(x + (bandWidth * 0.5f), halfWidth);
            DrawRect(
                new Rect2(x, 0f, bandWidth + 1f, StripHeight),
                new Color(UiTheme.PanelBg, UiTheme.PanelBg.A * alpha));
        }

        // A hairline along the bottom, fading with everything else — it gives the band an edge to sit
        // on so the letters are not floating over the sky.
        DrawLine(new Vector2(0f, StripHeight - 0.5f), new Vector2(Size.X, StripHeight - 0.5f),
            new Color(UiTheme.Brass, 0.35f), 1f);
    }

    /// <summary>Opacity for a position across the strip: 1 through the middle, easing to 0 at the two
    /// ends. Shared by the backdrop, the ticks and the letters so they all vanish together.</summary>
    private static float EdgeFade(float x, float halfWidth)
    {
        const float fadeZone = 0.34f; // fraction of each half that fades
        float distance = Mathf.Abs(x - halfWidth) / halfWidth; // 0 centre .. 1 edge
        if (distance <= 1f - fadeZone)
        {
            return 1f;
        }

        return Mathf.Clamp((1f - distance) / fadeZone, 0f, 1f);
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
        int size = UiTheme.FontSize(UiTheme.CaptionFontSize);
        Vector2 measured = font.GetStringSize(text, HorizontalAlignment.Left, -1f, size);
        var pos = new Vector2(x - (measured.X / 2f), 15f);

        // Shadowed, like every other label drawn over the world (MapView.DrawLabel's lesson): the
        // strip is semi-transparent at the ends, so an unshadowed letter crossing a bright sky
        // disappears exactly where the fade makes it faintest.
        DrawString(font, pos + Vector2.One, text, HorizontalAlignment.Left, -1f, size,
            new Color(UiTheme.Engrave, colour.A));
        DrawString(font, pos, text, HorizontalAlignment.Left, -1f, size, colour);
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
