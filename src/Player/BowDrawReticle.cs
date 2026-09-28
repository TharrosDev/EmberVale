using Embervale.Localization;
using Embervale.UI;
using Godot;

namespace Embervale.Player;

/// <summary>A ring around the screen centre that fills as the bow is drawn, turns ember-hot when the
/// draw is full, flickers red when the arms give out, and names a headshot for a beat.</summary>
public partial class BowDrawReticle : Control
{
    private const float Radius = 22f;
    private const double FlashSeconds = 0.6;

    private float _charge;
    private bool _strained;
    private double _flash;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        GetViewport().SizeChanged += QueueRedraw;
    }

    public void Show(float charge, bool strained)
    {
        if (Mathf.IsEqualApprox(charge, _charge) && strained == _strained)
        {
            return;
        }

        _charge = charge;
        _strained = strained;
        QueueRedraw();
    }

    public void Flash()
    {
        _flash = FlashSeconds;
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (_flash <= 0d)
        {
            return;
        }

        _flash = Mathf.Max(_flash - delta, 0d);
        QueueRedraw();
    }

    public override void _Draw()
    {
        Vector2 c = (GetViewportRect().Size * 0.5f).Round();

        if (_charge > 0f)
        {
            Color color = _strained ? UiTheme.Bad : _charge >= 1f ? UiTheme.AccentHot : UiTheme.Text;
            DrawArc(c, Radius, -Mathf.Pi / 2f, (-Mathf.Pi / 2f) + (Mathf.Tau * _charge), 48,
                new Color(0f, 0f, 0f, 0.5f), 5f, true);
            DrawArc(c, Radius, -Mathf.Pi / 2f, (-Mathf.Pi / 2f) + (Mathf.Tau * _charge), 48,
                new Color(color, 0.95f), 3f, true);
        }

        if (_flash > 0d)
        {
            Font font = ThemeDB.FallbackFont;
            string text = Loc.T("combat.ranged.headshot");
            var at = new Vector2(c.X - (font.GetStringSize(text, HorizontalAlignment.Left, -1, 18).X * 0.5f),
                c.Y + Radius + 26f);
            DrawString(font, at, text, HorizontalAlignment.Left, -1, 18,
                new Color(UiTheme.AccentHot, (float)(_flash / FlashSeconds)));
        }
    }
}
