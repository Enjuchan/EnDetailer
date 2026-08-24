using System;
using EnDetailer.Core;
using Xunit;

public class WeightedDpsTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static RollingDpsTracker WithSteadyDamage(double perSecond, int seconds)
    {
        var tracker = new RollingDpsTracker();
        for (var i = 0; i <= seconds; i++)
            tracker.Record("Spieler", i * perSecond, T0.AddSeconds(i));

        return tracker;
    }

    [Fact]
    public void SteadyDamage_WeightedMatchesTheFlatWindow()
    {
        // Bei gleichmaessigem Schaden muessen beide Verfahren dasselbe liefern -
        // die Gewichtung darf den Wert nicht verschieben, nur seinen Verlauf.
        var tracker = WithSteadyDamage(1000, 30);

        var weighted = tracker.GetWeightedDps("Spieler", T0.AddSeconds(30), TimeSpan.FromSeconds(10));

        Assert.Equal(1000, weighted, 0);
    }

    [Fact]
    public void WeightedDps_FallsTowardZeroWhenDamageStops()
    {
        // Der entscheidende Unterschied zum Durchschnitt: Ohne Schaden geht der
        // Wert gegen null, statt stehenzubleiben.
        var tracker = WithSteadyDamage(1000, 10);

        for (var i = 11; i <= 40; i++)
            tracker.Record("Spieler", 10000, T0.AddSeconds(i));

        var weighted = tracker.GetWeightedDps("Spieler", T0.AddSeconds(40), TimeSpan.FromSeconds(10));

        Assert.True(weighted < 20, $"Wert war {weighted}");
    }

    [Fact]
    public void WeightedDps_HasNoCliffWhenAHitLeavesTheWindow()
    {
        // Ein einzelner grosser Treffer, danach nichts mehr. Beim harten Fenster
        // faellt der Wert genau nach Fensterlaenge schlagartig ab. Weich gewichtet
        // darf zwischen zwei benachbarten Zeitpunkten kein Sprung entstehen.
        var tracker = new RollingDpsTracker();
        tracker.Record("Spieler", 0, T0);
        tracker.Record("Spieler", 40000, T0.AddSeconds(1));

        // Nur bis kurz vor den Abfragezeitpunkt aufzeichnen - im Spiel liegen nie
        // Messwerte in der Zukunft.
        for (var i = 2; i <= 11; i++)
            tracker.Record("Spieler", 40000, T0.AddSeconds(i));

        // Der Treffer von Sekunde 1 verlaesst das Zehn-Sekunden-Fenster bei 11.
        var window = TimeSpan.FromSeconds(10);
        var justBefore = tracker.GetWeightedDps("Spieler", T0.AddSeconds(10.9), window);
        var justAfter = tracker.GetWeightedDps("Spieler", T0.AddSeconds(11.1), window);

        var jump = Math.Abs(justBefore - justAfter);
        Assert.True(jump < justBefore * 0.1, $"Sprung war {jump} bei {justBefore}");
    }

    [Fact]
    public void WeightedDps_ReactsToABurst()
    {
        // Nach ruhiger Phase ein Burst: der Wert muss deutlich steigen, sonst
        // waere die Glaettung zu traege, um noch etwas auszusagen.
        var tracker = new RollingDpsTracker();
        for (var i = 0; i <= 20; i++)
            tracker.Record("Spieler", i * 1000, T0.AddSeconds(i));

        var calm = tracker.GetWeightedDps("Spieler", T0.AddSeconds(20), TimeSpan.FromSeconds(10));

        for (var i = 21; i <= 25; i++)
            tracker.Record("Spieler", 20000 + (i - 20) * 5000, T0.AddSeconds(i));

        var burst = tracker.GetWeightedDps("Spieler", T0.AddSeconds(25), TimeSpan.FromSeconds(10));

        Assert.True(burst > calm * 2, $"ruhig {calm}, Burst {burst}");
    }

    [Fact]
    public void WeightedDps_ForUnknownCombatant_ReturnsZero()
    {
        var tracker = new RollingDpsTracker();

        Assert.Equal(0, tracker.GetWeightedDps("Niemand", T0, TimeSpan.FromSeconds(10)));
    }
}
