using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using Embervale.Bootstrap;

namespace Embervale.Debugging;

/// <summary>
/// One requested picture for <c>--shot</c>: where to stand, where to look, and under what sky; or a
/// UI state or a spell phase another harness already knows how to stage. Read from command-line
/// flags or from JSON; this class only parses and checks, so it is unit-tested without the engine.
///
/// <para><b>Flags</b> (after <c>-- --shot</c>): <c>--name</c>, a place (<c>--at=x,z</c> or
/// <c>x,y,z</c>, <c>--cell=id</c>, <c>--location=id</c>, with <c>--region=id</c> when the place does
/// not imply one), <c>--yaw</c> (degrees, 0 looks north along −Z, 90 east), <c>--pitch</c> (degrees,
/// negative looks down), <c>--distance</c> (metres the camera stands back from the place; 0 puts the
/// eye on it), <c>--height</c> (metres above the ground the camera looks at), <c>--fov</c>,
/// <c>--hour</c>, <c>--weather=id</c>, <c>--view=free|fp|tp</c>, <c>--hud</c>, <c>--show-player</c>,
/// <c>--settle=frames</c>, <c>--timeout=seconds</c>; or <c>--ui=suite/shot[,shot]</c>; or
/// <c>--spell=id</c> with <c>--phase=windup|release|impact|linger|all</c>.</para>
///
/// <para><b>JSON</b> (<c>--shot=path.json</c>, or the text itself): one object, an array of them, or
/// <c>{"shots":[...]}</c>, with the flag names as keys (<c>hide-hud</c>, <c>hide_hud</c> and
/// <c>hideHud</c> are the same key).</para>
/// </summary>
public sealed class OneShotSpec
{
    public static readonly string[] Views = { "free", "fp", "tp" };
    public static readonly string[] Phases = { "windup", "release", "impact", "linger", "all" };

    public string Name { get; set; } = "shot";

    public string? Region { get; set; }

    public string? Cell { get; set; }

    public string? Location { get; set; }

    /// <summary>World X,Z, or X,Y,Z; null when the place is a cell, a location or the region's spawn.</summary>
    public float[]? At { get; set; }

    public float Yaw { get; set; }

    public float Pitch { get; set; } = -10f;

    public float Distance { get; set; } = 12f;

    public float Height { get; set; } = 1.6f;

    public float Fov { get; set; } = 70f;

    public float? Hour { get; set; }

    public string? Weather { get; set; }

    public string View { get; set; } = "free";

    public bool Hud { get; set; }

    public bool ShowPlayer { get; set; }

    public int Settle { get; set; } = 45;

    public float Timeout { get; set; } = 60f;

    /// <summary><c>suite/shot[,shot]</c>: the suite's flag without its dashes, and shot patterns.</summary>
    public string? Ui { get; set; }

    public string? Spell { get; set; }

    public string Phase { get; set; } = "impact";

    public bool IsUi => !string.IsNullOrEmpty(Ui);

    public bool IsSpell => !string.IsNullOrEmpty(Spell);

    public bool IsWorld => !IsUi && !IsSpell;

    /// <summary>The suite a <see cref="Ui"/> request names, as a flag (<c>--panelshots</c>).</summary>
    public string UiSuiteFlag => "--" + (Ui ?? string.Empty).Split('/')[0].Trim().TrimStart('-');

