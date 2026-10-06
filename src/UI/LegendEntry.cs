namespace Embervale.UI;

/// <summary>
/// One pair in a footer legend (<see cref="UiLegend"/>): the input action whose glyph is drawn and
/// the verb beside it. <paramref name="Label"/> is already localised (pass <c>Loc.T(...)</c>).
/// <paramref name="SecondAction"/> draws a second glyph before the verb for a pair of inputs that
/// do one thing, such as the two shoulder buttons that switch screens.
/// </summary>
public readonly record struct LegendEntry(string Action, string Label, string? SecondAction = null);
