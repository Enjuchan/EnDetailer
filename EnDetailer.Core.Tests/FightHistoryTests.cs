using System;
using System.Collections.Generic;
using EnDetailer.Core;
using Xunit;

public class FightHistoryTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static MetricRows Empty() => new([], 0, 0);

    private static FightRecord Record(string title) =>
        new(title, TimeSpan.FromSeconds(60), T0, 0, Empty(), Empty(), Empty());

    private static FightHistory WithFights(int count)
    {
        var history = new FightHistory();
        for (var i = 1; i <= count; i++)
        {
            history.LiveStarted();
            history.Add(Record("Fight " + i));
        }

        return history;
    }

    private static CombatSnapshot Snapshot(string title, double damage, double healing, double taken, int deaths = 1)
    {
        var c = new CombatantSnapshot(
            "Spieler", Job.Unknown, damage, 0, 10, 20, healing, 0, 5, taken, deaths, 0, 0, 0, 0, 0, 0, "", 0, "", 0);
        return new CombatSnapshot(T0, title, "00:10", true, [c]);
    }

    [Fact]
    public void Add_StoresRecord()
    {
        var history = new FightHistory();
        var record = Record("Boss");

        history.Add(record);

        Assert.Equal(1, history.Count);
        Assert.Same(record, history[0]);
        Assert.Null(history.Selected);
    }

    [Fact]
    public void Add_KeepsNewestFirst()
    {
        var history = WithFights(3);

        Assert.Equal("Fight 3", history[0].Title);
        Assert.Equal("Fight 2", history[1].Title);
        Assert.Equal("Fight 1", history[2].Title);
    }

    [Fact]
    public void Add_DropsOldestBeyondCapacity()
    {
        var history = WithFights(FightHistory.Capacity + 3);

        Assert.Equal(FightHistory.Capacity, history.Count);
        Assert.Equal("Fight 13", history[0].Title);
        Assert.Equal("Fight 4", history[FightHistory.Capacity - 1].Title);
    }

    [Fact]
    public void GoOlder_FromLiveSkipsTheJustEndedFight()
    {
        var history = WithFights(3);

        history.GoOlder();

        Assert.Equal(1, history.SelectedIndex);
        Assert.Equal("Fight 2", history.Selected!.Title);
    }

    [Fact]
    public void GoOlder_FromLiveWhileFightRunsShowsNewestEntry()
    {
        var history = WithFights(2);
        history.LiveStarted();

        history.GoOlder();

        Assert.Equal(0, history.SelectedIndex);
        Assert.Equal("Fight 2", history.Selected!.Title);
    }

    [Fact]
    public void GoOlder_StopsAtOldestEntry()
    {
        var history = WithFights(3);

        history.GoOlder();
        history.GoOlder();
        Assert.False(history.CanGoOlder);
        history.GoOlder();

        Assert.Equal(2, history.SelectedIndex);
    }

    [Fact]
    public void GoOlder_WithOnlyTheJustEndedFightDoesNothing()
    {
        var history = WithFights(1);

        Assert.False(history.CanGoOlder);
        history.GoOlder();

        Assert.Equal(FightHistory.LiveIndex, history.SelectedIndex);
    }

    [Fact]
    public void GoNewer_ReturnsToLiveWithoutVisitingTheDuplicate()
    {
        var history = WithFights(3);
        history.GoOlder();
        history.GoOlder();

        history.GoNewer();
        Assert.Equal(1, history.SelectedIndex);

        history.GoNewer();
        Assert.Equal(FightHistory.LiveIndex, history.SelectedIndex);
        Assert.False(history.CanGoNewer);

        history.GoNewer();
        Assert.Equal(FightHistory.LiveIndex, history.SelectedIndex);
    }

    [Fact]
    public void GoNewer_WhileFightRunsPassesThroughNewestEntry()
    {
        var history = WithFights(2);
        history.LiveStarted();
        history.GoOlder();
        history.GoOlder();

        history.GoNewer();
        Assert.Equal(0, history.SelectedIndex);

        history.GoNewer();
        Assert.Equal(FightHistory.LiveIndex, history.SelectedIndex);
    }

    [Fact]
    public void LiveStarted_JumpsToLiveFromAnywhere()
    {
        var history = WithFights(4);
        history.GoOlder();
        history.GoOlder();

        history.LiveStarted();

        Assert.Equal(FightHistory.LiveIndex, history.SelectedIndex);
        Assert.Equal(0, history.FirstBrowsable);
    }

    [Fact]
    public void Select_IgnoresIndexOfTheDuplicateAndOutOfRange()
    {
        var history = WithFights(3);

        history.Select(0);
        history.Select(3);
        history.Select(-5);
        Assert.Equal(FightHistory.LiveIndex, history.SelectedIndex);

        history.Select(2);
        Assert.Equal(2, history.SelectedIndex);

        history.SelectLive();
        Assert.Equal(FightHistory.LiveIndex, history.SelectedIndex);
    }

    [Fact]
    public void Add_KeepsTheShownEntryWhenOthersShift()
    {
        var history = WithFights(3);
        history.LiveStarted();
        history.GoOlder();
        var shown = history.Selected;

        history.Add(Record("Fight 4"));

        Assert.Same(shown, history.Selected);
        Assert.Equal(1, history.SelectedIndex);
    }

    [Fact]
    public void Add_FallsBackToLiveWhenShownEntryDropsOut()
    {
        var history = WithFights(FightHistory.Capacity);
        history.LiveStarted();
        for (var i = 0; i < FightHistory.Capacity; i++)
            history.GoOlder();
        Assert.Equal(FightHistory.Capacity - 1, history.SelectedIndex);

        history.Add(Record("Newest"));

        Assert.Equal(FightHistory.LiveIndex, history.SelectedIndex);
    }

    [Fact]
    public void Version_ChangesOnlyWhenSomethingChanges()
    {
        var history = WithFights(2);
        var before = history.Version;

        history.GoNewer();
        history.SelectLive();
        Assert.Equal(before, history.Version);

        history.GoOlder();
        Assert.NotEqual(before, history.Version);
    }

    [Fact]
    public void Capture_StoresRowsForAllMetrics()
    {
        var damage = new RollingDpsTracker();
        var healing = new RollingDpsTracker();
        var taken = new RollingDpsTracker();
        foreach (var (t, d, h, k) in new[] { (0, 0d, 0d, 0d), (10, 5000d, 2000d, 800d) })
        {
            damage.Record("Spieler", d, T0.AddSeconds(t));
            healing.Record("Spieler", h, T0.AddSeconds(t));
            taken.Record("Spieler", k, T0.AddSeconds(t));
        }

        var record = FightRecord.Capture(
            Snapshot("Boss", 5000, 2000, 800, deaths: 2),
            T0.AddSeconds(10), T0, T0.AddSeconds(20), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(1),
            damage, healing, taken);

        Assert.Equal("Boss", record.Title);
        Assert.Equal(TimeSpan.FromSeconds(10), record.ActiveDuration);
        Assert.Equal(T0.AddSeconds(20), record.EndedAt);
        Assert.Equal(2, record.Deaths);
        Assert.Equal(5000, record.For(MeterMetric.Damage).Rows[0].Total, 1);
        Assert.Equal(2000, record.For(MeterMetric.Healing).Rows[0].Total, 1);
        Assert.Equal(800, record.For(MeterMetric.DamageTaken).Rows[0].Total, 1);
        Assert.Equal(500, record.For(MeterMetric.Damage).EncounterSum, 1);
        Assert.NotNull(record.For(MeterMetric.Damage).Rows[0].Detail);
    }

    [Fact]
    public void Capture_RowsStayUnchangedWhenANewFightRuns()
    {
        var damage = new RollingDpsTracker();
        var healing = new RollingDpsTracker();
        var taken = new RollingDpsTracker();
        void RecordAll(int t, double d, double h, double k)
        {
            damage.Record("Spieler", d, T0.AddSeconds(t));
            healing.Record("Spieler", h, T0.AddSeconds(t));
            taken.Record("Spieler", k, T0.AddSeconds(t));
        }

        RecordAll(0, 0, 0, 0);
        RecordAll(10, 5000, 2000, 800);

        var record = FightRecord.Capture(
            Snapshot("First", 5000, 2000, 800),
            T0.AddSeconds(10), T0, T0.AddSeconds(20), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(1),
            damage, healing, taken);

        var newStart = T0.AddSeconds(100);
        damage.MarkEncounterStart(newStart);
        healing.MarkEncounterStart(newStart);
        taken.MarkEncounterStart(newStart);
        RecordAll(100, 0, 0, 0);
        RecordAll(110, 99_000, 77_000, 66_000);

        Assert.Equal(5000, record.For(MeterMetric.Damage).Rows[0].Total, 1);
        Assert.Equal(2000, record.For(MeterMetric.Healing).Rows[0].Total, 1);
        Assert.Equal(800, record.For(MeterMetric.DamageTaken).Rows[0].Total, 1);
        Assert.Equal(500, record.For(MeterMetric.Damage).Rows[0].EncounterRate, 1);
    }

    [Fact]
    public void MeterRows_BuildClearsTheTargetAndSumsDeaths()
    {
        var tracker = new RollingDpsTracker();
        tracker.Record("Spieler", 0, T0);
        tracker.Record("Spieler", 1000, T0.AddSeconds(5));
        var into = new List<MeterRow> { new("Alt", Job.Unknown, 1, 1, 1, 0, 0, 0) };

        var totals = MeterRows.Build(
            Snapshot("X", 1000, 0, 0, deaths: 3), tracker, T0.AddSeconds(5), T0,
            TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), into);

        Assert.Single(into);
        Assert.Equal("Spieler", into[0].Name);
        Assert.Equal(3, totals.Deaths);
        Assert.Equal(200, totals.Sum(false), 1);
    }
}
