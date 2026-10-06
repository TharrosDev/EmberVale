using Embervale.Settings;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The HUD layout system (Phase 30.5B): a full-screen, mouse-transparent root with anchored
/// **widget slots** every HUD element parents into, replacing per-widget anchor/offset math.
/// Each slot is a stack pinned to a corner/edge, inset by one safe-area margin (one knob
/// instead of scattered magic 16s); stacked slots space their widgets automatically — hidden
/// widgets collapse, so the boss bar, event banner and nameplate never overlap the way
/// hand-tuned y-offsets did. Global UI scale is handled upstream by the window's content
/// scale factor (see <c>SettingsService.ApplyGraphics</c>), so slots need no per-widget
/// scaling.
///
/// The slots sit in one child, <see cref="Scaled"/>, which carries the player's HUD scale, HUD
/// opacity and safe zone (<see cref="ApplyScale"/>, <see cref="ApplyOpacity"/>,
/// <see cref="ApplySafeZone"/>). <see cref="Overlay"/> stays outside it: the reticles there are
/// placed in screen coordinates and the full-screen fades have to reach the edges.
///
/// Slots are anchored directly on this root (zero-size rects at their pivot that grow toward
/// screen centre as content demands — the same mechanics the pre-30.5B widgets used, minus
/// the duplication). OS safe-area insets (TV overscan, notches) are zero on the desktop/
/// Steam Deck targets, so the margin is the token inset; raise <see cref="SafeMargin"/> if a
/// platform ever needs more.
/// </summary>
public partial class HudLayout : Control
{
    /// <summary>Inset between the screen edge and every slot.</summary>
    public int SafeMargin { get; set; } = UiTheme.SpaceLg;

    /// <summary>Clear height from the screen's bottom edge to the top of the hotbar block plus a
    /// <see cref="UiTheme.HudGap"/>: anything centred above the hotbar (prompt, tutorial hint, the
    /// placement strip) sits at or above this line so it never touches the slots.</summary>
    public const int BottomClearance = 160;

    /// <summary>How far above the bottom edge the bottom-centre slot floats (prompt near the
    /// player's natural gaze): <see cref="BottomClearance"/> less the safe margin the slot adds itself.</summary>
    public int BottomCenterLift { get; set; } = BottomClearance - UiTheme.SpaceLg;

    /// <summary>Top-left stack (context: time, weather).</summary>
    public VBoxContainer TopLeft { get; private set; } = null!;

    /// <summary>Top-centre stack (compass, boss bar, event banner, nameplate) — order = add order.</summary>
    public VBoxContainer TopCenter { get; private set; } = null!;

    /// <summary>Top-right stack (quest tracker).</summary>
    public VBoxContainer TopRight { get; private set; } = null!;

    /// <summary>Bottom-left stack (vitals) — first cell of the bottom flow bar.</summary>
    public VBoxContainer BottomLeft { get; private set; } = null!;

    /// <summary>Dock for the quick-use hotbar, centred in the bottom bar's free space. A flow
    /// sibling of <see cref="BottomLeft"/>, so the hotbar and vitals can never overlap at any
    /// UI scale or resolution.</summary>
    public VBoxContainer BottomDock { get; private set; } = null!;

    /// <summary>Bottom-centre stack (interaction prompt), floated above the screen edge.</summary>
    public VBoxContainer BottomCenter { get; private set; } = null!;

    /// <summary>Bottom-right stack (minimap, 39.5B) — the last cell of the bottom flow bar, so it can
    /// no more overlap the hotbar than the hotbar can overlap the vitals.</summary>
    public VBoxContainer BottomRight { get; private set; } = null!;

    /// <summary>Free layer for screen-space widgets that position themselves (lock reticle) and
    /// full-screen overlays (vignette, fades). Not inset by the safe margin, and not scaled.</summary>
    public Control Overlay { get; private set; } = null!;

    /// <summary>Parent of every slot. Full-rect until a HUD scale or safe zone says otherwise.</summary>
    public Control Scaled { get; private set; } = null!;

    /// <summary>The HUD scale in force (1 = unscaled).</summary>
    public float HudScale { get; private set; } = 1f;

    /// <summary>The safe zone in force, as a fraction of the screen kept clear on every side.</summary>
    public float SafeZone { get; private set; }