    /// <summary>The shot patterns a <see cref="Ui"/> request names.</summary>
    public string[] UiShots
    {
        get
        {
            string text = Ui ?? string.Empty;
            int slash = text.IndexOf('/');
            return slash < 0
                ? Array.Empty<string>()
                : text[(slash + 1)..].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
    }

    /// <summary>The unit vector the camera looks along: yaw 0 is north (−Z), 90 is east (+X);
    /// a negative pitch looks down. Pitch is kept inside ±89° so the view always has an up.</summary>
    public (float X, float Y, float Z) Forward()
    {
        double yaw = Yaw * Math.PI / 180.0;
        double pitch = Math.Clamp(Pitch, -89f, 89f) * Math.PI / 180.0;
        return ((float)(Math.Sin(yaw) * Math.Cos(pitch)), (float)Math.Sin(pitch), (float)(-Math.Cos(yaw) * Math.Cos(pitch)));
    }

    /// <summary>Where the camera stands relative to the point it looks at.</summary>
    public (float X, float Y, float Z) CameraOffset()
    {
        (float x, float y, float z) = Forward();
        return (-x * Distance, -y * Distance, -z * Distance);
    }

    // The first thing the request said that could not be understood: an unknown key, a number
    // that is not one. Validate reports it, so a typo never becomes an 'ok' picture of the default.
    private string? _unreadable;

    /// <summary>What is wrong with this request, or null.</summary>
    public string? Validate()
    {
        if (_unreadable != null)
        {
            return _unreadable;
        }

        if (Name.Length == 0 || Name.IndexOfAny(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' }) >= 0)
        {
            return $"name '{Name}' is not usable as a file name";
        }

        if (IsUi && IsSpell)
        {
            return "ui and spell are separate requests; give one";
        }

        if (IsUi)
        {
            return Ui!.Contains('/') && UiShots.Length > 0 && UiSuiteFlag.Length > 2
                ? null
                : $"ui '{Ui}' must be suite/shot, e.g. panelshots/00-map";
        }

        if (IsSpell)
        {
            return Array.IndexOf(Phases, Phase) >= 0 ? null : $"phase '{Phase}' is not one of {string.Join("|", Phases)}";
        }

        if (Array.IndexOf(Views, View) < 0)
        {
            return $"view '{View}' is not one of {string.Join("|", Views)}";
        }

        if (At != null && At.Length != 2 && At.Length != 3)
        {
            return "at needs x,z or x,y,z";
        }

        if (At != null && Array.Exists(At, value => !float.IsFinite(value)))
        {
            return "at holds a value that is not a number";
        }

        if (Hour is { } hour && (hour < 0f || hour > 24f))
        {
            return $"hour {hour.ToString(CultureInfo.InvariantCulture)} is outside 0..24";
        }

        if (Distance < 0f || Distance > 2000f || Fov < 10f || Fov > 150f || Settle < 1 || Timeout <= 0f)
        {
            return "distance must be 0..2000, fov 10..150, settle at least 1 and timeout above 0";
        }

        return null;
    }

    // --- flags --------------------------------------------------------------------------------------

    /// <summary>The one request the flags after <c>--shot</c> describe.</summary>
    public static OneShotSpec FromArgs(CommandLineArgs args)
    {
        var spec = new OneShotSpec();
        foreach (string key in Keys)
        {
            string flag = "--" + key;
            if (!args.Has(flag))
            {
                continue;
            }

            string? value = args.Value(flag);
            spec.Set(Normal(key), value ?? "true");
        }

        return spec;
    }

    private static readonly string[] Keys =
    {
        "name", "region", "cell", "location", "at", "yaw", "pitch", "distance", "height", "fov", "hour",
        "weather", "view", "hud", "show-player", "settle", "timeout", "ui", "spell", "phase",
    };

    // --- JSON ---------------------------------------------------------------------------------------

    /// <summary>The requests in a JSON text: an object, an array of objects, or an object with a
    /// <c>shots</c> array. Returns an empty list and sets <paramref name="error"/> when it is not that.</summary>
    public static List<OneShotSpec> FromJson(string json, out string? error)
    {
        var specs = new List<OneShotSpec>();
        error = null;
        try
        {
            using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });
            JsonElement root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("shots", out JsonElement list))
            {
                root = list;
            }

            if (root.ValueKind == JsonValueKind.Object)
            {
                specs.Add(FromElement(root));
            }
            else if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement element in root.EnumerateArray())
                {
                    if (element.ValueKind != JsonValueKind.Object)
                    {
                        error = "every entry of a shot list must be an object";
                        return new List<OneShotSpec>();
                    }

                    specs.Add(FromElement(element));
                }
            }
            else
            {
                error = "a shot spec is an object, an array of objects, or {\"shots\": [...]}";
            }
        }
        catch (JsonException e)
        {
            error = "the shot spec is not valid JSON: " + e.Message;
            return new List<OneShotSpec>();
        }

        if (error == null && specs.Count == 0)
        {
            error = "the shot spec holds no shots";
        }

        // Several shots in one file each need their own file name.
        for (int i = 0; i < specs.Count; i++)
        {
            if (specs.Count > 1 && specs[i].Name == "shot")
            {
                specs[i].Name = $"shot_{i + 1:00}";
            }
        }

        return specs;
    }

    private static OneShotSpec FromElement(JsonElement element)
    {
        var spec = new OneShotSpec();
        foreach (JsonProperty property in element.EnumerateObject())
        {
            string value = property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString() ?? string.Empty,
                JsonValueKind.Array => JoinArray(property.Value),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => property.Value.GetRawText(),
            };
            spec.Set(Normal(property.Name), value);
        }

        return spec;
    }

    private static string JoinArray(JsonElement array)
    {
        var parts = new List<string>();
        foreach (JsonElement item in array.EnumerateArray())
        {
            parts.Add(item.ValueKind == JsonValueKind.String ? item.GetString() ?? string.Empty : item.GetRawText());
        }

        return string.Join(",", parts);
    }

    private static string Normal(string key) =>
        key.Replace("-", string.Empty).Replace("_", string.Empty).ToLowerInvariant();

    private void Set(string key, string value)
    {
        switch (key)
        {
            case "name": Name = value.Trim(); break;
            case "region": Region = Text(value); break;
            case "cell": Cell = Text(value); break;
            case "location": Location = Text(value); break;
            case "at": At = Numbers(value); break;
            case "yaw": Yaw = Number(key, value, Yaw); break;
            case "pitch": Pitch = Number(key, value, Pitch); break;
            case "distance": Distance = Number(key, value, Distance); break;
            case "height": Height = Number(key, value, Height); break;
            case "fov": Fov = Number(key, value, Fov); break;
            case "hour":
                float hour = Number(key, value, float.NaN);
                Hour = float.IsNaN(hour) ? null : hour;
                break;
            case "weather": Weather = Text(value); break;
            case "view": View = value.Trim().ToLowerInvariant(); break;
            case "hud": Hud = Truth(value); break;
            case "hidehud": Hud = !Truth(value); break;
            case "showplayer": ShowPlayer = Truth(value); break;
            case "settle": Settle = (int)Number(key, value, Settle); break;
            case "timeout": Timeout = Number(key, value, Timeout); break;
            case "ui": Ui = Text(value); break;
            case "spell": Spell = Text(value); break;
            case "phase": Phase = value.Trim().ToLowerInvariant(); break;
            default:
                _unreadable ??= $"unknown key '{key}' (known: {string.Join(", ", Keys)}, hide-hud)";
                break;
        }
    }

    private float Number(string key, string value, float fallback)
    {
        if (float.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed) && float.IsFinite(parsed))
        {
            return parsed;
        }

        _unreadable ??= $"{key} '{value.Trim()}' is not a number";
        return fallback;
    }

    private static string? Text(string value) => value.Trim().Length == 0 ? null : value.Trim();

    private static bool Truth(string value) => value.Trim().ToLowerInvariant() is "true" or "1" or "yes" or "";

    private static float Number(string value, float fallback) =>
        float.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed) && float.IsFinite(parsed)
            ? parsed
            : fallback;

    private static float[] Numbers(string value)
    {
        string[] parts = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var numbers = new float[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            numbers[i] = Number(parts[i], float.NaN);
        }

        return numbers;
    }
}
