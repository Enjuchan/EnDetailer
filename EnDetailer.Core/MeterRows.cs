using System;
using System.Collections.Generic;

namespace EnDetailer.Core;

/// <summary>Summen, die beim Bauen der Zeilen anfallen und fuer Fusszeile und Kopf gebraucht werden.</summary>
public readonly record struct RowTotals(double RateSum, double TotalSum, int Deaths, double Seconds)
{
    /// <summary>Summe der Spalte in der gewaehlten Zeitbasis; Zeile und Summe meinen so nie Verschiedenes.</summary>
    public double Sum(bool rolling) => rolling ? this.RateSum : this.TotalSum / this.Seconds;
}

/// <summary>
/// Die Zeilenrechnung des Meters, einmal fuer jede Messgroesse und jeden Zeitpunkt. Der Aufrufer
/// waehlt den Tracker der Messgroesse; die Rechnung selbst ist dieselbe wie im laufenden Kampf.
/// </summary>
public static class MeterRows
{
    /// <summary>Summe der Echtzeitwerte aller Spieler, dieselbe Rechnung wie die Ratenspalte, ohne Zeilen zu bauen.</summary>
    public static double RateSum(CombatSnapshot snapshot, RollingDpsTracker tracker, DateTime now, DateTime start, TimeSpan reaction)
    {
        var combatants = snapshot.Combatants;
        var sum = 0d;
        for (var i = 0; i < combatants.Count; i++)
            sum += tracker.GetCurrentRate(combatants[i].Name, now, reaction, start);

        return sum;
    }

    /// <summary>
    /// Schreibt die Zeilen nach <paramref name="into"/> (vorher geleert). Den Encounter-DPS rechnen
    /// wir selbst statt IINACTs Wert zu uebernehmen, weil dessen Encounter laenger laufen kann als
    /// unserer. Als Nenner dient die Zeit bis zum letzten Treffer, nicht die Wanduhr - sonst
    /// druecken Karenzzeit und Nachlauf den Wert unter das, was LMeter und FFLogs zeigen.
    /// </summary>
    public static RowTotals Build(
        CombatSnapshot snapshot,
        RollingDpsTracker tracker,
        DateTime now,
        DateTime start,
        TimeSpan reaction,
        TimeSpan activeDuration,
        List<MeterRow> into)
    {
        var seconds = Math.Max(1, activeDuration.TotalSeconds);

        into.Clear();
        var combatants = snapshot.Combatants;
        var rateSum = 0d;
        var totalSum = 0d;
        var deaths = 0;

        for (var i = 0; i < combatants.Count; i++)
        {
            var c = combatants[i];
            var total = tracker.GetTotalDamage(c.Name);
            var rate = tracker.GetCurrentRate(c.Name, now, reaction, start);

            into.Add(new MeterRow(
                c.Name,
                c.Job,
                total,
                rate,
                total / seconds,
                c.CritPercent,
                c.DirectHitPercent,
                c.OverhealPercent,
                c));

            rateSum += rate;
            totalSum += total;
            deaths += c.Deaths;
        }

        return new RowTotals(rateSum, totalSum, deaths, seconds);
    }
}
