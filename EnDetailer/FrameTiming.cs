using System;
using System.Collections.Generic;
using Dalamud.Plugin.Services;

namespace EnDetailer;

/// <summary>
/// Misst Dauern und hält die der letzten Sekunden für die Diagnose-Zeile bereit. Gemessen wird immer, weil
/// das kaum etwas kostet; Mittel und Höchstwert gehen nur alle 10 Sekunden ins Log, solange die Diagnose an ist.
/// </summary>
public sealed class FrameTiming(IPluginLog log, string name, TimeSpan window, Func<bool> logEnabled)
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

        // Die Zähler laufen auch bei ausgeschalteter Diagnose weiter, damit der erste Eintrag nach dem
        // Einschalten nur die letzten 10 Sekunden enthält.
        if (logEnabled())
        {
            log.Information("{Name}: avg {Average:0.000} ms, max {Max:0.000} ms over {Count} samples.",
                name, logTotal / logCount, logMax, logCount);
        }

        logTotal = 0;
        logMax = 0;
        logCount = 0;
        nextLog = now + LogInterval;
    }

    /// <summary>Mittel und Höchstwert der Messungen im Fenster, oder Text ohne Werte.</summary>
    public string Summary()
    {
        if (samples.Count == 0)
            return Loc.T($"{name}: no data yet", $"{name}: noch keine Daten");

        var total = 0.0;
        var max = 0.0;
        foreach (var (_, ms) in samples)
        {
            total += ms;
            max = Math.Max(max, ms);
        }

        return Loc.T($"{name}: avg {total / samples.Count:0.000} ms, max {max:0.000} ms", $"{name}: Mittel {total / samples.Count:0.000} ms, max {max:0.000} ms");
    }
}
