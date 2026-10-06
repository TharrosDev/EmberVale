using Embervale.Core.Services;
using Embervale.Settings;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The painting behind the title: one of four, by how far the newest save has got
/// (<see cref="ShellFrontRules.TitlePainting"/>), drawn a little past the view so it can drift,
/// with embers rising across it.
///
/// The drift is a looping tween and the embers a small <see cref="CpuParticles2D"/>; nothing here
/// overrides <c>_Process</c>. Both are off, and the painting sits still, under reduced motion or
/// the static-menu-background setting: call <see cref="Refresh"/> when either may have changed.
/// </summary>
public partial class TitleBackdrop : Control
{
    /// <summary>Seconds for the painting to cross its drift and come back.</summary>
    private const float DriftSeconds = 46f;

    private const int EmberCount = 40;
    private const float EmberLifetime = 11f;

    private readonly TextureRect _art;
    private readonly CpuParticles2D _embers;
    private Tween? _drift;
    private int _act;

    /// <summary>The act whose painting is up.</summary>
    public int Act => _act;

    /// <summary>Whether the painting is drifting and the embers rising right now.</summary>
    public bool Moving => _embers.Emitting;

    public TitleBackdrop()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        ClipContents = true;
        SetAnchorsPreset(LayoutPreset.FullRect);

        // Sized by hand (Layout), not by anchors: it overhangs the view and its position is the drift's.
        _art = UiTheme.Cover(null);
        _art.SetAnchorsPreset(LayoutPreset.TopLeft);
        AddChild(_art);

        // Sparks off a banked fire: few, small, slow, and gone before they reach the top.
        var cooling = new Gradient
        {
            Offsets = new[] { 0f, 0.15f, 0.7f, 1f },
            Colors = new[]
            {
                UiTheme.AccentHot with { A = 0f }, UiTheme.AccentHot with { A = 0.85f },
                UiTheme.Accent with { A = 0.55f }, UiTheme.EmberGlow with { A = 0f },
            },
        };
        _embers = new CpuParticles2D
        {
            Amount = EmberCount,
            Lifetime = EmberLifetime,
            Preprocess = EmberLifetime,
            Randomness = 1f,
            EmissionShape = CpuParticles2D.EmissionShapeEnum.Rectangle,
            Direction = new Vector2(0.18f, -1f),
            Spread = 22f,
            Gravity = new Vector2(3f, -5f),
            InitialVelocityMin = 14f,
            InitialVelocityMax = 44f,
            ScaleAmountMin = 1.5f,
            ScaleAmountMax = 3f,
            ColorRamp = cooling,
            Emitting = false,
            Visible = false,
        };
        AddChild(_embers);

        Resized += Layout;
    }

    public override void _Ready()
    {
        Layout();
        Refresh();
    }

    /// <summary>Puts up the painting for <paramref name="act"/>. A no-op for the act already shown.</summary>
    public void SetAct(int act)
    {
        if (act == _act && _art.Texture != null)
        {
            return;
        }

        _act = act;
        _art.Texture = UiTheme.Painting(ShellFrontRules.TitlePainting(act));
    }

    /// <summary>Starts or stops the drift and the embers to match the settings as they are now.</summary>
    public void Refresh()
    {
        bool move = UiTheme.MotionEnabled && !StaticBackground && IsInsideTree();
        if (move == Moving && (move == (_drift != null)))
        {
            return;
        }

        _drift?.Kill();
        _drift = null;
        _embers.Emitting = move;
        _embers.Visible = move;
        _art.Position = new Vector2(-UiTheme.BackdropOverscan, -UiTheme.BackdropOverscan);
        if (!move)
        {
            return;
        }

        // Across and back along a shallow diagonal, inside the overscan, for as long as it is up.
        Vector2 rest = _art.Position;
        var reach = new Vector2(UiTheme.BackdropOverscan, UiTheme.BackdropOverscan * 0.5f);
        _art.Position = rest - reach;
        _drift = CreateTween().SetLoops();
        _drift.SetIgnoreTimeScale(true);
        _drift.TweenProperty(_art, "position", rest + reach, DriftSeconds * 0.5f)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        _drift.TweenProperty(_art, "position", rest - reach, DriftSeconds * 0.5f)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
    }

    private static bool StaticBackground =>
        ServiceLocator.Instance is { } locator &&
        locator.TryGet(out SettingsService settings) &&
        settings.Current.StaticMenuBackground;

    private void Layout()
    {
        // The painting overhangs the view on every side; the embers rise from under its bottom edge.
        float over = UiTheme.BackdropOverscan;
        _art.Size = Size + new Vector2(over * 2f, over * 2f);
        if (_drift == null)
        {
            _art.Position = new Vector2(-over, -over); // at rest it overhangs evenly
        }
        _embers.Position = new Vector2(Size.X * 0.5f, Size.Y + over);
        _embers.EmissionRectExtents = new Vector2(Size.X * 0.5f, over * 0.5f);
    }
}
