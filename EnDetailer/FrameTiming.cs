using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Services;

namespace EnDetailer;

/// <summary>
/// Misst Dauern und haelt die der letzten Sekunden fuer die Diagnose-Zeile bereit.
/// Zusaetzlich gehen Mittel und Hoechstwert wie bei EnGlam und EnMotion alle 10 Sekunden ins Log.
/// </summary>
public sealed class FrameTiming(IPluginLog log, string name, TimeSpan window)
{
    private static readonly TimeSpan LogInterval = TimeSpan.FromSeconds(10);

    private readonly Queue<(DateTime At, double Ms)> samples = new();
    private double logTotal;
    private double logMax;
    private int logCount;
    private DateTime nextLog = DateTime.UtcNow + LogInterval;

    public void Add(TimeSpan elapsed)
    {
        var now = DateTime.UtcNow;
        var ms = elapsed.TotalMilliseconds;

        samples.Enqueue((now, ms));
        while (samples.Count > 0 && now - samples.Peek().At > window)
            samples.Dequeue();

        logTotal += ms;
        logCount++;
        logMax = Math.Max(logMax, ms);

        if (now < nextLog)
            return;

        log.Information("{Name}: avg {Average:0.000} ms, max {Max:0.000} ms over {Count} samples.",
            name, logTotal / logCount, logMax, logCount);

        logTotal = 0;
        logMax = 0;
        logCount = 0;
        nextLog = now + LogInterval;
    }

    /// <summary>Mittel und Hoechstwert der Messungen im Fenster, oder Text ohne Werte.</summary>
    public string Summary()
    {
        if (samples.Count == 0)
            return $"{name}: no data yet";

        return $"{name}: avg {samples.Average(s => s.Ms):0.000} ms, max {samples.Max(s => s.Ms):0.000} ms";
    }
}
