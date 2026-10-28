using System;
using EnDetailer.Core;
using Xunit;

public class RollingDpsTrackerTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Record_WhenCumulativeDropsBack_TreatsItAsEncounterSplit()
    {
        var tracker = new RollingDpsTracker();

        // Der Kampf beginnt bei null, dann schneidet IINACT mittendrin und faengt
        // seinerseits neu an. Unsere Summe muss darueber hinweg weiterwachsen.
        tracker.Record("Spieler", 0, T0);
        tracker.Record("Spieler", 5000, T0.AddSeconds(1));
        tracker.Record("Spieler", 10000, T0.AddSeconds(2));
        tracker.Record("Spieler", 2000, T0.AddSeconds(3));

        Assert.Equal(12000, tracker.GetTotalDamage("Spieler"), 1);
    }

    [Fact]
    public void MarkEncounterStart_CountsOnlyDamageAfterTheNullPoint()
    {
        // IINACT laeuft mit einem alten Encounter weiter, waehrend wir selbst
        // schneiden. Der aufgelaufene Stand darf nicht als unser Schaden gelten.
        var tracker = new RollingDpsTracker();

        tracker.Record("Spieler", 4_500_000, T0);

        tracker.MarkEncounterStart(T0);

        tracker.Record("Spieler", 4_505_000, T0.AddSeconds(1));

        Assert.Equal(5000, tracker.GetTotalDamage("Spieler"), 1);
    }

    [Fact]
    public void MarkEncounterStart_CurrentRateIgnoresOlderDamage()
    {
        var tracker = new RollingDpsTracker();

        tracker.Record("Spieler", 4_500_000, T0);
        tracker.MarkEncounterStart(T0);

        var start = T0.AddSeconds(1);
        for (var i = 0; i <= 5; i++)
            tracker.Record("Spieler", 4_500_000 + i * 1000, start.AddSeconds(i));

        var dps = tracker.GetCurrentRate("Spieler", start.AddSeconds(5), TimeSpan.FromSeconds(10), start);

        // Tatsaechlich fallen 1000 Schaden je Sekunde; der Fremdschaden von 4,5 Mio.
        // darf nirgends auftauchen.
        Assert.InRange(dps, 700, 1300);
    }

    [Fact]
    public void WithoutEncounterStart_FirstSeenValueBecomesTheNullPoint()
    {
        // Plugin mitten im laufenden IINACT-Encounter geladen, ohne dass ein Kampf
        // begonnen haette: wir wissen nicht, wieviel davon zu uns gehoert, also
        // zaehlt erst ab jetzt - sonst stuenden dort Millionen Fremdschaden.
        var tracker = new RollingDpsTracker();

        tracker.Record("Spieler", 3_000_000, T0);
        tracker.Record("Spieler", 3_002_000, T0.AddSeconds(1));

        Assert.Equal(2000, tracker.GetTotalDamage("Spieler"), 1);
    }

    [Fact]
    public void AfterEncounterStart_UnknownCombatantCountsFromZero()
    {
        // Nach Kampfbeginn ist jeder neu auftauchende Combatant Teil dieses Kampfes.
        // Wuerde sein erster Wert zum Nullpunkt, ginge der erste Treffer verloren -
        // und die Zeile bliebe eine Runde lang leer.
        var tracker = new RollingDpsTracker();

        tracker.MarkEncounterStart(T0);
        tracker.Record("Neuling", 4000, T0);

        Assert.Equal(4000, tracker.GetTotalDamage("Neuling"), 1);
    }

    [Fact]
    public void CurrentRate_HasAValueFromTheFirstSampleOfAFight()
    {
        // Mit nur einem Messpunkt gab es bisher keinen Vergleichswert, der DPS blieb
        // null - sichtbar als leere Zeile in den ersten Sekunden jedes Kampfes.
        var tracker = new RollingDpsTracker();

        // Wie im Plugin: Das Paket mit dem ersten Schaden startet den Kampf, Beginn und
        // Messpunkt tragen denselben Zeitstempel.
        var start = T0.AddSeconds(1);
        tracker.Record("Spieler", 5000, T0);
        tracker.MarkEncounterStart(start);
        tracker.Record("Spieler", 8000, start);

        var dps = tracker.GetCurrentRate("Spieler", start, TimeSpan.FromSeconds(10), start);

        // 3000 Schaden irgendwann in der Sekunde vor dem Paket: um 3000 je Sekunde,
        // mit Spielraum fuer die Unsicherheit, wann genau in dieser Sekunde.
        Assert.InRange(dps, 1500, 3300);
    }

    [Fact]
    public void GetCurrentRate_ForUnknownCombatant_ReturnsZero()
    {
        var tracker = new RollingDpsTracker();

        Assert.Equal(0, tracker.GetCurrentRate("Niemand", T0, TimeSpan.FromSeconds(10), T0));
    }
}
