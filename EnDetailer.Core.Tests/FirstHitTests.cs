using System;
using EnDetailer.Core;
using Xunit;

public class FirstHitTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void WeightedDps_HasAValueAfterTheVeryFirstHit()
    {
        // Der Kampf beginnt, eine Sekunde spaeter der erste Treffer. Schon dieser
        // eine Messpunkt muss einen Wert ergeben - sonst bleibt die Zeile beim
        // ersten Schlag jedes Kampfes leer.
        var tracker = new RollingDpsTracker();

        tracker.MarkEncounterStart(T0);
        tracker.Record("Spieler", 5000, T0.AddSeconds(1));

        var dps = tracker.GetWeightedDps("Spieler", T0.AddSeconds(1), TimeSpan.FromSeconds(25));

        Assert.True(dps > 0, $"DPS war {dps}");
    }

    [Fact]
    public void WeightedDps_WorksForSomeoneWhoJoinsMidFight()
    {
        // Ein Combatant, den IINACT erst nach Kampfbeginn zum ersten Mal meldet.
        var tracker = new RollingDpsTracker();

        tracker.MarkEncounterStart(T0);
        tracker.Record("Nachzuegler", 3000, T0.AddSeconds(4));

        var dps = tracker.GetWeightedDps("Nachzuegler", T0.AddSeconds(4), TimeSpan.FromSeconds(25));

        Assert.True(dps > 0, $"DPS war {dps}");
    }

    [Fact]
    public void FlatWindow_HasAValueAfterTheVeryFirstHit()
    {
        var tracker = new RollingDpsTracker();

        tracker.MarkEncounterStart(T0);
        tracker.Record("Spieler", 5000, T0.AddSeconds(1));

        var dps = tracker.GetRollingDps("Spieler", T0.AddSeconds(1), TimeSpan.FromSeconds(25), T0);

        Assert.Equal(5000, dps, 0);
    }
}
