using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Embervale.Debugging;

/// <summary>
/// One <c>[node]</c> block of a <c>.tscn</c>, reduced to what the content validator asks of it: its name,
/// type, parent, the script it carries (resolved through the scene's <c>ext_resource</c> lines) and its
/// raw <c>Key = value</c> property lines. Pure text, no Godot types, so the scene arms are unit-tested.
/// </summary>
public sealed record SceneNodeText(
    string Name, string Type, string Parent, string ScriptPath, IReadOnlyDictionary<string, string> Props)
{
    public bool Has(string key) => Props.ContainsKey(key);

    /// <summary>A string property, unquoted; <paramref name="fallback"/> when the scene does not set it
    /// (the component's own default then applies).</summary>
    public string Str(string key, string fallback = "") =>
        Props.TryGetValue(key, out string? raw) && TryUnquote(raw, out string value) ? value : fallback;

    public int Int(string key, int fallback) =>
        Props.TryGetValue(key, out string? raw) && int.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v)
            ? v
            : fallback;

    public float Float(string key, float fallback) =>
        Props.TryGetValue(key, out string? raw) && float.TryParse(raw.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float v)
            ? v
            : fallback;

    /// <summary>The strings of a <c>PackedStringArray("a", "b")</c> (or an <c>Array[String]</c>) property.</summary>
    public IReadOnlyList<string> StrList(string key, IReadOnlyList<string> fallback)
    {
        if (!Props.TryGetValue(key, out string? raw))
        {
            return fallback;
        }

        var items = new List<string>();
        foreach (Match match in Regex.Matches(raw, "\"((?:[^\"\\\\]|\\\\.)*)\""))
        {
            items.Add(Regex.Unescape(match.Groups[1].Value));
        }

        return items;
    }

    /// <summary>A <c>Vector3(x, y, z)</c> property.</summary>
    public (float X, float Y, float Z) Vector3(string key, (float X, float Y, float Z) fallback)
    {
        if (Props.TryGetValue(key, out string? raw))
        {
            int open = raw.IndexOf('(');
            MatchCollection numbers = Regex.Matches(
                open >= 0 ? raw[(open + 1)..] : raw, @"-?\d+(?:\.\d+)?(?:e-?\d+)?");
            if (numbers.Count == 3 &&
                float.TryParse(numbers[0].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float x) &&
                float.TryParse(numbers[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float y) &&
                float.TryParse(numbers[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
            {
                return (x, y, z);
            }
        }

        return fallback;
    }

    private static bool TryUnquote(string raw, out string value)
    {
        string trimmed = raw.Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
        {
            value = Regex.Unescape(trimmed[1..^1]);
            return true;
        }

        value = string.Empty;
        return false;
    }
}

/// <summary>Extracts <see cref="SceneNodeText"/> blocks from <c>.tscn</c> text.</summary>
public static class SceneTextParser
{
    private static readonly Regex Header = new(@"^\[(\w+)\s*(.*)\]\s*$", RegexOptions.Compiled);
    private static readonly Regex Attribute = new(@"(\w+)=(""(?:[^""\\]|\\.)*""|[^\s\]]+)", RegexOptions.Compiled);
    private static readonly Regex ScriptRef = new(@"^ExtResource\(""([^""]+)""\)$", RegexOptions.Compiled);

    public static List<SceneNodeText> Nodes(string sceneText)
    {
        var scripts = new Dictionary<string, string>(StringComparer.Ordinal);
        var nodes = new List<SceneNodeText>();

        string name = string.Empty, type = string.Empty, parent = string.Empty;
        Dictionary<string, string>? props = null;

        void Flush()
        {
            if (props == null)
            {
                return;
            }

            string script = string.Empty;
            if (props.TryGetValue("script", out string? rawScript) &&
                ScriptRef.Match(rawScript.Trim()) is { Success: true } reference &&
                scripts.TryGetValue(reference.Groups[1].Value, out string? path))
            {
                script = path;
            }

            nodes.Add(new SceneNodeText(name, type, parent, script, props));
            props = null;
        }

        foreach (string rawLine in sceneText.Replace("\r\n", "\n").Split('\n'))
        {
            string line = rawLine.TrimEnd();
            if (line.Length == 0 || line.StartsWith(';'))
            {
                continue;
            }

            Match header = Header.Match(line);
            if (header.Success)
            {
                Flush();
                string kind = header.Groups[1].Value;
                Dictionary<string, string> attributes = new();
                foreach (Match attribute in Attribute.Matches(header.Groups[2].Value))
                {
                    string value = attribute.Groups[2].Value;
                    attributes[attribute.Groups[1].Value] =
                        value.Length >= 2 && value[0] == '"' ? Regex.Unescape(value[1..^1]) : value;
                }

                if (kind == "ext_resource" && attributes.TryGetValue("id", out string? id) &&
                    attributes.TryGetValue("path", out string? scriptPath) &&
                    attributes.TryGetValue("type", out string? resourceType) && resourceType == "Script")
                {
                    scripts[id] = scriptPath;
                }
                else if (kind == "node")
                {
                    name = attributes.GetValueOrDefault("name", string.Empty);
                    type = attributes.GetValueOrDefault("type", string.Empty);
                    parent = attributes.GetValueOrDefault("parent", string.Empty);
                    props = new Dictionary<string, string>(StringComparer.Ordinal);
                }

                continue;
            }

            if (props != null)
            {
                int equals = line.IndexOf(" = ", StringComparison.Ordinal);
                if (equals > 0 && !char.IsWhiteSpace(line[0]))
                {
                    props[line[..equals]] = line[(equals + 3)..];
                }
            }
        }

        Flush();
        return nodes;
    }
}
