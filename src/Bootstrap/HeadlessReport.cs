using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Embervale.Bootstrap;

/// <summary>
/// What a headless gate found, as one machine-readable line.
///
/// <para>A gate collects facts, failures and warnings here and ends with <see cref="Finish"/>, which
/// prints exactly one line, <c>EMBERVALE_RESULT {json}</c>, writes the same JSON to
/// <c>--report=&lt;path&gt;</c> when that was given, and returns the exit code (0 pass, 1 fail). A
/// caller reads that line instead of the gate's prose.</para>
///
/// <para>This half is pure (no engine calls) so the JSON is unit-tested; the half that reads the
/// command line and prints is <c>HeadlessReport.Godot.cs</c>.</para>
/// </summary>
public sealed partial class HeadlessReport
{
    /// <summary>The prefix of the machine line. The JSON follows after one space.</summary>
    public const string LinePrefix = "EMBERVALE_RESULT";

    /// <summary>Bumped when the shape of the JSON changes.</summary>
    public const int Schema = 1;

    private readonly List<KeyValuePair<string, object?>> _facts = new();
    private readonly List<string> _failures = new();
    private readonly List<string> _warnings = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    /// <param name="gate">The gate's name without dashes, e.g. <c>state</c> for <c>--state</c>.</param>
    public HeadlessReport(string gate)
    {
        Gate = gate;
    }

    public string Gate { get; }

    public IReadOnlyList<string> Failures => _failures;

    public IReadOnlyList<string> Warnings => _warnings;

    public bool Passed => _failures.Count == 0;

    /// <summary>0 when nothing failed, 1 when a check failed, 2 when the gate could not run at all
    /// (<see cref="Refuse"/>): "could not check" must never read as "checked and broken".</summary>
    public int ExitCode => _refused ? RefusedExitCode : Passed ? 0 : 1;

    /// <summary>The exit code of a run that was refused: a bad flag, a missing prerequisite.</summary>
    public const int RefusedExitCode = 2;

    private bool _refused;

    /// <summary>Records why the gate could not run. It is a failure, and the exit code becomes 2.</summary>
    public void Refuse(string message)
    {
        _refused = true;
        Fail(message);
    }

    /// <summary>Records one fact. A string, bool or number is written as itself, a sequence of
    /// strings as an array, a dictionary with string keys as an object and any other sequence as an
    /// array (both nest), anything else through <c>ToString</c>. Setting a key again replaces it
    /// in place.</summary>
    public HeadlessReport Fact(string key, object? value)
    {
        for (int i = 0; i < _facts.Count; i++)
        {
            if (_facts[i].Key == key)
            {
                _facts[i] = new KeyValuePair<string, object?>(key, value);
                return this;
            }
        }

        _facts.Add(new KeyValuePair<string, object?>(key, value));
        return this;
    }

    /// <summary>Records a failure; the gate exits 1. A repeated message is recorded once.</summary>
    public void Fail(string message)
    {
        if (!_failures.Contains(message))
        {
            _failures.Add(message);
        }
    }

    /// <summary>Records a warning; it does not change the exit code.</summary>
    public void Warn(string message)
    {
        if (!_warnings.Contains(message))
        {
            _warnings.Add(message);
        }
    }

    /// <summary>Fails with <paramref name="failure"/> when <paramref name="ok"/> is false.</summary>
    public bool Check(bool ok, string failure)
    {
        if (!ok)
        {
            Fail(failure);
        }

        return ok;
    }

    /// <summary>The report as one line of JSON:
    /// <c>{"schema","gate","ok","exit_code","elapsed_ms","facts":{},"failures":[],"warnings":[]}</c>.</summary>
    public string ToJson(long elapsedMs)
    {
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream))
        {
            json.WriteStartObject();
            json.WriteNumber("schema", Schema);
            json.WriteString("gate", Gate);
            json.WriteBoolean("ok", Passed);
            json.WriteNumber("exit_code", ExitCode);
            json.WriteNumber("elapsed_ms", elapsedMs);

            json.WriteStartObject("facts");
            foreach (KeyValuePair<string, object?> fact in _facts)
            {
                json.WritePropertyName(fact.Key);
                WriteValue(json, fact.Value);
            }

            json.WriteEndObject();

            WriteStrings(json, "failures", _failures);
            WriteStrings(json, "warnings", _warnings);
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Prints the machine line through <paramref name="writeLine"/>, writes the JSON to
    /// <paramref name="reportPath"/> when one is given, and returns the exit code. A report file
    /// that cannot be written is a failure: the caller asked for it and would otherwise read a
    /// stale one.</summary>
    public int Emit(Action<string> writeLine, string? reportPath)
    {
        long elapsed = _clock.ElapsedMilliseconds;
        if (!string.IsNullOrEmpty(reportPath))
        {
            try
            {
                string? directory = Path.GetDirectoryName(Path.GetFullPath(reportPath));
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(reportPath, ToJson(elapsed));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or
                                          ArgumentException or NotSupportedException)
            {
                Fail($"could not write the report to '{reportPath}': {error.Message}");
            }
        }

        writeLine($"{LinePrefix} {ToJson(elapsed)}");
        return ExitCode;
    }

    private static void WriteStrings(Utf8JsonWriter json, string name, List<string> values)
    {
        json.WriteStartArray(name);
        foreach (string value in values)
        {
            json.WriteStringValue(value);
        }

        json.WriteEndArray();
    }

    private static void WriteValue(Utf8JsonWriter json, object? value)
    {
        switch (value)
        {
            case null:
                json.WriteNullValue();
                break;
            case string text:
                json.WriteStringValue(text);
                break;
            case bool flag:
                json.WriteBooleanValue(flag);
                break;
            case int or long or short or byte or uint:
                json.WriteNumberValue(Convert.ToInt64(value));
                break;
            case ulong big:
                json.WriteNumberValue(big);
                break;
            case float or double or decimal:
                double number = Convert.ToDouble(value);
                if (double.IsFinite(number))
                {
                    json.WriteNumberValue(number);
                }
                else
                {
                    // JSON has no NaN or infinity; a string keeps the line parseable.
                    json.WriteStringValue(number.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }

                break;
            case IEnumerable<string> list:
                json.WriteStartArray();
                foreach (string item in list)
                {
                    json.WriteStringValue(item);
                }

                json.WriteEndArray();
                break;
            case System.Collections.IDictionary map:
                json.WriteStartObject();
                foreach (System.Collections.DictionaryEntry entry in map)
                {
                    json.WritePropertyName(entry.Key.ToString() ?? string.Empty);
                    WriteValue(json, entry.Value);
                }

                json.WriteEndObject();
                break;
            case System.Collections.IEnumerable sequence:
                json.WriteStartArray();
                foreach (object? item in sequence)
                {
                    WriteValue(json, item);
                }

                json.WriteEndArray();
                break;
            default:
                json.WriteStringValue(value.ToString());
                break;
        }
    }
}
