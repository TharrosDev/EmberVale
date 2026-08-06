using Godot;

namespace Embervale.UI;

/// <summary>
/// The decorative layer (Phase 37.5A): brass corner ornaments and the three shader-backed
/// magical motifs. Kept out of <see cref="UiTheme"/> because ornament is not a token — it is
/// something a screen *earns*.
///
/// **The ornament budget, which is the rule that stops this becoming clutter:** decoration
/// scales with the rarity of the moment, not with the importance of the widget. The main menu,
/// the boss frame, the spellbook and a Legendary drop get corner brass and glow. An inventory
/// row, a settings toggle and a quest objective get none, forever. If every surface is
/// ornamented the ornament stops meaning anything, and the readability the brief asks for is
/// the first thing to go.
///
/// The animated motifs (<see cref="RuneCircle"/>, <see cref="SigilField"/>,
/// <see cref="InkShimmer"/>) all live on <see cref="ColorRect"/>s rather than on panels. That is
/// load-bearing: a ColorRect's UV is guaranteed to span 0..1 over its rect, and every one of
/// these shaders does polar or sweep maths in UV space. On a rounded <see cref="PanelContainer"/>
/// the same shader would drift off centre as the panel resized.
/// </summary>
public static class UiOrnament
{
    private const string RuneCirclePath = "res://assets/shaders/ui/rune_circle.gdshader";
    private const string SigilDriftPath = "res://assets/shaders/ui/sigil_drift.gdshader";
    private const string InkShimmerPath = "res://assets/shaders/ui/ink_shimmer.gdshader";

    private static Shader? _rune, _sigil, _shimmer;
    private static bool _triedRune, _triedSigil, _triedShimmer;

    /// <summary>
    /// A rotating rune diagram, sized square. The spellbook's centrepiece.
    ///
    /// Under reduced motion the ring is built holding its start angle rather than being omitted:
    /// the setting exists to remove movement, not to strip a screen of its identity. The
    /// <c>motion</c> uniform is read once at build time, so a mid-session settings change lands on
    /// the next rebuild — which is how the panels already handle every other token.
    /// </summary>
    public static ColorRect RuneCircle(float size, Color? color = null, float intensity = 0.55f, float ticks = 24f)
    {
        var rect = Effect(ref _rune, ref _triedRune, RuneCirclePath, size, size);
        if (rect.Material is ShaderMaterial material)
        {
            material.SetShaderParameter("ring_color", color ?? UiTheme.GlyphLight);
            material.SetShaderParameter("intensity", intensity);
            material.SetShaderParameter("tick_count", ticks);
        }

        return rect;
    }

    /// <summary>An ambient field of drifting sigils, meant to fill a panel behind its content.
    /// Deliberately faint — it sits under body text.</summary>
    public static ColorRect SigilField(Color? color = null, float alphaMax = 0.10f, float density = 9f)
    {
        var rect = Effect(ref _sigil, ref _triedSigil, SigilDriftPath, 0f, 0f);
        rect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        if (rect.Material is ShaderMaterial material)
        {
            material.SetShaderParameter("sigil_color", color ?? UiTheme.GlyphLight);
            material.SetShaderParameter("alpha_max", alphaMax);
            material.SetShaderParameter("density", density);
        }

        return rect;
    }

    /// <summary>A slow highlight sweeping across a heading, like light over gold leaf.
    /// Rationed to the title screen and the spellbook's school heading — see the ornament budget
    /// above. Renders nothing at all under reduced motion, because a *travelling* highlight has
    /// nothing meaningful to show frozen.</summary>
    public static ColorRect InkShimmer(Color? color = null, float period = 7f, float intensity = 0.5f)
    {
        var rect = Effect(ref _shimmer, ref _triedShimmer, InkShimmerPath, 0f, 0f);
        rect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        if (rect.Material is ShaderMaterial material)
        {
            material.SetShaderParameter("shimmer_color", color ?? UiTheme.Accent);
            material.SetShaderParameter("period", period);
            material.SetShaderParameter("intensity", intensity);
        }

        return rect;
    }

    /// <summary>
    /// Brass corner brackets laid over a panel — four L-shaped rules inset from its corners.
    /// Built as plain <see cref="ColorRect"/>s rather than a texture so they retint with the
    /// palette (and with the 37.5G high-contrast mode) for free.
    ///
    /// Returns a mouse-transparent overlay to be added as the panel's **last** child so it draws
    /// on top; it never intercepts input.
    /// </summary>
    public static Control CornerBrass(float arm = 14f, float thickness = 2f, float inset = 4f, Color? color = null)
    {
        Color brass = color ?? UiTheme.BrassLit;

        var overlay = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        overlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);

        // Each corner is a horizontal arm and a vertical arm sharing an origin. Anchored to the
        // corner rather than positioned, so the brackets stay welded there at any panel size.
        foreach ((int ax, int ay) in new[] { (0, 0), (1, 0), (0, 1), (1, 1) })
        {
            overlay.AddChild(Arm(brass, new Vector2(arm, thickness), ax, ay, inset));
            overlay.AddChild(Arm(brass, new Vector2(thickness, arm), ax, ay, inset));
        }

        return overlay;
    }

    /// <summary>One bracket arm, anchored to the corner named by <paramref name="ax"/>/
    /// <paramref name="ay"/> (0 = left/top edge, 1 = right/bottom). Offsets are written directly
    /// rather than going through <c>Position</c>, whose meaning depends on the anchors that were
    /// set before it.</summary>
    private static ColorRect Arm(Color color, Vector2 size, int ax, int ay, float inset)
    {
        var rect = new ColorRect
        {
            Color = color,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            AnchorLeft = ax,
            AnchorRight = ax,
            AnchorTop = ay,
            AnchorBottom = ay,
        };

        rect.OffsetLeft = ax == 0 ? inset : -size.X - inset;
        rect.OffsetRight = rect.OffsetLeft + size.X;
        rect.OffsetTop = ay == 0 ? inset : -size.Y - inset;
        rect.OffsetBottom = rect.OffsetTop + size.Y;
        return rect;
    }

    /// <summary>Builds a shader-backed effect rect, wiring the shared <c>motion</c> uniform from
    /// <see cref="UiTheme.MotionUniform"/> so one accessibility setting stops all three motifs.
    /// A missing shader yields a fully transparent rect rather than a null — a decorative layer
    /// that fails must cost nothing, not crash the screen it decorates.</summary>
    private static ColorRect Effect(ref Shader? cached, ref bool tried, string path, float width, float height)
    {
        if (!tried)
        {
            tried = true;
            cached = ResourceLoader.Exists(path) ? GD.Load<Shader>(path) : null;
            if (cached is null)
            {
                Core.Diagnostics.Log.Warn($"UiOrnament: could not load '{path}'; the motif will not render.");
            }
        }

        var rect = new ColorRect
        {
            Color = new Color(1f, 1f, 1f, 0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };

        if (width > 0f || height > 0f)
        {
            rect.CustomMinimumSize = new Vector2(width, height);
        }

        if (cached is { } shader)
        {
            var material = new ShaderMaterial { Shader = shader };
            material.SetShaderParameter("motion", UiTheme.MotionUniform);
            rect.Material = material;
        }

        return rect;
    }
}
