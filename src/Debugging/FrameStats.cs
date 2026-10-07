using System;
using System.Collections.Generic;
using Embervale.World;

namespace Embervale.Debugging;

/// <summary>One slow frame: when it happened in the sampled window, how long it took, and what
/// else happened on it (<c>cell-load</c>, <c>gc2</c>, or empty when nothing was seen).</summary>
public readonly record struct FrameHitch(double AtSeconds, double Milliseconds, string Cause);

/// <summary>
/// The frame-time record of one measured run: every frame's duration, how many were hitches, and
/// the worst few with their cause. Pure (no engine calls), so the arithmetic a run report prints is
/// unit-tested; the percentiles are <see cref="WorldPerformanceRules.Distribution"/>, the rule the
/// world monitor already uses.
/// </summary>
public sealed class FrameStats
{
    /// <summary>A frame longer than this missed 30 fps.</summary>
    public const double HitchMs = 1000d / 30d;

    /// <summary>Frames kept for the percentiles: an hour at 60 fps. Past it the counts, the worst
    /// frame and the hitch list still update; only the distribution stops growing.</summary>
    public const int MaxSamples = 216_000;

    /// <summary>How many of the worst frames are kept with their time and cause.</summary>
    public const int WorstKept = 5;

    private readonly List<double> _samples = new();
    private readonly List<FrameHitch> _worst = new();

    public int Count { get; private set; }

    /// <summary>Frames over 33.3 ms, 50 ms and 100 ms. Each count includes the ones above it.</summary>
    public int Over33 { get; private set; }

    public int Over50 { get; private set; }

    public int Over100 { get; private set; }

    public double Worst { get; private set; }

    /// <summary>The slowest hitches, worst first.</summary>
    public IReadOnlyList<FrameHitch> WorstHitches => _worst;

    public void Add(double milliseconds, double atSeconds, string cause = "")
    {
        if (!double.IsFinite(milliseconds) || milliseconds < 0d)
        {
            return;
        }

        Count++;
        if (_samples.Count < MaxSamples)
        {
            _samples.Add(milliseconds);
        }

        if (milliseconds > Worst)
        {
            Worst = milliseconds;
        }

        if (milliseconds <= HitchMs)
        {
            return;
        }

        Over33++;
        if (milliseconds > 50d)
        {
            Over50++;
        }

        if (milliseconds > 100d)
        {
            Over100++;
        }

        if (_worst.Count == WorstKept && milliseconds <= _worst[^1].Milliseconds)
        {
            return;
        }

        _worst.Add(new FrameHitch(atSeconds, milliseconds, cause));
        _worst.Sort((a, b) => b.Milliseconds.CompareTo(a.Milliseconds));
        if (_worst.Count > WorstKept)
        {
            _worst.RemoveAt(_worst.Count - 1);
        }
    }

    /// <summary>Average, p50, p95, p99 and worst of the kept frames; all zero when there are none.</summary>
    public WorldFrameDistribution Distribution()
    {
        WorldFrameDistribution kept = WorldPerformanceRules.Distribution(_samples);
        return kept with { Worst = Math.Max(kept.Worst, Worst) };
    }

    /// <summary>The middle value of <paramref name="values"/> (the upper of the two for an even
    /// count), or 0 for none. Sorts a copy.</summary>
    public static double Median(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
        {
            return 0d;
        }

        var ordered = new double[values.Count];
        for (int i = 0; i < ordered.Length; i++)
        {
            ordered[i] = values[i];
        }

        Array.Sort(ordered);
        return ordered[ordered.Length / 2];
    }
}
