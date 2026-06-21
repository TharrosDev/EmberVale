namespace Embervale.Items;

/// <summary>
/// Where an affix's name fragment sits relative to the base item name and how it
/// is grouped during generation. A rolled item draws at most one prefix and one
/// suffix into its display name (e.g. <c>Vicious</c> Steel Sword <c>of the Bear</c>).
/// </summary>
// APPEND ONLY: ordinals persist in .tres/saves — never reorder/insert/remove (EnumStabilityTests).
public enum AffixKind
{
    Prefix,
    Suffix,
}