    // Built in the constructor ("build detached, then add", CLAUDE.md §6) so the root's and every
    // slot's anchors are configured BEFORE tree entry — anchors applied to an already-entered
    // control resolved against a stale zero rect and dumped every widget at the origin.
    public HudLayout()
    {
        Name = "Layout";
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        Overlay = new Control { Name = "Overlay", MouseFilter = MouseFilterEnum.Ignore };
        Overlay.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(Overlay);

        Scaled = new Control { Name = "Scaled", MouseFilter = MouseFilterEnum.Ignore };
        Scaled.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(Scaled);

        TopLeft = Slot("TopLeft", horizontal: 0f, vertical: 0f);
        TopCenter = Slot("TopCenter", horizontal: 0.5f, vertical: 0f);
        TopRight = Slot("TopRight", horizontal: 1f, vertical: 0f);
        BottomCenter = Slot("BottomCenter", horizontal: 0.5f, vertical: 1f, extraLift: BottomCenterLift);

        // The bottom edge is a full-width flow bar: vitals left, the hotbar dock centred in the
        // remaining space by twin spacers. Flow layout means these can never overlap, no matter
        // how small the effective viewport gets (high UI scale, low resolution, Steam Deck).
        var bar = new HBoxContainer { Name = "BottomBar", MouseFilter = MouseFilterEnum.Ignore };
        bar.AddThemeConstantOverride("separation", UiTheme.SpaceMd);
        bar.AnchorLeft = 0f;
        bar.AnchorRight = 1f;
        bar.AnchorTop = 1f;
        bar.AnchorBottom = 1f;
        bar.OffsetLeft = SafeMargin;
        bar.OffsetRight = -SafeMargin;
        bar.OffsetTop = -SafeMargin;
        bar.OffsetBottom = -SafeMargin;
        bar.GrowVertical = GrowDirection.Begin;
        Scaled.AddChild(bar);

        BottomLeft = new VBoxContainer
        {
            Name = "BottomLeft",
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsVertical = SizeFlags.ShrinkEnd,
        };
        BottomLeft.AddThemeConstantOverride("separation", UiTheme.HudGap);
        bar.AddChild(BottomLeft);

        bar.AddChild(new Control { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ExpandFill });

        BottomDock = new VBoxContainer
        {
            Name = "BottomDock",
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsVertical = SizeFlags.ShrinkEnd,
        };
        bar.AddChild(BottomDock);

        bar.AddChild(new Control { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ExpandFill });

        BottomRight = new VBoxContainer
        {
            Name = "BottomRight",
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsVertical = SizeFlags.ShrinkEnd,
        };
        BottomRight.AddThemeConstantOverride("separation", UiTheme.HudGap);
        bar.AddChild(BottomRight);
    }

    /// <summary>A stacked slot pinned to a corner/edge. <paramref name="horizontal"/> and
    /// <paramref name="vertical"/> are 0/0.5/1 anchor fractions (left/centre/right, top/bottom).
    /// The slot starts as a zero-size rect at its safe-area-inset pivot and grows toward the
    /// screen centre as its content's minimum size demands.</summary>
    private VBoxContainer Slot(string name, float horizontal, float vertical, int extraLift = 0)
    {
        var slot = new VBoxContainer { Name = name, MouseFilter = MouseFilterEnum.Ignore };
        slot.AddThemeConstantOverride("separation", UiTheme.HudGap);

        slot.AnchorLeft = horizontal;
        slot.AnchorRight = horizontal;
        slot.AnchorTop = vertical;
        slot.AnchorBottom = vertical;

        // Pivot the zero-size rect one margin in from the edge it hugs (centres stay on axis).
        float x = horizontal switch { 0f => SafeMargin, 1f => -SafeMargin, _ => 0f };
        float y = vertical == 1f ? -(SafeMargin + extraLift) : SafeMargin + extraLift;
        slot.OffsetLeft = x;
        slot.OffsetRight = x;
        slot.OffsetTop = y;
        slot.OffsetBottom = y;

        slot.GrowHorizontal = horizontal switch
        {
            0f => GrowDirection.End,
            1f => GrowDirection.Begin,
            _ => GrowDirection.Both,
        };
        slot.GrowVertical = vertical == 1f ? GrowDirection.Begin : GrowDirection.End;

        Scaled.AddChild(slot);
        return slot;
    }

    /// <summary>Scales every slot about the screen's top-left corner. The slots lay out in a rect
    /// that many times smaller, so corner widgets stay in their corners at any scale.</summary>
    public void ApplyScale(float scale)
    {
        scale = SettingsMath.ClampHudScale(scale);
        if (scale != HudScale)
        {
            HudScale = scale;
            Refit();
        }
    }

    /// <summary>Keeps <paramref name="fraction"/> of the screen clear on every side (a TV that
    /// overscans), on top of the <see cref="SafeMargin"/> every slot already has.</summary>
    public void ApplySafeZone(float fraction)
    {
        fraction = SettingsMath.ClampHudSafeZone(fraction);
        if (fraction != SafeZone)
        {
            SafeZone = fraction;
            Refit();
        }
    }

    /// <summary>Fades the slots as one. The overlay is left alone: a crosshair or a damage arc the
    /// player has turned down with the rest of the HUD is one they can no longer read.</summary>
    public void ApplyOpacity(float opacity)
    {
        opacity = SettingsMath.ClampHudOpacity(opacity);
        if (opacity != Scaled.Modulate.A)
        {
            Scaled.Modulate = new Color(1f, 1f, 1f, opacity);
        }
    }

    // Anchors rather than a size written on resize: the scaled rect is a fraction of this one, so
    // the engine keeps it fitted through every window change with nothing here listening. An anchor
    // past 1 is how a HUD scaled below 1 gets a layout rect larger than the screen.
    private void Refit()
    {
        (float left, float top, float right, float bottom) = HudMetrics.ScaledAnchors(HudScale, SafeZone);
        Scaled.SetAnchor(Side.Left, left, keepOffset: false, pushOppositeAnchor: false);
        Scaled.SetAnchor(Side.Top, top, keepOffset: false, pushOppositeAnchor: false);
        Scaled.SetAnchor(Side.Right, right, keepOffset: false, pushOppositeAnchor: false);
        Scaled.SetAnchor(Side.Bottom, bottom, keepOffset: false, pushOppositeAnchor: false);
        Scaled.OffsetLeft = 0f;
        Scaled.OffsetTop = 0f;
        Scaled.OffsetRight = 0f;
        Scaled.OffsetBottom = 0f;
        Scaled.Scale = new Vector2(HudScale, HudScale);
    }
}
