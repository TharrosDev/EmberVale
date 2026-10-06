using Godot;

namespace Embervale.UI;

/// <summary>
/// The mark beside an ingredient's count: a tick when enough is held, a cross when it is not, a
/// plus when a master is adding the shortfall to the bill. Drawn rather than typed, like
/// <see cref="UiDeltaArrow"/>, so it cannot come out as a missing-glyph box; the shape is the
/// channel that survives a colour-vision mode and the colour only agrees with it.
/// </summary>
public sealed partial class TradeMark : Control
{
    private readonly TradeRules.IngredientState _state;

    public TradeMark(TradeRules.IngredientState state, float size = 12f)
    {
        _state = state;
        CustomMinimumSize = new Vector2(size, size);
        SizeFlagsVertical = SizeFlags.ShrinkCenter;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    /// <summary>The colour that goes with a state, for the count printed beside the mark.</summary>
    public static Color ColorOf(TradeRules.IngredientState state) => state switch
    {
        TradeRules.IngredientState.Enough => UiTheme.Good,
        TradeRules.IngredientState.Supplied => UiTheme.Accent,
        _ => UiTheme.Bad,
    };

    public override void _Draw()
    {
        float w = Size.X;
        float h = Size.Y;
        float stroke = UiTheme.HighContrast ? 3f : 2f;
        Color color = ColorOf(_state);

        switch (_state)
        {
            case TradeRules.IngredientState.Enough:
                DrawPolyline(
                    new[] { new Vector2(w * 0.10f, h * 0.55f), new Vector2(w * 0.40f, h * 0.85f), new Vector2(w * 0.92f, h * 0.18f) },
                    color, stroke, true);
                break;

            case TradeRules.IngredientState.Supplied:
                DrawLine(new Vector2(w * 0.5f, h * 0.12f), new Vector2(w * 0.5f, h * 0.88f), color, stroke, true);
                DrawLine(new Vector2(w * 0.12f, h * 0.5f), new Vector2(w * 0.88f, h * 0.5f), color, stroke, true);
                break;

            default:
                DrawLine(new Vector2(w * 0.15f, h * 0.15f), new Vector2(w * 0.85f, h * 0.85f), color, stroke, true);
                DrawLine(new Vector2(w * 0.85f, h * 0.15f), new Vector2(w * 0.15f, h * 0.85f), color, stroke, true);
                break;
        }
    }
}
