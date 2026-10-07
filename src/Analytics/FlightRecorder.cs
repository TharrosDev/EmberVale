using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Embervale.Analytics;

/// <summary>
/// A bounded ring of the most recent things that happened: analytics rows, hits on or by the
/// player, logged warnings and errors, and dev-console commands. It costs one slot write per note
/// and is only ever read when something has gone wrong: the <see cref="AnalyticsSink"/> dumps it to
/// a file on the first invariant violation of a session, so the bug report already holds the
/// seconds that led up to it.
///
/// <para>Thread-safe (a warning can be logged from a worker thread) and pure: no engine calls.</para>
/// </summary>
public sealed class FlightRecorder
{
    public const int DefaultCapacity = 256;

    /// <summary>The process-wide recorder. Anything may note into it; nothing is written until a dump.</summary>
    public static FlightRecorder Shared { get; } = new(DefaultCapacity);

    private readonly (long Ms, string Kind, string Detail)[] _ring;
    private readonly object _gate = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private int _next;
    private int _count;

    public FlightRecorder(int capacity)
    {
        _ring = new (long, string, string)[Math.Max(1, capacity)];
    }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _count;
            }
        }
    }

    /// <summary>Records one thing. <paramref name="kind"/> is a short tag (<c>row</c>, <c>hit</c>,
    /// <c>warn</c>, <c>error</c>, <c>cmd</c>); <paramref name="detail"/> is free text.</summary>
    public void Note(string kind, string detail)
    {
        lock (_gate)
        {
            _ring[_next] = (_clock.ElapsedMilliseconds, kind, detail);
            _next = (_next + 1) % _ring.Length;
            if (_count < _ring.Length)
            {
                _count++;
            }
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _next = 0;
            _count = 0;
        }
    }

    /// <summary>The notes, oldest first, one JSON object per line:
    /// <c>{"ms":1234,"kind":"cmd","detail":"spawn 6"}</c>. <c>ms</c> counts from process start.</summary>
    public List<string> Lines()
    {
        var lines = new List<string>();
        lock (_gate)
        {
            int start = (_next - _count + _ring.Length) % _ring.Length;
            for (int i = 0; i < _count; i++)
            {
                (long ms, string kind, string detail) = _ring[(start + i) % _ring.Length];
                lines.Add(Line(ms, kind, detail));
            }
        }

        return lines;
    }

    /// <summary>Writes a header line (<c>kind</c> = <c>dump</c>, with the reason) and then every
    /// note to <paramref name="path"/>, creating its directory. Returns false, never throws, when
    /// the file cannot be written: a recorder that fails must not take the session with it.</summary>
    public bool Dump(string path, string reason)
    {
        try
        {
            string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            List<string> lines = Lines();
            lines.Insert(0, Line(_clock.ElapsedMilliseconds, "dump", reason));
            File.WriteAllLines(path, lines, new UTF8Encoding(false));
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
                                      ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    private static string Line(long ms, string kind, string detail)
    {
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream))
        {
            json.WriteStartObject();
            json.WriteNumber("ms", ms);
            json.WriteString("kind", kind);
            json.WriteString("detail", detail);
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
