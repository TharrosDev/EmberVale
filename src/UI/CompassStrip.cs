using Embervale.Core.Services;
using Embervale.Entities;
using Embervale.Localization;
using Embervale.Quests;
using Embervale.World;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The HUD compass — rebuilt from nothing in 39.5C.
///
/// ⚠️ <b>THE OLD ONE WAS NINE OVERLAPPING DRAW PASSES IN A 320×26 BOX, AND IT LOOKED LIKE IT.</b>
/// A filled panel, a sixteen-band fade over it, 15° graduations, a tick under every cardinal, the
/// cardinal letters, a tick for every discovered place, an objective chevron, a waypoint chevron and
/// a centre wedge — all competing inside twenty-six vertical pixels. Each addition was individually
/// reasonable and the sum was unreadable: at any heading the widget was a row of twenty-odd
/// near-identical hairlines, which is a **barcode**, not a compass. 39.5C first tried to fix it by
/// separating the channels into rows; that treated the symptom. The real problem was that there were
/// too many channels.
///
/// So this is a rewrite, and the design is mostly subtraction:
///
/// <list type="bullet">
/// <item><b>No panel and no fade.</b> The strip is a single hairline rule with letters above it.
/// Removing the fill removed the box, the banding and the stepping artefact together — there is
/// nothing left to look stepped.</item>
/// <item><b>No graduations.</b> They existed to give a slow turn something to move against; the
/// letters and the destination marks already do that, and the graduations were most of the
/// barcode.</item>
/// <item><b>No discovered-place ticks.</b> This is the real cut. 39.5A put every found shop on the
/// strip because nothing else showed them — but 39.5B added the <see cref="MinimapHud"/>, which
/// answers "what is near me" far better than a one-dimensional strip ever could. Two surfaces
/// answering one question is how both end up cluttered. **The minimap owns nearby places; the
/// compass owns facing and destination.**</item>
/// </list>
///
/// What is left is four things, each in its own horizontal band: a centre mark, the eight headings,
/// the rule, and the destinations — with a distance for the one the player is actually following,
/// and an edge arrow when it is behind them.
///
/// The heading arithmetic remains the pure, unit-tested <see cref="CompassMath"/>.
/// </summary>
public sealed partial class CompassStrip : Control
{
    /// <summary>±90° visible either side of straight ahead.</summary>
    private const float Fov = Mathf.Pi / 2f;

    private const float Width = 460f;

    // The four bands, top to bottom. Everything drawn here lands in exactly one of them, which is
    // the whole rule that keeps the widget legible.
    private const float MarkTop = 0f;        // centre mark + destination chevrons: y 0..9
    private const float LetterBaseline = 26f; // cardinal letters sit above the rule
    private const float RuleY = 32f;          // the horizon
    private const float DistanceBaseline = 45f; // destination distance, hanging below

    private const float Height = 50f;

    private const float ObjectiveResolveInterval = 0.4f;

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

    /// <summary>The tracked objective's world position, or null when there is nothing to walk toward.
    ///
    /// Exposed so the quest tracker prints the distance and bearing to the same point this strip
    /// marks (39.5B). Resolving it twice would mean two <see cref="ObjectiveLocator"/> scene walks
    /// per interval and — worse — two answers, which is how a tracker saying "320 m NW" ends up
    /// beside a compass marker pointing east.</summary>
    public Vector3? ObjectiveTarget => _objectiveTarget;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        CustomMinimumSize = new Vector2(Width, Height);
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

