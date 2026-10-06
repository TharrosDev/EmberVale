using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Embervale.Save;

/// <summary>
/// The content checksum of a save: SHA-256 over a <b>canonical</b> form of the envelope's
/// <c>objects</c> payload, stored as <c>"sha256:&lt;hex&gt;"</c> in the envelope and in the header.
/// Canonical means independent of how the JSON was written: object keys are visited in ordinal
/// order, whitespace is not part of it, strings are hashed decoded and numbers by value. So
/// re-indenting a save, or reading it and writing it back unchanged, leaves the checksum alone;
/// changing any key, string, number, or the order of an array breaks it. Godot-free, so xUnit
/// runs it.
/// </summary>
public static class SaveChecksum
{
    /// <summary>The prefix of a stored checksum; anything else is an algorithm this build cannot verify.</summary>
    public const string Prefix = "sha256:";

    /// <summary>The checksum of an <c>objects</c> element.</summary>
    public static string Compute(JsonElement objects)
    {
        var buffer = new ArrayBufferWriter<byte>(4096);
        Write(objects, buffer);
        return Prefix + Convert.ToHexString(SHA256.HashData(buffer.WrittenSpan)).ToLowerInvariant();
    }

    /// <summary>The checksum of the <c>objects</c> section of a whole envelope document, or null
    /// when the text is not JSON, not an object, or has no <c>objects</c> object.</summary>
    public static string? ComputeForEnvelope(string? envelopeJson)
    {
        if (string.IsNullOrEmpty(envelopeJson))
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(envelopeJson, SaveEnvelope.ParseOptions);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                   document.RootElement.TryGetProperty(SaveEnvelope.ObjectsKey, out JsonElement objects) &&
                   objects.ValueKind == JsonValueKind.Object
                ? Compute(objects)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Whether a stored checksum agrees with a computed one. An absent stored checksum
    /// (every save written before format 4) is accepted: there is nothing to disagree with.</summary>
    public static bool Accepts(string? stored, string computed) =>
        string.IsNullOrEmpty(stored) || string.Equals(stored, computed, StringComparison.Ordinal);

    private static void Write(JsonElement element, ArrayBufferWriter<byte> buffer)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var properties = new List<JsonProperty>();
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    properties.Add(property);
                }

                properties.Sort(static (a, b) => string.CompareOrdinal(a.Name, b.Name));
                Tag(buffer, '{', properties.Count);
                foreach (JsonProperty property in properties)
                {
                    Text(buffer, property.Name);
                    Write(property.Value, buffer);
                }

                break;
            case JsonValueKind.Array:
                Tag(buffer, '[', element.GetArrayLength());
                foreach (JsonElement item in element.EnumerateArray())
                {
                    Write(item, buffer);
                }

                break;
            case JsonValueKind.String:
                Text(buffer, element.GetString() ?? string.Empty);
                break;
            case JsonValueKind.Number:
                Raw(buffer, "n" + Number(element) + ";");
                break;
            case JsonValueKind.True:
                Raw(buffer, "t");
                break;
            case JsonValueKind.False:
                Raw(buffer, "f");
                break;
            default:
                Raw(buffer, "z");
                break;
        }
    }

    // ⚠️ A number is hashed by VALUE, not by its token. The engine's JSON parser does not promise
    // to hand back the type it was given (50 can come back as 50.0), so a tool that reads a save
    // and writes it out again, changing nothing, would otherwise break the checksum of content it
    // never touched. 50, 50.0 and 5e1 are one number here; 50 and 50.5 are not.
    private static string Number(JsonElement element)
    {
        // An integer at or past 2^53 is hashed as the double it becomes, not by its digits: the
        // engine's parser returns every number as a float, so 8123456789012345678 comes back from
        // a tool's read-and-rewrite as 8123456789012345900.0 and has to hash the same. Compared as
        // a double because Math.Abs(long.MinValue) throws.
        if (element.TryGetInt64(out long whole))
        {
            double asDouble = whole;
            return Math.Abs(asDouble) < 9007199254740992d
                ? whole.ToString(CultureInfo.InvariantCulture)
                : asDouble.ToString("G15", CultureInfo.InvariantCulture);
        }

        if (!element.TryGetDouble(out double value))
        {
            return element.GetRawText();
        }

        // 2^53: below it every whole double is an exact integer and prints as one. A fraction is
        // taken to 15 significant digits, the most a decimal can carry through a double and back
        // unchanged, so a parser that lands one unit in the last place away from another still
        // hashes the same number.
        return value == Math.Truncate(value) && Math.Abs(value) < 9007199254740992d
            ? ((long)value).ToString(CultureInfo.InvariantCulture)
            : value.ToString("G15", CultureInfo.InvariantCulture);
    }

    // Counts and lengths are part of the stream so two different documents can never flatten to the
    // same bytes ("ab" + "c" versus "a" + "bc").
    private static void Tag(ArrayBufferWriter<byte> buffer, char kind, int count) =>
        Raw(buffer, kind + count.ToString(CultureInfo.InvariantCulture) + ":");

    private static void Text(ArrayBufferWriter<byte> buffer, string value)
    {
        Raw(buffer, "s" + Encoding.UTF8.GetByteCount(value).ToString(CultureInfo.InvariantCulture) + ":");
        Raw(buffer, value);
    }

    private static void Raw(ArrayBufferWriter<byte> buffer, string value) => Encoding.UTF8.GetBytes(value, buffer);
}
