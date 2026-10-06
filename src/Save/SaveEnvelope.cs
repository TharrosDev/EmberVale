using System;
using System.Text.Json;

namespace Embervale.Save;

/// <summary>Why an envelope cannot be loaded. Runtime only: never saved.</summary>
public enum SaveEnvelopeFault
{
    /// <summary>Loadable (an older format the migration chain covers included).</summary>
    None,

    /// <summary>There is no file.</summary>
    Missing,

    /// <summary>Not JSON, or JSON that is not an object.</summary>
    NotAnObject,

    /// <summary>No numeric <c>version</c>: not one of this game's saves.</summary>
    NoVersion,

    /// <summary>A fractional, non-finite or out-of-range <c>version</c>.</summary>
    InvalidVersion,

    /// <summary>Written by a later format than this build knows.</summary>
    Newer,

    /// <summary>Below the first format the game ever wrote; no migration covers it.</summary>
    TooOld,

    /// <summary>No <c>objects</c> object.</summary>
    NoObjects,

    /// <summary>An <c>objects</c> entry that is not an object (<see cref="SaveEnvelope.FaultDetail"/> names it).</summary>
    BadEntry,

    /// <summary>The stored checksum disagrees with the content.</summary>
    ChecksumMismatch,
}

/// <summary>
/// The result of validating a save envelope <b>without applying it</b>: parse, shape, version and
/// checksum, in the order a load refuses them. It is the one place those rules live;
/// <see cref="SaveManager.LoadGame"/> and <see cref="SaveManager.InspectSlot"/> both ask it, so the
/// slot browser can never call a slot healthy that a load would refuse. Godot-free: it reads the
/// file's text with System.Text.Json, so xUnit runs every refusal.
/// </summary>
public sealed class SaveEnvelope
{
    /// <summary>The format this build writes. A bump needs a step in <see cref="SaveMigrations"/>.</summary>
    public const int CurrentVersion = 4;

    /// <summary>The first format the game ever wrote; nothing older is a real save.</summary>
    public const int FirstVersion = 1;

    public const string VersionKey = "version";
    public const string TimestampKey = "timestamp";
    public const string HeaderKey = "header";
    public const string ObjectsKey = "objects";
    public const string ChecksumKey = "checksum";

    /// <summary>Lenient on purpose (trailing commas, comments): a hand-edited save the engine's
    /// own parser would take must not be called corrupt here first.</summary>
    internal static readonly JsonDocumentOptions ParseOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
        MaxDepth = 256,
    };

    private SaveEnvelope()
    {
    }

    public SaveEnvelopeFault Fault { get; private set; }

    /// <summary>The offending entry key for <see cref="SaveEnvelopeFault.BadEntry"/>, else empty.</summary>
    public string FaultDetail { get; private set; } = string.Empty;

    /// <summary>The declared format version; 0 when the document did not yield a valid one.</summary>
    public int Version { get; private set; }

    /// <summary>The checksum the file carries; empty when it has none (every pre-v4 save).</summary>
    public string StoredChecksum { get; private set; } = string.Empty;

    /// <summary>The checksum of the file's actual <c>objects</c>; empty when there were none to hash.</summary>
    public string ComputedChecksum { get; private set; } = string.Empty;

    /// <summary>The envelope's own header as JSON text, readable even when the save is refused;
    /// empty when the document has no header object.</summary>
    public string HeaderJson { get; private set; } = string.Empty;

    /// <summary>The envelope's top-level timestamp (Unix seconds), 0 when absent.</summary>
    public double Timestamp { get; private set; }

    /// <summary>How many entries <c>objects</c> holds.</summary>
    public int ObjectCount { get; private set; }

    public bool IsLoadable => Fault == SaveEnvelopeFault.None;

    /// <summary>Loadable, but only after <see cref="SaveMigrations"/> has walked it forward.</summary>
    public bool NeedsMigration => IsLoadable && Version < CurrentVersion;

    /// <summary>The fault as the slot browser's four-way answer.</summary>
    public SaveHealth Health => Fault switch
    {
        SaveEnvelopeFault.None => SaveHealth.Ok,
        SaveEnvelopeFault.Missing => SaveHealth.Missing,
        SaveEnvelopeFault.Newer => SaveHealth.Newer,
        _ => SaveHealth.Corrupt,
    };

    /// <summary>Validates a save document. <paramref name="json"/> null means the file is absent.
    /// Never throws.</summary>
    public static SaveEnvelope Read(string? json)
    {
        var envelope = new SaveEnvelope();
        if (json == null)
        {
            envelope.Fault = SaveEnvelopeFault.Missing;
            return envelope;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(json, ParseOptions);
            envelope.Fault = envelope.Inspect(document.RootElement);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or ArgumentException)
        {
            envelope.Fault = SaveEnvelopeFault.NotAnObject;
        }

        return envelope;
    }

    private SaveEnvelopeFault Inspect(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return SaveEnvelopeFault.NotAnObject;
        }

        // Read what describes the save before judging it: a refused slot still shows whose it was.
        if (root.TryGetProperty(HeaderKey, out JsonElement header) && header.ValueKind == JsonValueKind.Object)
        {
            HeaderJson = header.GetRawText();
        }

        if (root.TryGetProperty(TimestampKey, out JsonElement timestamp) &&
            timestamp.ValueKind == JsonValueKind.Number && timestamp.TryGetDouble(out double seconds) &&
            double.IsFinite(seconds))
        {
            Timestamp = seconds;
        }

        // A missing "version" is not an old save, it is not one of ours.
        if (!root.TryGetProperty(VersionKey, out JsonElement versionElement) ||
            versionElement.ValueKind != JsonValueKind.Number)
        {
            return SaveEnvelopeFault.NoVersion;
        }

        if (!versionElement.TryGetDouble(out double rawVersion) || !double.IsFinite(rawVersion) ||
            rawVersion != Math.Truncate(rawVersion) || rawVersion < int.MinValue || rawVersion > int.MaxValue)
        {
            return SaveEnvelopeFault.InvalidVersion;
        }

        Version = (int)rawVersion;
        if (Version > CurrentVersion)
        {
            return SaveEnvelopeFault.Newer;
        }

        if (Version < FirstVersion)
        {
            return SaveEnvelopeFault.TooOld;
        }

        if (!root.TryGetProperty(ObjectsKey, out JsonElement objects) || objects.ValueKind != JsonValueKind.Object)
        {
            return SaveEnvelopeFault.NoObjects;
        }

        foreach (JsonProperty entry in objects.EnumerateObject())
        {
            ObjectCount++;
            if (entry.Value.ValueKind != JsonValueKind.Object)
            {
                FaultDetail = entry.Name;
                return SaveEnvelopeFault.BadEntry;
            }
        }

        ComputedChecksum = SaveChecksum.Compute(objects);
        if (root.TryGetProperty(ChecksumKey, out JsonElement checksum))
        {
            // A checksum that is present but not a string is a damaged one, not an absent one.
            StoredChecksum = checksum.ValueKind == JsonValueKind.String
                ? checksum.GetString() ?? string.Empty
                : checksum.GetRawText();
            if (!SaveChecksum.Accepts(StoredChecksum, ComputedChecksum))
            {
                return SaveEnvelopeFault.ChecksumMismatch;
            }
        }

        return SaveEnvelopeFault.None;
    }
}