        QueueRedraw(); // the heading moves every frame; one widget, cheap to repaint
    }

    public override void _Draw()
    {
        float halfWidth = Size.X / 2f;
        float centreX = halfWidth;

        DrawRule(halfWidth);
        DrawCentreMark(centreX);

        if (_player?.Body is not { } body || !IsInstanceValid(body))
        {
            return;
        }

        float heading = HeadingOf(body);
        Vector3 origin = body.GlobalPosition;
        Font font = GetThemeDefaultFont();

        DrawCardinals(font, heading, halfWidth, centreX);

        // Destinations last so nothing draws over them. The objective is the game's mark; the
        // waypoint is the player's own and wins ties by being drawn after it.
        Vector3? waypoint = ServiceLocator.Instance is { } locator && locator.TryGet(out MapService map)
            ? map.Waypoint
            : null;

        if (_objectiveTarget is { } target)
        {
            DrawDestination(font, target, origin, heading, halfWidth, centreX, UiTheme.Good,
                playerWaypoint: false, showDistance: waypoint == null);
        }

        if (waypoint is { } playerMark)
        {
            DrawDestination(font, playerMark, origin, heading, halfWidth, centreX, UiTheme.AccentHot,
                playerWaypoint: true, showDistance: true);
        }
    }

    /// <summary>The horizon: one hairline, fading to nothing at both ends so the strip has no edges
    /// to pop against. Drawn in segments because a single line cannot carry a gradient.</summary>
    private void DrawRule(float halfWidth)
    {
        int segments = Mathf.Max(Mathf.RoundToInt(Size.X / 4f), 8);
        float step = Size.X / segments;

        for (int i = 0; i < segments; i++)
        {
            float x = i * step;
            float alpha = EdgeFade(x + (step * 0.5f), halfWidth);
            DrawRect(new Rect2(x, RuleY, step + 1f, 1f), new Color(UiTheme.Brass, 0.55f * alpha));
        }
    }

    /// <summary>Where the player is facing: one small wedge on the rule, pointing up into the
    /// letters. The only fixed element on the strip.</summary>
    private void DrawCentreMark(float centreX)
    {
        var mark = UiTheme.Adapt(UiTheme.Accent);
        DrawColoredPolygon(
            new[]
            {
                new Vector2(centreX - 5f, RuleY + 6f),
                new Vector2(centreX + 5f, RuleY + 6f),
                new Vector2(centreX, RuleY - 1f),
            },
            mark);
    }

    /// <summary>The eight headings. N is ember and the true cardinals are bone at body size; the
    /// intercardinals are dim and smaller — so a glance lands on one letter, not eight.</summary>
    private void DrawCardinals(Font font, float heading, float halfWidth, float centreX)
    {
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

            Color colour = north ? UiTheme.Adapt(UiTheme.Accent) : major ? UiTheme.Text : UiTheme.Dim;
            DrawLabel(font, Loc.T(key), x, new Color(colour, EdgeFade(x, halfWidth)),
                major ? UiTheme.BodyFontSize : UiTheme.CaptionFontSize, LetterBaseline);
        }
    }

    /// <summary>
    /// A destination: a chevron above the rule, its distance below, and — when it is behind the
    /// player — an arrow pinned to the edge pointing the shorter way round.
    ///
    /// ⚠️ <b>The edge arrow is the functional half and its absence was a real gap.</b> Outside the
    /// ±90° window the old strip drew nothing at all, so a player facing away from their own
    /// waypoint saw an empty compass and had to spin on the spot to find which way to turn. A marker
    /// that vanishes exactly when it is needed is worse than a coarse one that does not.
    /// </summary>
    private void DrawDestination(
        Font font, Vector3 target, Vector3 origin, float heading, float halfWidth, float centreX,
        Color tint, bool playerWaypoint, bool showDistance)
    {
        float dx = target.X - origin.X;
        float dz = target.Z - origin.Z;
        float rel = CompassMath.Relative(CompassMath.BearingTo(dx, dz), heading);
        Color mark = UiTheme.Adapt(tint);

        if (!CompassMath.InView(rel, Fov))
        {
            float edgeX = rel > 0f ? Size.X - 6f : 6f;
            float direction = rel > 0f ? 1f : -1f;
            Vector2[] arrow =
            {
                new(edgeX + (direction * 5f), MarkTop + 5f),
                new(edgeX - (direction * 4f), MarkTop),
                new(edgeX - (direction * 4f), MarkTop + 10f),
            };
            if (playerWaypoint)
            {
                DrawPolyline(new[] { arrow[1], arrow[0], arrow[2] }, new Color(mark, 0.9f), 2f);
            }
            else
            {
                DrawColoredPolygon(arrow, new Color(mark, 0.85f));
            }
            return;
        }

        float x = centreX + CompassMath.StripOffset(rel, Fov, halfWidth);
        float fade = EdgeFade(x, halfWidth);

        if (playerWaypoint)
        {
            Vector2[] diamond =
            {
                new(x, MarkTop),
                new(x + 6f, MarkTop + 5f),
                new(x, MarkTop + 10f),
                new(x - 6f, MarkTop + 5f),
                new(x, MarkTop),
            };
            DrawPolyline(diamond, new Color(mark, fade), 2f);
        }
        else
        {
            DrawColoredPolygon(
                new[]
                {
                    new Vector2(x - 6f, MarkTop),
                    new Vector2(x + 6f, MarkTop),
                    new Vector2(x, MarkTop + 9f),
                },
                new Color(mark, fade));
        }

        // §16: a distance for the destination that matters, and only that one. Printing it for every
        // marker is the "excessive text" the same section warns against.
        if (showDistance)
        {
            (string value, string unitKey) = CompassMath.Distance(Mathf.Sqrt((dx * dx) + (dz * dz)));
            DrawLabel(font, $"{value}{Loc.T(unitKey)}", x, new Color(mark, fade),
                UiTheme.CaptionFontSize, DistanceBaseline);
        }
    }

    /// <summary>Opacity across the strip: solid through the middle, easing to nothing at the ends, so
    /// a heading scrolling off the side dissolves rather than popping.</summary>
    private static float EdgeFade(float x, float halfWidth)
    {
        const float fadeZone = 0.30f;
        float distance = Mathf.Abs(x - halfWidth) / halfWidth; // 0 centre .. 1 edge
        return distance <= 1f - fadeZone
            ? 1f
            : Mathf.Clamp((1f - distance) / fadeZone, 0f, 1f);
    }

    /// <summary>A centred, shadowed label on a given baseline. The shadow is not decoration: the
    /// strip has no panel behind it now, so a letter crossing a bright sky is otherwise invisible.</summary>
    private void DrawLabel(Font font, string text, float x, Color colour, int sizeToken, float baselineY)
    {
        int size = UiTheme.FontSize(sizeToken);
        Vector2 measured = font.GetStringSize(text, HorizontalAlignment.Left, -1f, size);
        var pos = new Vector2(x - (measured.X / 2f), baselineY);

        DrawString(font, pos + Vector2.One, text, HorizontalAlignment.Left, -1f, size,
            new Color(UiTheme.Engrave, colour.A));
        DrawString(font, pos, text, HorizontalAlignment.Left, -1f, size, colour);
    }

    /// <summary>The player's compass heading from its facing (forward = -Z).</summary>
    private static float HeadingOf(Node3D body)
    {
        Vector3 forward = -body.GlobalBasis.Z;
        return CompassMath.HeadingFromForward(forward.X, forward.Z);
    }

    /// <summary>Tracked quest → its first incomplete objective → its nearest live world target, or
    /// the authored destination when nothing matching is loaded (39.5C).</summary>
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
            // ⚠️ ACTIVE, not merely incomplete (41D). A branch objective the player is not on is
            // inert — neither done nor pending — and the needle would happily point at it, sending
            // the player down the path they declined. Same line, same reason, in MapScreen and
            // GameHud: three surfaces, one fact (invariant 5).
            if (!progress.IsObjectiveComplete(i) && progress.IsObjectiveActive(i))
            {
                return ObjectiveLocator.Locate(objectives[i], GetTree(), body.GlobalPosition);
            }
        }

        return null; // active quest with all objectives met (awaiting turn-in)
    }
}
