using System;
using EnDetailer.Core;
using Xunit;

/// <summary>
/// Der Kampfbeginn und der erste Messpunkt tragen im Betrieb denselben Zeitstempel:
/// Das Plugin meldet beides aus demselben Datenpaket. Frueheren Tests hier fehlte
/// genau das - sie legten den Messpunkt eine Sekunde spaeter und liefen deshalb
/// gruen, waehrend im Spiel nichts angezeigt wurde.
/// </summary>
public class FirstHitTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void WeightedDps_HasAValueWhenStartAndFirstHitShareATimestamp()
    {
        var tracker = new RollingDpsTracker();

        // Vorlauf ausserhalb des Kampfes, wie ihn IINACT liefert
        tracker.Record("Spieler", 0, T0.AddSeconds(-2));
        tracker.Record("Spieler", 0, T0.AddSeconds(-1));

        // Kampfbeginn und erster Treffer im selben Datenpaket
        tracker.MarkEncounterStart(T0);
        tracker.Record("Spieler", 5000, T0);

        var dps = tracker.GetWeightedDps("Spieler", T0, TimeSpan.FromSeconds(25));

        Assert.True(dps > 0, $"DPS war {dps}");
    }

    [Fact]
    public void FlatWindow_HasAValueWhenStartAndFirstHitShareATimestamp()
    {
        var tracker = new RollingDpsTracker();

        tracker.Record("Spieler", 0, T0.AddSeconds(-2));
        tracker.Record("Spieler", 0, T0.AddSeconds(-1));

        tracker.MarkEncounterStart(T0);
        tracker.Record("Spieler", 5000, T0);

        var dps = tracker.GetRollingDps("Spieler", T0, TimeSpan.FromSeconds(25), T0);

        Assert.True(dps > 0, $"DPS war {dps}");
    }

    [Fact]
    public void FirstFightAfterLoading_HasAValueToo()
    {
        // Frisch geladenes Plugin: Es gibt ueberhaupt keinen Vorlauf, der Combatant
        // taucht mit dem Kampfbeginn zum ersten Mal auf.
        var tracker = new RollingDpsTracker();

        tracker.MarkEncounterStart(T0);
        tracker.Record("Spieler", 5000, T0);

        var dps = tracker.GetWeightedDps("Spieler", T0, TimeSpan.FromSeconds(25));

        Assert.True(dps > 0, $"DPS war {dps}");
    }

    [Fact]
    public void SomeoneJoiningMidFight_HasAValueOnTheirFirstHit()
    {
        var tracker = new RollingDpsTracker();

        tracker.MarkEncounterStart(T0);
        tracker.Record("Spieler", 5000, T0);
        tracker.Record("Nachzuegler", 3000, T0.AddSeconds(4));

        var dps = tracker.GetWeightedDps("Nachzuegler", T0.AddSeconds(4), TimeSpan.FromSeconds(25));

        Assert.True(dps > 0, $"DPS war {dps}");
    }

    [Fact]
    public void TotalDamage_IsExactOnTheFirstHit()
    {
        var tracker = new RollingDpsTracker();

        tracker.Record("Spieler", 4_000_000, T0.AddSeconds(-1));
        tracker.MarkEncounterStart(T0);
        tracker.Record("Spieler", 4_005_000, T0);

        Assert.Equal(5000, tracker.GetTotalDamage("Spieler"), 1);
    }
}
