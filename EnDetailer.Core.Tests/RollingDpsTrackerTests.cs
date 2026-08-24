using System;
using EnDetailer.Core;
using Xunit;

public class RollingDpsTrackerTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void RollingDps_UsesOnlyDamageInsideWindow()
    {
        var tracker = new RollingDpsTracker();

        for (var i = 0; i <= 10; i++)
            tracker.Record("Spieler", i * 1000, T0.AddSeconds(i));

        var dps = tracker.GetRollingDps("Spieler", T0.AddSeconds(10), TimeSpan.FromSeconds(10), T0);

        Assert.Equal(1000, dps, 1);
    }

    [Fact]
    public void RollingDps_FallsToZeroWhenDamageStops()
    {
        var tracker = new RollingDpsTracker();

        for (var i = 0; i <= 10; i++)
            tracker.Record("Spieler", i * 1000, T0.AddSeconds(i));

        for (var i = 11; i <= 25; i++)
            tracker.Record("Spieler", 10000, T0.AddSeconds(i));

        var dps = tracker.GetRollingDps("Spieler", T0.AddSeconds(25), TimeSpan.FromSeconds(10), T0);

        Assert.Equal(0, dps, 1);
    }

    [Fact]
    public void RollingDps_EarlyInFight_DividesByElapsedNotWindow()
    {
        var tracker = new RollingDpsTracker();

        tracker.Record("Spieler", 0, T0);
        tracker.Record("Spieler", 3000, T0.AddSeconds(3));

        var dps = tracker.GetRollingDps("Spieler", T0.AddSeconds(3), TimeSpan.FromSeconds(10), T0);

        Assert.Equal(1000, dps, 1);
    }

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
    public void MarkEncounterStart_RollingDpsIgnoresOlderDamage()
    {
        var tracker = new RollingDpsTracker();

        tracker.Record("Spieler", 4_500_000, T0);
        tracker.MarkEncounterStart(T0);

        var start = T0.AddSeconds(1);
        for (var i = 0; i <= 5; i++)
            tracker.Record("Spieler", 4_500_000 + i * 1000, start.AddSeconds(i));

        var dps = tracker.GetRollingDps("Spieler", start.AddSeconds(5), TimeSpan.FromSeconds(10), start);

        Assert.Equal(1000, dps, 1);
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
    public void RollingDps_HasAValueFromTheFirstSampleOfAFight()
    {
        // Mit nur einem Messpunkt gab es bisher keinen Vergleichswert, der DPS blieb
        // null - sichtbar als leere Zeile in den ersten Sekunden jedes Kampfes.
        var tracker = new RollingDpsTracker();

        tracker.Record("Spieler", 5000, T0);
        tracker.MarkEncounterStart(T0);
        tracker.Record("Spieler", 8000, T0.AddSeconds(1));

        var dps = tracker.GetRollingDps("Spieler", T0.AddSeconds(1), TimeSpan.FromSeconds(10), T0);

        Assert.Equal(3000, dps, 1);
    }

    [Fact]
    public void RollingDps_InterpolatesTheWindowEdge()
    {
        // Gleichmaessig 1000 Schaden je Sekunde ueber 20 Sekunden. Die Fenstergrenze
        // liegt bei 15,5 Sekunden mitten zwischen zwei Messpunkten. Hart
        // abgeschnitten kaeme dort ein zu grosser Wert heraus, weil eine halbe
        // Sekunde Schaden zuviel mitgezaehlt wird.
        var tracker = new RollingDpsTracker();

        for (var i = 0; i <= 20; i++)
            tracker.Record("Spieler", i * 1000, T0.AddSeconds(i));

        var dps = tracker.GetRollingDps("Spieler", T0.AddSeconds(20.5), TimeSpan.FromSeconds(5), T0);

        // Zwischen 15,5s und 20s sind 4500 Schaden gefallen, geteilt durch 5 Sekunden
        Assert.Equal(900, dps, 1);
    }

    [Fact]
    public void RollingDps_ChangesBetweenSamples()
    {
        // Zwischen zwei Messpunkten wandert das Fenster weiter. Der Wert muss sich
        // deshalb auch dann aendern, wenn gerade nichts Neues eintrifft - sonst
        // steht die Anzeige eine Sekunde lang still und springt dann.
        var tracker = new RollingDpsTracker();

        for (var i = 0; i <= 10; i++)
            tracker.Record("Spieler", i * 1000, T0.AddSeconds(i));

        var atTick = tracker.GetRollingDps("Spieler", T0.AddSeconds(10.0), TimeSpan.FromSeconds(5), T0);
        var between = tracker.GetRollingDps("Spieler", T0.AddSeconds(10.5), TimeSpan.FromSeconds(5), T0);

        Assert.NotEqual(atTick, between, 1);
    }

    [Fact]
    public void GetRollingDps_ForUnknownCombatant_ReturnsZero()
    {
        var tracker = new RollingDpsTracker();

        Assert.Equal(0, tracker.GetRollingDps("Niemand", T0, TimeSpan.FromSeconds(10), T0));
    }
}
