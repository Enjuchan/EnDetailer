using System;
using EnDetailer.Core;
using Xunit;

namespace EnDetailer.Core.Tests;

public class FightGraphTests
{
    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    [Fact]
    public void Add_RecordsOnePointPerSecond()
    {
        var graph = new FightGraph();

        Assert.False(graph.Due(S(0.9)));
        graph.Add(S(0.9), 1, 1, 1);
        Assert.Equal(0, graph.For(MeterMetric.Damage).Count);

        for (var second = 1; second <= 5; second++)
        {
            Assert.True(graph.Due(S(second)));
            graph.Add(S(second), second, 0, 0);
            Assert.False(graph.Due(S(second + 0.5)));
        }

        var series = graph.For(MeterMetric.Damage);
        Assert.Equal(5, series.Count);
        Assert.Equal(1, series.StepSeconds);
        Assert.Equal(1, series[0]);
        Assert.Equal(5, series[4]);
        Assert.Equal(5, series.Max);
    }

    [Fact]
    public void Add_FillsMissedSecondsWithTheSameValues()
    {
        var graph = new FightGraph();
        graph.Add(S(4.2), 7, 0, 0);

        var series = graph.For(MeterMetric.Damage);
        Assert.Equal(4, series.Count);
        Assert.Equal(7, series[3]);
    }

    [Fact]
    public void Add_KeepsTheThreeMetricsApart()
    {
        var graph = new FightGraph();
        graph.Add(S(1), 100, 20, 3);
        graph.Add(S(2), 200, 40, 6);

        Assert.Equal(200, graph.For(MeterMetric.Damage)[1]);
        Assert.Equal(20, graph.For(MeterMetric.Healing)[0]);
        Assert.Equal(6, graph.For(MeterMetric.DamageTaken)[1]);
        Assert.Equal(200, graph.For(MeterMetric.Damage).Max);
        Assert.Equal(40, graph.For(MeterMetric.Healing).Max);
        Assert.Equal(6, graph.For(MeterMetric.DamageTaken).Max);
    }

    [Fact]
    public void Add_MergesPairsAtTheLimitAndKeepsTheWholeFight()
    {
        var graph = new FightGraph();
        var series = graph.For(MeterMetric.Damage);

        for (var second = 1; second <= 3600; second++)
        {
            graph.Add(S(second), second, 0, 0);
            Assert.True(series.Count <= GraphSeries.MaxPoints);
        }

        // 3600 Sekunden: zweimal zusammengefasst, Abstand 4, ganze Dauer abgedeckt.
        Assert.Equal(4, series.StepSeconds);
        Assert.Equal(900, series.Count);
        Assert.Equal(3600, series.Count * series.StepSeconds);
        Assert.Equal((1 + 2 + 3 + 4) / 4.0, series[0], 6);
        // Nach der ersten Zusammenfassung kommen nur noch Einzelwerte im Abstand 2: 3598 und 3600.
        Assert.Equal((3598 + 3600) / 2.0, series[899], 6);
        Assert.Equal(series[899], series.Max, 6);
    }

    [Fact]
    public void Add_FirstMergeAveragesNeighbours()
    {
        var graph = new FightGraph();
        var series = graph.For(MeterMetric.Damage);

        for (var second = 1; second <= 1800; second++)
            graph.Add(S(second), second, 0, 0);

        Assert.Equal(900, series.Count);
        Assert.Equal(2, series.StepSeconds);
        Assert.Equal(1.5, series[0], 6);
        Assert.Equal(1799.5, series[899], 6);

        // Die nächsten Punkte kommen im doppelten Abstand.
        Assert.False(graph.Due(S(1801)));
        Assert.True(graph.Due(S(1802)));
        graph.Add(S(1805), 9, 0, 0);
        Assert.Equal(902, series.Count);
    }

    [Fact]
    public void Reset_StartsANewRecording()
    {
        var graph = new FightGraph();
        var series = graph.For(MeterMetric.Healing);
        for (var second = 1; second <= 2000; second++)
            graph.Add(S(second), 1, 1, 1);

        var version = series.Version;
        graph.Reset();

        Assert.Equal(0, series.Count);
        Assert.Equal(1, series.StepSeconds);
        Assert.Equal(0, series.Max);
        Assert.NotEqual(version, series.Version);

        graph.Add(S(1), 5, 6, 7);
        Assert.Equal(6, series[0]);
        Assert.Equal(1, series.Count);
    }

    [Fact]
    public void Truncate_KeepsOnlyThePointsUpToTheLastHit()
    {
        var graph = new FightGraph();
        for (var second = 1; second <= 10; second++)
            graph.Add(S(second), second, 0, 0);

        graph.Truncate(S(6.9));

        var series = graph.For(MeterMetric.Damage);
        Assert.Equal(6, series.Count);
        Assert.Equal(6, series.Max);

        graph.Truncate(S(100));
        Assert.Equal(6, series.Count);
    }

    [Fact]
    public void Record_GraphStaysUnchangedWhenANewFightIsRecorded()
    {
        var live = new FightGraph();
        for (var second = 1; second <= 20; second++)
            live.Add(S(second), second * 10, second, 1);

        var rows = new MetricRows([], 0, 0);
        var record = new FightRecord("Fight", S(20), DateTime.UtcNow, 0, rows, rows, rows, live.Copy());
        var shown = record.GraphFor(MeterMetric.Damage);

        live.Reset();
        for (var second = 1; second <= 50; second++)
            live.Add(S(second), 99_999, 99_999, 99_999);

        Assert.Equal(20, shown.Count);
        Assert.Equal(10, shown[0]);
        Assert.Equal(200, shown[19]);
        Assert.Equal(200, shown.Max);
        Assert.Equal(20, record.GraphFor(MeterMetric.Healing).Count);
        Assert.Equal(1, record.GraphFor(MeterMetric.DamageTaken)[5]);
    }

    [Fact]
    public void Record_WithoutGraphHasEmptySeries()
    {
        var rows = new MetricRows([], 0, 0);
        var record = new FightRecord("Fight", S(20), DateTime.UtcNow, 0, rows, rows, rows);

        Assert.Equal(0, record.GraphFor(MeterMetric.Damage).Count);
    }

    [Fact]
    public void RateSum_AddsTheRatesOfAllCombatants()
    {
        var t0 = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var tracker = new RollingDpsTracker();
        tracker.MarkEncounterStart(t0);
        tracker.Record("A", 0, t0);
        tracker.Record("B", 0, t0);
        tracker.Record("A", 1000, t0.AddSeconds(5));
        tracker.Record("B", 3000, t0.AddSeconds(5));

        var snapshot = new CombatSnapshot(
            t0.AddSeconds(5), "T", "00:05", true,
            [Combatant("A"), Combatant("B")]);

        var now = t0.AddSeconds(8);
        var reaction = S(10);
        var expected = tracker.GetCurrentRate("A", now, reaction, t0) + tracker.GetCurrentRate("B", now, reaction, t0);

        Assert.True(expected > 0);
        Assert.Equal(expected, MeterRows.RateSum(snapshot, tracker, now, t0, reaction), 9);
    }

    private static CombatantSnapshot Combatant(string name) =>
        new(name, Job.Unknown, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, string.Empty, 0, string.Empty, 0);
}
