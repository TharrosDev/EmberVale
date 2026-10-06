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

    // How the objective chevron is drawn (campaign UI): the tracked quest's kind picks its colour, an
    // optional objective is hollow, and a cross-realm objective points at a door and names the realm.
    private CompassMarkKind _markKind = CompassMarkKind.Main;
    private CompassMarkState _markState = CompassMarkState.Active;
    private string? _portalRegionName;

    // --- Redraw-on-change (performance pass) -----------------------------------
    //
    // The strip used to repaint itself every frame: ~115 rule segments, the centre wedge, up to five
    // letters each measured and drawn twice, and a freshly formatted distance. Now the parts that
    // never move (the rule and the wedge) live on their own canvas item, drawn once and kept by the
    // engine, and the moving parts repaint only when what they show has moved by more than the eye
    // can see: the heading by a tenth of a degree (a quarter of a pixel on this strip) or, with a
    // destination marked, the player by two centimetres.
    private const float HeadingStep = Mathf.Pi / 1800f; // 0.1 degree
    private const float MoveStepSquared = 0.02f * 0.02f;

    private Control _fixed = null!;
    private bool _stale = true;
    private bool _drawnBody;
    private float _drawnHeading;
    private Vector3 _drawnOrigin;
    private Vector3? _drawnWaypoint;

    // The eight letters, translated and measured once per font and size instead of once per frame.
    private readonly string[] _cardinalText = new string[Cardinals.Length];
    private readonly float[] _cardinalWidth = new float[Cardinals.Length];
    private Font? _measuredFont;
    private int _measuredBodySize;
    private int _measuredCaptionSize;

    // The distance line under each destination, rebuilt when the number or its label changes.
    private DistanceLabel _objectiveDistance;
    private DistanceLabel _waypointDistance;

    private struct DistanceLabel
    {
        public bool Known;
        public float Shown;
        public string? Prefix;
        public int Size;
        public string Text;
        public float Width;
    }

    // Reused polygon buffers: the engine copies the points on each draw call, so one array per
    // shape is enough and none is allocated per frame.
    private readonly Vector2[] _three = new Vector2[3];
    private readonly Vector2[] _four = new Vector2[4];
    private readonly Vector2[] _five = new Vector2[5];

    public void SetPlayer(IEntity? player)
    {
        _player = player;
        _stale = true;
    }

    /// <summary>Drops the cached letters and distance lines and repaints (locale or colour-vision
    /// change): the strip holds translated text and adapted colours.</summary>
    public void InvalidateText()
    {
        _measuredFont = null;
        _objectiveDistance = default;
        _waypointDistance = default;
        _stale = true;
        _fixed?.QueueRedraw();
    }

    /// <summary>The tracked objective's world position, or null when there is nothing to walk toward.
    ///
    /// Exposed so the quest tracker prints the distance and bearing to the same point this strip
    /// marks (39.5B). Resolving it twice would mean two <see cref="ObjectiveLocator"/> scene walks
    /// per interval and — worse — two answers, which is how a tracker saying "320 m NW" ends up
    /// beside a compass marker pointing east.</summary>
    public Vector3? ObjectiveTarget => _objectiveTarget;

    /// <summary>The realm the chevron is pointing toward through a door, or null when it points at the
    /// objective itself. The quest tracker reads it so its readout names the same realm.</summary>
    public string? PortalRegionName => _portalRegionName;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        CustomMinimumSize = new Vector2(Width, Height);
        Size = CustomMinimumSize;

        // The rule and the centre wedge. Behind the parent, so the letters and destination marks
        // still draw over them exactly as they did when all of it was one pass.
        _fixed = new Control { Name = "Fixed", MouseFilter = MouseFilterEnum.Ignore, ShowBehindParent = true };
        _fixed.SetAnchorsPreset(LayoutPreset.FullRect);
        _fixed.Draw += DrawFixed;
        AddChild(_fixed);
    }

    public override void _Process(double delta)
    {
        _resolveTimer -= (float)delta;
        if (_resolveTimer <= 0f)
        {
            _resolveTimer = ObjectiveResolveInterval;
            Vector3? before = _objectiveTarget;
            CompassMarkKind kindBefore = _markKind;
            CompassMarkState stateBefore = _markState;
            string? realmBefore = _portalRegionName;
            _objectiveTarget = ResolveObjectiveTarget();
            if (before != _objectiveTarget || kindBefore != _markKind || stateBefore != _markState ||
                realmBefore != _portalRegionName)
            {
                _stale = true;
            }
        }

        // Hidden (a boss owns the top centre, or a menu is up): nothing to repaint. The engine
        // repaints the strip itself when it is shown again, and _stale survives until then.
        if (IsVisibleInTree() && NeedsRedraw())
        {
            _stale = false;
            QueueRedraw();
        }
    }

    /// <summary>Whether anything the moving half of the strip shows has changed since it was last
    /// painted. <see cref="_Draw"/> records what it painted, so this compares against the screen.</summary>
    private bool NeedsRedraw()
    {
        if (_stale)
        {
            return true;
        }

        if (_player?.Body is not { } body || !IsInstanceValid(body))
        {
            return _drawnBody; // the letters are up and there is no longer a player to face
        }

        if (!_drawnBody ||
            Mathf.Abs(CompassMath.WrapPi(HeadingOf(body) - _drawnHeading)) > HeadingStep)
        {
            return true;
        }

        Vector3? waypoint = CurrentWaypoint();
        if (waypoint != _drawnWaypoint)
        {
            return true;
        }

        // Standing still or walking with nothing marked, the player's position changes nothing here.
        if (_objectiveTarget == null && waypoint == null)
        {
            return false;
        }

        Vector3 origin = body.GlobalPosition;
        float dx = origin.X - _drawnOrigin.X;
        float dz = origin.Z - _drawnOrigin.Z;
        return (dx * dx) + (dz * dz) > MoveStepSquared;
    }

    private static Vector3? CurrentWaypoint() =>
        ServiceLocator.Instance is { } locator && locator.TryGet(out MapService map) ? map.Waypoint : null;

    private void DrawFixed()
    {
        float halfWidth = Size.X / 2f;
        DrawRule(halfWidth);
        DrawCentreMark(halfWidth);
    }

    public override void _Draw()
    {
        float halfWidth = Size.X / 2f;
        float centreX = halfWidth;

        if (_player?.Body is not { } body || !IsInstanceValid(body))
        {
            _drawnBody = false;
            return;
        }

        float heading = HeadingOf(body);
        Vector3 origin = body.GlobalPosition;
        Font font = GetThemeDefaultFont();

        DrawCardinals(font, heading, halfWidth, centreX);

        // Destinations last so nothing draws over them. The objective is the game's mark; the
        // waypoint is the player's own and wins ties by being drawn after it.
        Vector3? waypoint = CurrentWaypoint();

        _drawnBody = true;
        _drawnHeading = heading;
        _drawnOrigin = origin;
        _drawnWaypoint = waypoint;

        if (_objectiveTarget is { } target)
        {
            // Main-thread objectives wear the ember of the journal's main spine and errands its quieter
            // tone; both agree with the tracker band beside them. An optional objective is hollow.
            Color objectiveTint = _markKind == CompassMarkKind.Main ? UiTheme.QuestMain : UiTheme.QuestSide;
            DrawDestination(font, target, origin, heading, halfWidth, centreX, objectiveTint,
                playerWaypoint: false, showDistance: waypoint == null, ref _objectiveDistance,
                hollow: _markState == CompassMarkState.Optional, label: _portalRegionName);
        }

        if (waypoint is { } playerMark)
        {
            DrawDestination(font, playerMark, origin, heading, halfWidth, centreX, UiTheme.AccentHot,
                playerWaypoint: true, showDistance: true, ref _waypointDistance);
        }
    }

    /// <summary>The horizon: one hairline, fading to nothing at both ends so the strip has no edges
    /// to pop against. Drawn in segments because a single line cannot carry a gradient.</summary>
    private void DrawRule(float halfWidth)
    {
        float width = Size.X;
        int segments = Mathf.Max(Mathf.RoundToInt(width / 4f), 8);
        float step = width / segments;

        for (int i = 0; i < segments; i++)
        {
            float x = i * step;
            float alpha = EdgeFade(x + (step * 0.5f), halfWidth);
            _fixed.DrawRect(new Rect2(x, RuleY, step + 1f, 1f), new Color(UiTheme.Brass, 0.55f * alpha));
        }
    }

    /// <summary>Where the player is facing: one small wedge on the rule, pointing up into the
    /// letters. The only fixed element on the strip.</summary>
    private void DrawCentreMark(float centreX)
    {
        var mark = UiTheme.Adapt(UiTheme.Accent);
        _three[0] = new Vector2(centreX - 5f, RuleY + 6f);
        _three[1] = new Vector2(centreX + 5f, RuleY + 6f);
        _three[2] = new Vector2(centreX, RuleY - 1f);
        _fixed.DrawColoredPolygon(_three, mark);
    }

    /// <summary>The eight headings. N is ember and the true cardinals are bone at body size; the
    /// intercardinals are dim and smaller — so a glance lands on one letter, not eight.</summary>
    private void DrawCardinals(Font font, float heading, float halfWidth, float centreX)
    {
        int bodySize = UiTheme.FontSize(UiTheme.BodyFontSize);
        int captionSize = UiTheme.FontSize(UiTheme.CaptionFontSize);
        if (!ReferenceEquals(font, _measuredFont) || bodySize != _measuredBodySize ||
            captionSize != _measuredCaptionSize)
        {
            _measuredFont = font;
            _measuredBodySize = bodySize;
            _measuredCaptionSize = captionSize;
            for (int i = 0; i < Cardinals.Length; i++)
            {
                _cardinalText[i] = Loc.T(Cardinals[i].Key);
                _cardinalWidth[i] = font.GetStringSize(
                    _cardinalText[i], HorizontalAlignment.Left, -1f, IsMajor(Cardinals[i].Angle) ? bodySize : captionSize).X;
            }
        }

        for (int i = 0; i < Cardinals.Length; i++)
        {
            float angle = Cardinals[i].Angle;
            float rel = CompassMath.Relative(angle, heading);
            if (!CompassMath.InView(rel, Fov))
            {
                continue;
            }

            float x = centreX + CompassMath.StripOffset(rel, Fov, halfWidth);
            bool major = IsMajor(angle);
            bool north = Mathf.IsZeroApprox(angle);

            Color colour = north ? UiTheme.Adapt(UiTheme.Accent) : major ? UiTheme.Text : UiTheme.Dim;
            DrawMeasuredLabel(font, _cardinalText[i], _cardinalWidth[i], x,
                new Color(colour, EdgeFade(x, halfWidth)), major ? bodySize : captionSize, LetterBaseline);
        }
    }

    private static bool IsMajor(float angle) =>
        Mathf.IsZeroApprox(Mathf.PosMod(angle + 0.001f, Mathf.Pi / 2f) - 0.001f);

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
        Color tint, bool playerWaypoint, bool showDistance, ref DistanceLabel distance,
        bool hollow = false, string? label = null)
    {
        float dx = target.X - origin.X;
        float dz = target.Z - origin.Z;
        float rel = CompassMath.Relative(CompassMath.BearingTo(dx, dz), heading);
        Color mark = UiTheme.Adapt(tint);

        if (!CompassMath.InView(rel, Fov))
        {
            float edgeX = rel > 0f ? Size.X - 6f : 6f;
            float direction = rel > 0f ? 1f : -1f;
            var tip = new Vector2(edgeX + (direction * 5f), MarkTop + 5f);
            var top = new Vector2(edgeX - (direction * 4f), MarkTop);
            var bottom = new Vector2(edgeX - (direction * 4f), MarkTop + 10f);
            if (playerWaypoint || hollow)
            {
                _three[0] = top;
                _three[1] = tip;
                _three[2] = bottom;
                DrawPolyline(_three, new Color(mark, hollow ? 0.6f : 0.9f), 2f);
            }
            else
            {
                _three[0] = tip;
                _three[1] = top;
                _three[2] = bottom;
                DrawColoredPolygon(_three, new Color(mark, 0.85f));
            }
            return;
        }

        float x = centreX + CompassMath.StripOffset(rel, Fov, halfWidth);
        float fade = EdgeFade(x, halfWidth);

        if (playerWaypoint)
        {
            _five[0] = new Vector2(x, MarkTop);
            _five[1] = new Vector2(x + 6f, MarkTop + 5f);
            _five[2] = new Vector2(x, MarkTop + 10f);
            _five[3] = new Vector2(x - 6f, MarkTop + 5f);
            _five[4] = new Vector2(x, MarkTop);
            DrawPolyline(_five, new Color(mark, fade), 2f);
        }
        else if (hollow)
        {
            // An optional objective: the chevron's outline only, and dimmer, so it reads as "also".
            _four[0] = new Vector2(x - 6f, MarkTop);
            _four[1] = new Vector2(x + 6f, MarkTop);
            _four[2] = new Vector2(x, MarkTop + 9f);
            _four[3] = new Vector2(x - 6f, MarkTop);
            DrawPolyline(_four, new Color(mark, fade * 0.7f), 2f);
        }
        else
        {
            _three[0] = new Vector2(x - 6f, MarkTop);
            _three[1] = new Vector2(x + 6f, MarkTop);
            _three[2] = new Vector2(x, MarkTop + 9f);
            DrawColoredPolygon(_three, new Color(mark, fade));
        }

        // §16: a distance for the destination that matters, and only that one. Printing it for every
        // marker is the "excessive text" the same section warns against.
        if (showDistance)
        {
            // Whole metres below a kilometre, a tenth of a kilometre above: the line is rebuilt and
            // re-measured when that number (or the realm it names) changes, not on every repaint.
            float metres = Mathf.Sqrt((dx * dx) + (dz * dz));
            float shown = metres < 1000f
                ? System.MathF.Round(metres)
                : 100000f + System.MathF.Round(metres / 100f, System.MidpointRounding.AwayFromZero);
            int size = UiTheme.FontSize(UiTheme.CaptionFontSize);
            if (!distance.Known || shown != distance.Shown || label != distance.Prefix || size != distance.Size)
            {
                (string value, string unitKey) = CompassMath.Distance(metres);
                string readout = $"{value}{Loc.T(unitKey)}";
                distance.Known = true;
                distance.Shown = shown;
                distance.Prefix = label;
                distance.Size = size;
                distance.Text = label != null ? $"{label} {readout}" : readout;
                distance.Width = font.GetStringSize(distance.Text, HorizontalAlignment.Left, -1f, size).X;
            }

            DrawMeasuredLabel(font, distance.Text, distance.Width, x, new Color(mark, fade), size, DistanceBaseline);
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

    /// <summary>A centred, shadowed label on a given baseline, from text measured once by the caller.
    /// The shadow is not decoration: the strip has no panel behind it now, so a letter crossing a
    /// bright sky is otherwise invisible.</summary>
    private void DrawMeasuredLabel(
        Font font, string text, float width, float x, Color colour, int size, float baselineY)
    {
        var pos = new Vector2(x - (width / 2f), baselineY);

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

    /// <summary>
    /// Tracked quest, its current objective (required before optional), then where to point.
    ///
    /// A live world target or the authored place wins when the objective is in THIS region. When the
    /// objective's place is in another realm the strip points at this realm's door toward it (and names the
    /// realm), because the other realm's atlas position is a direction on a map the player cannot walk.
    /// If no door can be resolved it falls back to the old behaviour.
    /// </summary>
    private Vector3? ResolveObjectiveTarget()
    {
        _portalRegionName = null;
        if (_player is not { } player || player.Body is not { } body || !IsInstanceValid(body) ||
            player.GetComponent<QuestLogComponent>()?.Tracked is not { } progress)
        {
            return null;
        }

        // ⚠️ CURRENT, not merely incomplete or active (41D). A branch objective the player is not on is
        // inert - neither done nor pending - and the needle would happily point at it, sending the
        // player down the path they declined. The shared rule also puts a required objective ahead of an
        // optional one, so a side step can never pull the needle off the thing that finishes the quest.
        ObjectiveResource? objective = QuestProgressViews.CurrentObjective(progress, out _);
        if (objective == null)
        {
            return null; // active quest with all objectives met (awaiting turn-in)
        }

        _markKind = CompassRoutingRules.KindOf(progress.Quest.IsMainQuest);
        _markState = CompassRoutingRules.StateOf(objective.IsOptional);

        Vector3? direct = ObjectiveLocator.Locate(objective, GetTree(), body.GlobalPosition);

        string currentRegion = QuestPlaces.ActiveRegionId();
        string objectiveRegion = QuestPlaces.RegionIdOfLocation(QuestPlaces.PlaceId(objective));
        CompassMode mode = CompassRoutingRules.ModeFor(
            objectiveRegion, progress.Quest.RegionId, currentRegion, direct != null);

        if (mode == CompassMode.Portal)
        {
            string destination = CompassRoutingRules.DestinationRegion(objectiveRegion, progress.Quest.RegionId);
            string? hop = CompassRoutingRules.NextHop(
                currentRegion, destination, QuestPlaces.RegionGraph(), id => QuestPlaces.IsRegionOpen(id, player));
            if (hop != null && QuestPlaces.PortalPosition(currentRegion, hop) is { } door)
            {
                string realm = QuestPlaces.RegionName(destination);
                _portalRegionName = realm.Length > 0 ? realm : null;
                return door;
            }
        }

        return direct;
    }
}
