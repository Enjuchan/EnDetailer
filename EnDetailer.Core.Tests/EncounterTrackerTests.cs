using System;
using EnDetailer.Core;
using Xunit;

public class EncounterTrackerTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private sealed class FakeConditions : IGameConditions
    {
        public bool InCombat { get; set; }
        public bool InCutscene { get; set; }
        public uint ZoneId { get; set; }
    }

    [Fact]
    public void Starts_OnFirstDamage()
    {
        var c = new FakeConditions { InCombat = true };
        var t = new EncounterTracker(c);

        t.Update(0, T0);
        Assert.Equal(EncounterState.Idle, t.State);

        t.Update(500, T0.AddSeconds(1));
        Assert.Equal(EncounterState.Running, t.State);
    }

    [Fact]
    public void Starts_EvenBeforeTheGameReportsCombat()
    {
        // FFXIV setzt das Kampf-Flag verzoegert. Der erste Schadenstick trifft
        // vorher ein - wer auf das Flag wartet, verliert den ersten Treffer.
        var c = new FakeConditions { InCombat = false };
        var t = new EncounterTracker(c);

        t.Update(500, T0);

        Assert.Equal(EncounterState.Running, t.State);
    }

    [Fact]
    public void KeepsRunning_ThroughCutsceneEvenWhenOutOfCombat()
    {
        var c = new FakeConditions { InCombat = true };
        var t = new EncounterTracker(c) { GracePeriod = TimeSpan.FromSeconds(10) };

        t.Update(500, T0);

        c.InCombat = false;
        c.InCutscene = true;
        for (var i = 1; i <= 40; i++)
            t.Update(500, T0.AddSeconds(i));

        Assert.Equal(EncounterState.Running, t.State);
    }

    /// <summary>
    /// Das Spiel pulst jeden Frame, IINACT nur im Kampf. Diese Hilfsmethode
    /// bildet den Puls sekundenweise nach, so wie es real ablaeuft.
    /// </summary>
    private static void TickSeconds(EncounterTracker t, DateTime from, int seconds)
    {
        for (var i = 1; i <= seconds; i++)
            t.Tick(from.AddSeconds(i));
    }

    [Fact]
    public void Ends_AfterGracePeriodOutOfCombat()
    {
        var c = new FakeConditions { InCombat = true };
        var t = new EncounterTracker(c) { GracePeriod = TimeSpan.FromSeconds(10) };

        t.Update(500, T0);

        c.InCombat = false;
        TickSeconds(t, T0, 5);
        Assert.Equal(EncounterState.Running, t.State);

        TickSeconds(t, T0.AddSeconds(5), 7);
        Assert.Equal(EncounterState.Ended, t.State);
    }

    [Fact]
    public void Ends_EvenWhenIinactStopsSendingData()
    {
        // IINACT sendet CombatData nur im Kampf. Nach Kampfende kommen keine
        // Updates mehr - allein der Puls aus dem Spiel muss den Encounter beenden.
        var c = new FakeConditions { InCombat = true };
        var t = new EncounterTracker(c) { GracePeriod = TimeSpan.FromSeconds(10) };

        t.Update(500, T0);
        Assert.Equal(EncounterState.Running, t.State);

        c.InCombat = false;
        TickSeconds(t, T0, 15);

        Assert.Equal(EncounterState.Ended, t.State);
    }

    [Fact]
    public void GracePeriod_RestartsAfterCutsceneEnds()
    {
        var c = new FakeConditions { InCombat = true };
        var t = new EncounterTracker(c) { GracePeriod = TimeSpan.FromSeconds(10) };

        t.Update(500, T0);

        c.InCombat = false;
        c.InCutscene = true;
        TickSeconds(t, T0, 30);
        Assert.Equal(EncounterState.Running, t.State);

        // Cutscene vorbei, ab jetzt laeuft die Karenzzeit
        c.InCutscene = false;
        TickSeconds(t, T0.AddSeconds(30), 5);
        Assert.Equal(EncounterState.Running, t.State);

        TickSeconds(t, T0.AddSeconds(35), 7);
        Assert.Equal(EncounterState.Ended, t.State);
    }

    [Fact]
    public void Freezes_UntilNextFightStarts()
    {
        var c = new FakeConditions { InCombat = true };
        var t = new EncounterTracker(c) { GracePeriod = TimeSpan.FromSeconds(10) };
        var starts = 0;
        t.EncounterStarted += () => starts++;

        t.Update(500, T0);
        Assert.Equal(1, starts);

        c.InCombat = false;
        TickSeconds(t, T0, 12);
        Assert.Equal(EncounterState.Ended, t.State);

        // Zahlen bleiben stehen, solange nichts passiert
        TickSeconds(t, T0.AddSeconds(12), 48);
        Assert.Equal(EncounterState.Ended, t.State);
        Assert.Equal(1, starts);

        // Neuer Kampf setzt zurueck
        c.InCombat = true;
        t.Update(50, T0.AddSeconds(70));
        Assert.Equal(EncounterState.Running, t.State);
        Assert.Equal(2, starts);
    }

    [Fact]
    public void ActiveDuration_StopsAtLastHit_WhileWallClockKeepsRunning()
    {
        // ACT laesst die Encounter-Uhr beim letzten Treffer stehen. Wer die
        // Karenzzeit mitrechnet, bekommt einen systematisch zu niedrigen
        // Encounter-DPS, der nicht mehr zu anderen Parsern passt.
        var c = new FakeConditions { InCombat = true };
        var t = new EncounterTracker(c) { GracePeriod = TimeSpan.FromSeconds(10) };

        t.Update(1000, T0);
        t.Update(2000, T0.AddSeconds(30));

        c.InCombat = false;
        TickSeconds(t, T0.AddSeconds(30), 12);

        Assert.Equal(EncounterState.Ended, t.State);

        // Bis zum letzten Treffer: 30 Sekunden
        Assert.Equal(TimeSpan.FromSeconds(30), t.ActiveDuration);

        // Wanduhr laeuft bis zum Kampfende weiter: 30 plus Karenzzeit
        Assert.True(t.Duration >= TimeSpan.FromSeconds(40), $"Wanduhr war {t.Duration}");
    }

    [Fact]
    public void ActiveDuration_CountsPausesInsideTheFight()
    {
        // Innerhalb des Kampfes zaehlt eine Pause sehr wohl mit - erst danach
        // wieder Schaden zu machen verlaengert die aktive Dauer entsprechend.
        var c = new FakeConditions { InCombat = true };
        var t = new EncounterTracker(c);

        t.Update(1000, T0);
        t.Update(2000, T0.AddSeconds(60));

        Assert.Equal(TimeSpan.FromSeconds(60), t.ActiveDuration);
    }

    [Fact]
    public void ZoneChange_EndsEncounter_WhenEnabled()
    {
        var c = new FakeConditions { InCombat = true, ZoneId = 100 };
        var t = new EncounterTracker(c) { ResetOnZoneChange = true };

        t.Update(500, T0);
        Assert.Equal(EncounterState.Running, t.State);

        c.ZoneId = 200;
        t.Update(500, T0.AddSeconds(1));
        Assert.Equal(EncounterState.Ended, t.State);
    }

    [Fact]
    public void ZoneChange_IsIgnored_WhenDisabled()
    {
        var c = new FakeConditions { InCombat = true, ZoneId = 100 };
        var t = new EncounterTracker(c) { ResetOnZoneChange = false };

        t.Update(500, T0);
        c.ZoneId = 200;
        t.Update(500, T0.AddSeconds(1));

        Assert.Equal(EncounterState.Running, t.State);
    }
}
