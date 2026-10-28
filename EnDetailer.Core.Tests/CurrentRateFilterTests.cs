using System;
using EnDetailer.Core;
using Xunit;

public class CurrentRateFilterTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Reaction = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Kampf wie im Betrieb: Beginn und erstes Datenpaket tragen denselben
    /// Zeitstempel, das erste Paket enthaelt den Schaden der Sekunde davor, danach
    /// kommt jede Sekunde ein Paket.
    /// </summary>
    private static RollingDpsTracker SteadyFight(double perSecond, int seconds)
    {
        var tracker = new RollingDpsTracker();
        tracker.MarkEncounterStart(T0);
        for (var i = 0; i <= seconds; i++)
            tracker.Record("Spieler", perSecond * (i + 1), T0.AddSeconds(i));
        return tracker;
    }

    [Fact]
    public void SteadyDamage_ReachesTheRateAfterFiveTau()
    {
        var tracker = SteadyFight(1000, 60);

        // tau = 5 s, fuenf tau = 25 s; geprueft wird spaeter im Kampf.
        var rate = tracker.GetCurrentRate("Spieler", T0.AddSeconds(60), Reaction, T0);

        Assert.InRange(rate, 980, 1020);
    }

    [Fact]
    public void WithoutFurtherDamage_FallsBelowThreePercentAfterThirtySeconds()
    {
        var tracker = SteadyFight(1000, 60);
        var last = T0.AddSeconds(60);
        var before = tracker.GetCurrentRate("Spieler", last, Reaction, T0);

        var previous = before;
        for (var s = 1; s <= 60; s++)
        {
            var rate = tracker.GetCurrentRate("Spieler", last.AddSeconds(s), Reaction, T0);
            Assert.True(rate >= 0, $"Rate war {rate} nach {s} s");
            Assert.True(rate <= previous + 1e-9, $"Rate stieg nach {s} s");
            previous = rate;
        }

        var after = tracker.GetCurrentRate("Spieler", last.AddSeconds(30), Reaction, T0);
        Assert.True(after < before * 0.03, $"{after} von {before}");
    }

    [Fact]
    public void CutIinactEncounter_CausesNeitherJumpNorNegativeValue()
    {
        var tracker = new RollingDpsTracker();
        tracker.MarkEncounterStart(T0);

        // IINACT schneidet bei Sekunde 30: sein kumulativer Wert faellt auf null zurueck.
        double? previous = null;
        for (var i = 0; i <= 60; i++)
        {
            var cumulative = i < 30 ? 1000.0 * (i + 1) : 1000.0 * (i - 29);
            tracker.Record("Spieler", cumulative, T0.AddSeconds(i));

            var rate = tracker.GetCurrentRate("Spieler", T0.AddSeconds(i), Reaction, T0);
            Assert.True(rate >= 0, $"Rate war {rate} bei {i} s");
            if (previous is { } p && i > 10)
                Assert.InRange(rate / p, 0.9, 1.1);
            previous = rate;
        }

        Assert.Equal(1000.0 * 61, tracker.GetTotalDamage("Spieler"), 1);
    }

    [Fact]
    public void FirstPacketOfAFight_HasAValueImmediately()
    {
        var tracker = new RollingDpsTracker();
        tracker.MarkEncounterStart(T0);
        tracker.Record("Spieler", 1000, T0);

        var rate = tracker.GetCurrentRate("Spieler", T0, Reaction, T0);

        Assert.True(rate > 0, $"Rate war {rate}");
        Assert.InRange(rate, 700, 1300);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(10)]
    [InlineData(30)]
    public void SteadyDamageFromTheStart_StaysNearTheRateInTheFirstFifteenSeconds(int reactionSeconds)
    {
        var tracker = SteadyFight(1000, 15);
        var reaction = TimeSpan.FromSeconds(reactionSeconds);

        for (var i = 2; i <= 15; i++)
        {
            var rate = tracker.GetCurrentRate("Spieler", T0.AddSeconds(i), reaction, T0);
            Assert.InRange(rate, 850, 1150);
        }
    }

    [Fact]
    public void Value_IsContinuousBetweenFrames()
    {
        var tracker = SteadyFight(1000, 40);
        var frame = TimeSpan.FromSeconds(1.0 / 60);

        var now = T0.AddSeconds(20);
        var previous = tracker.GetCurrentRate("Spieler", now, Reaction, T0);
        while (now < T0.AddSeconds(40))
        {
            now += frame;
            var rate = tracker.GetCurrentRate("Spieler", now, Reaction, T0);
            Assert.True(Math.Abs(rate - previous) / previous < 0.01, $"Sprung bei {(now - T0).TotalSeconds:0.000} s: {previous} -> {rate}");
            previous = rate;
        }
    }

    [Fact]
    public void Value_DoesNotJumpWhenABigHitArrives()
    {
        var tracker = new RollingDpsTracker();
        tracker.MarkEncounterStart(T0);
        for (var i = 0; i <= 30; i++)
            tracker.Record("Spieler", 1000.0 * (i + 1) + (i >= 20 ? 50_000 : 0), T0.AddSeconds(i));

        var beforePacket = tracker.GetCurrentRate("Spieler", T0.AddSeconds(20) - TimeSpan.FromSeconds(1.0 / 60), Reaction, T0);
        var atPacket = tracker.GetCurrentRate("Spieler", T0.AddSeconds(20), Reaction, T0);

        Assert.True(atPacket / beforePacket < 1.05, $"{beforePacket} -> {atPacket}");
    }

    [Fact]
    public void EvaluatingBeforeTheLastSample_EqualsEvaluatingBeforeItExisted()
    {
        var full = SteadyFight(1000, 40);

        var partial = new RollingDpsTracker();
        partial.MarkEncounterStart(T0);
        for (var i = 0; i <= 30; i++)
            partial.Record("Spieler", 1000.0 * (i + 1), T0.AddSeconds(i));

        // Der Zeitpunkt liegt nach dem Messpunkt bei 30 s und vor allen weiteren.
        var at = T0.AddSeconds(30.4);

        Assert.Equal(
            partial.GetCurrentRate("Spieler", at, Reaction, T0),
            full.GetCurrentRate("Spieler", at, Reaction, T0),
            9);
    }

    [Fact]
    public void LateAppearingRow_CountsItsDamageAsJustDealt()
    {
        // "Limit Break" erscheint bei IINACT erst, wenn es eingesetzt wird. Sein
        // Schaden ist in dem Moment entstanden, nicht ueber den Kampf verteilt.
        var tracker = new RollingDpsTracker();
        tracker.MarkEncounterStart(T0);
        for (var i = 0; i <= 60; i++)
            tracker.Record("Spieler", 1000.0 * (i + 1), T0.AddSeconds(i));

        tracker.Record("Limit Break", 50_000, T0.AddSeconds(60));

        // Drei Sekunden spaeter liegt der Filter nahe seinem Hoechstwert fuer diesen
        // Treffer (rund 3500 bei Reaktionszeit 10 s). Waere der Treffer in die Mitte
        // des Kampfes datiert, laege der Wert unter 100.
        var rate = tracker.GetCurrentRate("Limit Break", T0.AddSeconds(63), Reaction, T0);
        Assert.InRange(rate, 3000, 4000);
    }

    [Fact]
    public void ForUnknownCombatant_ReturnsZero()
    {
        Assert.Equal(0, new RollingDpsTracker().GetCurrentRate("Niemand", T0, Reaction, T0));
    }
}
