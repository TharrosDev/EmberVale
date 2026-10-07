using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Embervale.Debugging;

/// <summary>One broken invariant: a stable dotted code a tool can match on, and the sentence a
/// person reads.</summary>
public readonly record struct IntegrityIssue(string Code, string Message);

/// <summary>
/// What one pass of the <see cref="WorldIntegrityChecker"/> found, as data: a list of issues with
/// codes, a text form for the dev console and a JSON form for tools. Pure, so the two forms are
/// unit-tested.
/// </summary>
public sealed class IntegrityReport
{
    private readonly List<IntegrityIssue> _issues = new();

    public IReadOnlyList<IntegrityIssue> Issues => _issues;

    public bool Ok => _issues.Count == 0;

    public void Add(string code, string message) => _issues.Add(new IntegrityIssue(code, message));

    /// <summary>The issue codes, in the order found.</summary>
    public IEnumerable<string> Codes()
    {
        foreach (IntegrityIssue issue in _issues)
        {
            yield return issue.Code;
        }
    }

    /// <summary><c>Integrity OK.</c>, or a count and one bullet per issue.</summary>
    public string ToText()
    {
        if (Ok)
        {
            return "Integrity OK.";
        }

        var sb = new StringBuilder($"Integrity: {_issues.Count} issue(s).");
        foreach (IntegrityIssue issue in _issues)
        {
            sb.Append("\n• ").Append(issue.Code).Append(": ").Append(issue.Message);
        }

        return sb.ToString();
    }

    /// <summary>One line: <c>{"ok":bool,"issues":[{"code":"…","msg":"…"}]}</c>.</summary>
    public string ToJson()
    {
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream))
        {
            json.WriteStartObject();
            json.WriteBoolean("ok", Ok);
            json.WriteStartArray("issues");
            foreach (IntegrityIssue issue in _issues)
            {
                json.WriteStartObject();
                json.WriteString("code", issue.Code);
                json.WriteString("msg", issue.Message);
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
