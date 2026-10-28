using System;
using System.Linq;
using EnDetailer.Core;
using Xunit;

namespace EnDetailer.Core.Tests;

public class PreviewCombatTests
{
    private static readonly DateTime Start = new(2026, 10, 28, 2, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Delivers_a_full_party_with_known_jobs()
    {
        var preview = new PreviewCombat(Start, null);

        var snapshot = preview.Snapshot(Start.AddSeconds(5));

        Assert.Equal(8, snapshot.Combatants.Count);
        Assert.DoesNotContain(snapshot.Combatants, c => c.Job == Job.Unknown);
        Assert.Equal(8, snapshot.Combatants.Select(c => c.Name).Distinct().Count());
    }

    [Fact]
    public void Local_player_name_replaces_one_preview_name()
    {
        var preview = new PreviewCombat(Start, "Enju Chan");

        var snapshot = preview.Snapshot(Start.AddSeconds(5));

        Assert.Contains(snapshot.Combatants, c => c.Name == "Enju Chan");
    }

    [Fact]
    public void Totals_never_decrease()
    {
        var preview = new PreviewCombat(Start, null);
        var previous = preview.Snapshot(Start);

        for (var second = 1; second <= 300; second++)
        {
            var current = preview.Snapshot(Start.AddSeconds(second));
            for (var i = 0; i < current.Combatants.Count; i++)
            {
                Assert.True(current.Combatants[i].TotalDamage >= previous.Combatants[i].TotalDamage);
                Assert.True(current.Combatants[i].TotalHealing >= previous.Combatants[i].TotalHealing);
                Assert.True(current.Combatants[i].TotalDamageTaken >= previous.Combatants[i].TotalDamageTaken);
            }

            previous = current;
        }
    }

    [Fact]
    public void Detail_values_are_plausible_and_never_decrease()
    {
        var preview = new PreviewCombat(Start, null);
        var previous = preview.Snapshot(Start.AddSeconds(5));

        Assert.All(previous.Combatants, c =>
        {
            Assert.NotEmpty(c.MaxHitName);
            Assert.NotEmpty(c.MaxHealName);
            Assert.True(c.MaxHitValue > 0);
            Assert.True(c.MaxHitValue <= c.TotalDamage);
            Assert.True(c.CritDirectHitPercent <= Math.Min(c.CritPercent, c.DirectHitPercent));
        });

        for (var second = 6; second <= 200; second++)
        {
            var current = preview.Snapshot(Start.AddSeconds(second));
            for (var i = 0; i < current.Combatants.Count; i++)
            {
                Assert.True(current.Combatants[i].MaxHitValue >= previous.Combatants[i].MaxHitValue);
                Assert.True(current.Combatants[i].MaxHealValue >= previous.Combatants[i].MaxHealValue);
                Assert.True(current.Combatants[i].ShieldAmount >= previous.Combatants[i].ShieldAmount);
            }

            previous = current;
        }
    }

    [Fact]
    public void Shields_and_defensive_rates_fit_the_job()
    {
        var snapshot = new PreviewCombat(Start, null).Snapshot(Start.AddSeconds(60));

        Assert.All(snapshot.Combatants.Where(c => c.Job is Job.DRG or Job.NIN or Job.BRD or Job.BLM),
            c => Assert.Equal(0, c.ShieldAmount));
        Assert.True(snapshot.Combatants.Single(c => c.Job == Job.SCH).ShieldAmount > 0);
        Assert.True(snapshot.Combatants.Single(c => c.Job == Job.PLD).BlockPercent > 0);
        Assert.True(snapshot.Combatants.Single(c => c.Job == Job.WAR).ParryPercent > 0);
    }

    [Fact]
    public void Burst_window_raises_damage_rate()
    {
        var preview = new PreviewCombat(Start, null);

        // Ein Burst-Fenster je Zyklus von zwei Minuten, Sekunde 8 bis 28.
        var beforeQuiet = preview.Snapshot(Start.AddSeconds(30)).Combatants.Sum(c => c.TotalDamage);
        var afterQuiet = preview.Snapshot(Start.AddSeconds(50)).Combatants.Sum(c => c.TotalDamage);
        var beforeBurst = preview.Snapshot(Start.AddSeconds(128)).Combatants.Sum(c => c.TotalDamage);
        var afterBurst = preview.Snapshot(Start.AddSeconds(148)).Combatants.Sum(c => c.TotalDamage);

        Assert.True(afterBurst - beforeBurst > 1.3 * (afterQuiet - beforeQuiet));
    }

    [Fact]
    public void Time_before_start_yields_empty_totals()
    {
        var preview = new PreviewCombat(Start, null);

        var snapshot = preview.Snapshot(Start.AddSeconds(-3));

        // Nur der erste Spieler hat einen Treffer genau bei Sekunde null, alle anderen
        // sind versetzt und haben noch nichts.
        Assert.All(snapshot.Combatants.Skip(1), c => Assert.Equal(0, c.TotalDamage));
    }
}
