using System;
using System.Collections.Generic;

namespace EnDetailer.Core;

/// <summary>Eingefrorene Zeilen einer Messgroesse samt den Summen beider Zeitbasen fuer die Fusszeile.</summary>
public sealed class MetricRows(MeterRow[] rows, double rollingSum, double encounterSum)
{
    public IReadOnlyList<MeterRow> Rows { get; } = rows;
    public double RollingSum { get; } = rollingSum;
    public double EncounterSum { get; } = encounterSum;

    public double Sum(bool rolling) => rolling ? this.RollingSum : this.EncounterSum;
}

/// <summary>
/// Ein beendeter Kampf im Verlauf. Alles ist beim Aufnehmen fertig berechnet und aendert sich
/// danach nicht mehr, auch wenn die Tracker bereits einen neuen Kampf zaehlen.
/// </summary>
public sealed class FightRecord
{
    private readonly MetricRows damage;
    private readonly MetricRows healing;
    private readonly MetricRows taken;
    private readonly FightGraph graph;

    public FightRecord(
        string title, TimeSpan activeDuration, DateTime endedAt, int deaths,
        MetricRows damage, MetricRows healing, MetricRows taken, FightGraph? graph = null)
    {
        this.graph = graph ?? FightGraph.Empty();
        this.Title = title;
        this.ActiveDuration = activeDuration;
        this.EndedAt = endedAt;
        this.Deaths = deaths;
        this.damage = damage;
        this.healing = healing;
        this.taken = taken;
    }

    public string Title { get; }
    public TimeSpan ActiveDuration { get; }
    public DateTime EndedAt { get; }
    public int Deaths { get; }

    public MetricRows For(MeterMetric metric) => metric switch
    {
        MeterMetric.Healing => this.healing,
        MeterMetric.DamageTaken => this.taken,
        _ => this.damage
    };

    /// <summary>Verlauf der Messgroesse, Stand bis zum letzten Treffer; aendert sich nicht mehr.</summary>
    public GraphSeries GraphFor(MeterMetric metric) => this.graph.For(metric);

    /// <summary>
    /// Friert den Kampf zum Zeitpunkt <paramref name="at"/> ein - bei Kampfende der letzte Treffer,
    /// weil in der Karenzzeit der gleitende Wert schon absinkt.
    /// </summary>
    public static FightRecord Capture(
        CombatSnapshot snapshot,
        DateTime at,
        DateTime start,
        DateTime endedAt,
        TimeSpan activeDuration,
        TimeSpan reaction,
        RollingDpsTracker damageTracker,
        RollingDpsTracker healingTracker,
        RollingDpsTracker takenTracker,
        FightGraph? graph = null)
    {
        var buffer = new List<MeterRow>(snapshot.Combatants.Count);
        var deaths = 0;

        MetricRows Take(RollingDpsTracker tracker)
        {
            var totals = MeterRows.Build(snapshot, tracker, at, start, reaction, activeDuration, buffer);
            deaths = totals.Deaths;
            return new MetricRows([.. buffer], totals.Sum(true), totals.Sum(false));
        }

        var damage = Take(damageTracker);
        var healing = Take(healingTracker);
        var taken = Take(takenTracker);

        return new FightRecord(snapshot.Title, activeDuration, endedAt, deaths, damage, healing, taken, graph?.Copy());
    }
}
