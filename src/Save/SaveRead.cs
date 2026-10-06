using Godot;

namespace Embervale.Save;

/// <summary>
/// Tolerant reads of a saved dictionary, for <see cref="ISaveable.Load"/> implementations. A save
/// outlives the build that wrote it: a key can be absent (an older save), hold another type (a
/// hand edit, a changed field), or name content that has since been removed. None of those may
/// throw, because one throwing <c>Load</c> fails the whole load and the player loses the slot.
/// Every read here answers with the caller's fallback instead. ⚠️ <c>data["key"]</c> throws on an
/// absent key and <c>Variant.AsGodotDictionary()</c> on a non-dictionary; these do neither.
/// </summary>
public static class SaveRead
{
    /// <summary>A string, or <paramref name="fallback"/> when absent or not a string.</summary>
    public static string Text(Godot.Collections.Dictionary? data, string key, string fallback = "") =>
        data != null && data.TryGetValue(key, out Variant value) &&
        value.VariantType is Variant.Type.String or Variant.Type.StringName
            ? value.AsString()
            : fallback;

    /// <summary>An integer, or <paramref name="fallback"/> when absent or not a finite number.</summary>
    public static int Int(Godot.Collections.Dictionary? data, string key, int fallback = 0) =>
        TryNumber(data, key, out double number) ? (int)System.Math.Clamp(number, int.MinValue, int.MaxValue) : fallback;

    /// <summary>A number, or <paramref name="fallback"/> when absent or not a finite number.</summary>
    public static double Number(Godot.Collections.Dictionary? data, string key, double fallback = 0d) =>
        TryNumber(data, key, out double number) ? number : fallback;

    /// <inheritdoc cref="Number"/>
    public static float Float(Godot.Collections.Dictionary? data, string key, float fallback = 0f) =>
        TryNumber(data, key, out double number) ? (float)number : fallback;

    /// <summary>A flag, or <paramref name="fallback"/> when absent. A number counts as true when non-zero.</summary>
    public static bool Flag(Godot.Collections.Dictionary? data, string key, bool fallback = false)
    {
        if (data == null || !data.TryGetValue(key, out Variant value))
        {
            return fallback;
        }

        return value.VariantType switch
        {
            Variant.Type.Bool => value.AsBool(),
            Variant.Type.Int or Variant.Type.Float => value.AsDouble() != 0d,
            _ => fallback,
        };
    }

    /// <summary>Whether <paramref name="key"/> holds a finite number, and that number.</summary>
    public static bool TryNumber(Godot.Collections.Dictionary? data, string key, out double number)
    {
        number = 0d;
        if (data == null || !data.TryGetValue(key, out Variant value) ||
            value.VariantType is not (Variant.Type.Int or Variant.Type.Float))
        {
            return false;
        }

        number = value.AsDouble();
        return double.IsFinite(number);
    }

    /// <summary>A nested dictionary, or null when absent or not a dictionary.</summary>
    public static Godot.Collections.Dictionary? Section(Godot.Collections.Dictionary? data, string key) =>
        data != null && data.TryGetValue(key, out Variant value) ? AsSection(value) : null;

    /// <summary>The value as a dictionary, or null when it is anything else (an array element that
    /// should have been an entry).</summary>
    public static Godot.Collections.Dictionary? AsSection(Variant value) =>
        value.VariantType == Variant.Type.Dictionary ? value.AsGodotDictionary() : null;

    /// <summary>A nested array, or an empty one when absent or not an array, so a caller can
    /// iterate it without a guard.</summary>
    public static Godot.Collections.Array List(Godot.Collections.Dictionary? data, string key) =>
        data != null && data.TryGetValue(key, out Variant value) && value.VariantType == Variant.Type.Array
            ? value.AsGodotArray()
            : new Godot.Collections.Array();
}
