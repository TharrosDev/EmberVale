using Embervale.Combat;
using Godot;

namespace Embervale.Magic.Vfx;

/// <summary>
/// The one screen flash spells share: a full-screen tint under the HUD that rises in a few frames
/// and falls away. There is exactly one, so two heavy spells landing together cannot stack into a
/// white-out, and every limit on it lives in <see cref="VfxScreenRules"/>: the peak is scaled by the
/// player's own flash setting, an enemy's spell flashes only when it struck the player, Reduced
/// Motion caps it, and flashes keep their distance from each other.
/// </summary>
public partial class VfxScreen : CanvasLayer
{
    private readonly ColorRect _rect;
    private readonly TextureRect _edge;
    private double _edgeAge = 10d;
    private float _edgePeak;
    private float _edgeSeconds = 1f;
    private double _clock;
    private double _lastFlash = -10d;
    private double _age = 10d;
    private float _peak;
    private Color _tint = Colors.White;

    public VfxScreen()
    {
        Name = "VfxScreen";
        Layer = 0; // over the world, under the HUD and every menu

        // A menu opened mid-flash pauses the tree. The flash must still finish fading: frozen, it
        // would hold a bright tint over the world for as long as the menu stayed open.
        ProcessMode = ProcessModeEnum.Always;
        _rect = new ColorRect
        {
            Name = "Flash",
            Color = new Color(1f, 1f, 1f, 0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false,
        };
        _rect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_rect);

        // The edge shimmer: a tint that is clear across the middle of the frame and gathers at
        // its edges, for an effect on the local player's own body in first person.
        _edge = new TextureRect
        {
            Name = "Edge",
            Texture = VfxTextures.Edge,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Modulate = new Color(1f, 1f, 1f, 0f),
            Visible = false,
        };
        _edge.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_edge);
    }

    /// <summary>
    /// Asks for an edge shimmer of <paramref name="strength"/> (0..1) in a school colour, lasting
    /// <paramref name="seconds"/>. Returns whether it was shown: a fainter one never cuts a stronger
    /// one short.
    /// </summary>
    internal bool Edge(Color school, float strength, float seconds)
    {
        float peak = VfxScreenRules.EdgePeak(strength, LiveComfort.Get().ScreenFlash, VfxQuality.ReducedMotion);
        if (peak <= 0.004f)
        {
            return false;
        }

        if (_edge.Visible && VfxScreenRules.EdgeEnvelope(_edgeAge, _edgePeak, _edgeSeconds) > peak)
        {
            return false;
        }

        // One already showing carries on from its own brightness instead of dipping to nothing.
        _edgeAge = _edge.Visible ? VfxScreenRules.EdgeAttackSeconds : 0d;
        _edgePeak = peak;
        _edgeSeconds = Mathf.Max(0.3f, seconds);
        Color tint = VfxScreenRules.EdgeTint(school);
        _edge.Modulate = new Color(tint.R, tint.G, tint.B, _edge.Visible ? peak : 0f);
        _edge.Visible = true;
        return true;
    }

    /// <summary>The peak alpha of the flash now on screen, for a probe.</summary>
    public float Peak => _peak;

    /// <summary>
    /// Asks for a flash of <paramref name="strength"/> (0..1) in a school colour. Returns whether it
    /// was shown: it is dropped when it would be invisible, or too soon after the last one.
    /// </summary>
    internal bool Flash(Color school, float strength, bool byPlayer, bool hitsPlayer)
    {
        float peak = VfxScreenRules.Peak(
            strength, LiveComfort.Get().ScreenFlash, VfxQuality.ReducedMotion, byPlayer, hitsPlayer);
        if (peak <= 0.004f || !VfxScreenRules.Allowed(_clock, _lastFlash))
        {
            return false;
        }

        _lastFlash = _clock;
        _age = 0d;
        _peak = peak;
        _tint = VfxScreenRules.Tint(school);
        _rect.Visible = true;
        return true;
    }

    /// <summary>Clears the flash at once (a load began).</summary>
    internal void Cut()
    {
        _age = 10d;
        _peak = 0f;
        _rect.Visible = false;
        _edgeAge = 10d;
        _edgePeak = 0f;
        _edge.Visible = false;
    }

    public override void _Process(double delta)
    {
        _clock += delta;
        if (_edge.Visible)
        {
            _edgeAge += delta;
            float shimmer = VfxScreenRules.EdgeEnvelope(_edgeAge, _edgePeak, _edgeSeconds);
            if (shimmer <= 0f && _edgeAge > VfxScreenRules.EdgeAttackSeconds)
            {
                _edge.Visible = false;
                _edgePeak = 0f;
            }
            else
            {
                Color tint = _edge.Modulate;
                _edge.Modulate = new Color(tint.R, tint.G, tint.B, shimmer);
            }
        }

        if (!_rect.Visible)
        {
            return;
        }

        _age += delta;
        float alpha = VfxScreenRules.Envelope(_age, _peak);
        if (alpha <= 0f && _age > VfxScreenRules.AttackSeconds)
        {
            _rect.Visible = false;
            _peak = 0f;
            return;
        }

        _rect.Color = new Color(_tint.R, _tint.G, _tint.B, alpha);
    }
}
