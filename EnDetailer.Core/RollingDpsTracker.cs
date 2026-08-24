using System;
using System.Collections.Generic;
using System.Linq;

namespace EnDetailer.Core;

/// <summary>
/// Fuehrt je Combatant einen Verlauf des kumulativen Schadens und leitet daraus
/// den Schaden innerhalb eines gleitenden Zeitfensters ab.
/// </summary>
public sealed class RollingDpsTracker
{
    private sealed class Series
    {
        public readonly List<(DateTime At, double Total)> Samples = [];
        public double Baseline;
        public double LastCumulative;
        public bool HasSample;

        /// <summary>
        /// Der Stand, den IINACT bei Beginn unseres Encounters hatte. Alles
        /// darueber ist unser Kampf. Null, solange noch kein Stand bekannt ist -
        /// dann wird der erste gesehene Wert zum Nullpunkt.
        /// </summary>
        public double? Origin;

        public double Cumulative => this.Baseline + this.LastCumulative;
    }

    // Muss laenger sein als das laengste einstellbare Fenster, sonst fehlen dem
    // Fenster die aelteren Messpunkte.
    private static readonly TimeSpan History = TimeSpan.FromSeconds(150);
    private readonly Dictionary<string, Series> series = [];

    /// <summary>
    /// Ob bereits ein Kampfbeginn gemeldet wurde. Davor ist unbekannt, wieviel vom
    /// gemeldeten Stand zu uns gehoert; danach ist jeder neue Combatant Teil des
    /// laufenden Kampfes und zaehlt ab null.
    /// </summary>
    private bool encounterStarted;

    /// <summary>Zeitpunkt des Kampfbeginns, als Startpunkt jeder Zeitreihe.</summary>
    private DateTime encounterStartedAt;

    /// <summary>
    /// Abstand des Nullpunkts vom Kampfbeginn.
    ///
    /// Kampfbeginn und erster Messpunkt tragen denselben Zeitstempel - das Plugin
    /// meldet beides aus demselben Datenpaket. Laege der Nullpunkt genau darauf,
    /// waere der Abstand null und es liesse sich keine Rate bilden: Die Anzeige
    /// bliebe beim ersten Treffer jedes Kampfes leer. Der Schaden dieses Ticks ist
    /// ohnehin im Intervall davor entstanden, also gehoert der Nullpunkt dorthin.
    /// Eine Sekunde entspricht dem Sendetakt von IINACT.
    /// </summary>
    private static readonly TimeSpan ZeroPointLead = TimeSpan.FromSeconds(1);


    public void Record(string name, double cumulativeDamage, DateTime at)
    {
        if (!this.series.TryGetValue(name, out var s))
        {
            s = new Series();
            this.series[name] = s;

            // Ein Nullpunkt vor dem Kampfbeginn. Ohne ihn hat die Reihe nach dem
            // ersten Treffer nur einen einzigen Messpunkt - daraus laesst sich keine
            // Rate bilden, und die Zeile bliebe beim ersten Schlag leer.
            if (this.encounterStarted)
            {
                var zero = this.encounterStartedAt - ZeroPointLead;
                if (at > zero)
                    s.Samples.Add((zero, 0));
            }
        }

        // Faellt der kumulative Wert zurueck, hat IINACT den Encounter geschnitten.
        // Der bisherige Stand wird zur Basis, auf der weiter aufaddiert wird.
        if (s.HasSample && cumulativeDamage < s.LastCumulative)
            s.Baseline += s.LastCumulative;

        s.LastCumulative = cumulativeDamage;
        s.HasSample = true;

        // Innerhalb eines laufenden Kampfes zaehlt ein neu auftauchender Combatant
        // ab null - sein erster gemeldeter Wert ist bereits sein Schaden. Vor dem
        // ersten Kampfbeginn ist dagegen unklar, wieviel davon uns gehoert, dann
        // wird der erste Wert zum Nullpunkt.
        s.Origin ??= this.encounterStarted ? 0 : s.Cumulative;

        s.Samples.Add((at, s.Cumulative - s.Origin.Value));

        var cutoff = at - History;
        while (s.Samples.Count > 2 && s.Samples[0].At < cutoff)
            s.Samples.RemoveAt(0);
    }

    public double GetTotalDamage(string name) =>
        this.series.TryGetValue(name, out var s) && s.Samples.Count > 0
            ? s.Samples[^1].Total
            : 0;

    public double GetRollingDps(string name, DateTime now, TimeSpan window, DateTime encounterStart)
    {
        if (!this.series.TryGetValue(name, out var s) || s.Samples.Count == 0)
            return 0;

        var windowStart = now - window;

        var baseTotal = TotalAt(s, windowStart);
        var damageInWindow = Math.Max(0, s.Samples[^1].Total - baseTotal);

        // Am Kampfanfang durch die tatsaechlich verstrichene Zeit teilen, damit der
        // Wert nicht kuenstlich klein startet. Danach immer durch die volle
        // Fensterlaenge, damit er ohne Schaden auf null sinkt.
        var elapsed = now - encounterStart;
        var divisor = elapsed < window ? elapsed : window;

        // Beim ersten Datenpaket eines Kampfes ist keine Zeit verstrichen: Beginn
        // und Messpunkt tragen denselben Zeitstempel. Ohne Untergrenze waere der
        // Nenner null und der Wert bliebe leer. Der Schaden ist im Intervall davor
        // entstanden, also wird mindestens dieses gerechnet.
        if (divisor < ZeroPointLead)
            divisor = ZeroPointLead;

        return damageInWindow / divisor.TotalSeconds;
    }

    /// <summary>
    /// Zeitlich gewichteter DPS: Schaden der letzten Sekunden zaehlt voll, aelterer
    /// klingt exponentiell aus.
    ///
    /// Das harte Fenster hat zwei Kanten - ein Treffer springt beim Eintreten in
    /// den Wert hinein und beim Verlassen wieder heraus, obwohl in diesem Moment
    /// nichts geschieht. Bei wenigen grossen Treffern, etwa langen Zaubern, ist
    /// jeder davon als Stufe sichtbar. Hier verschwindet der Beitrag allmaehlich,
    /// womit die zweite Kante entfaellt.
    ///
    /// Es bleibt ein echter Momentanwert: Ohne neuen Schaden laufen die juengsten
    /// Intervalle mit Rate null ein und ziehen das Ergebnis gegen null. Anders als
    /// ein Encounter-Durchschnitt kann er also fallen.
    /// </summary>
    public double GetWeightedDps(string name, DateTime now, TimeSpan window)
    {
        if (!this.series.TryGetValue(name, out var s) || s.Samples.Count < 2)
            return 0;

        // Zeitkonstante des Ausklingens. Die halbe Fensterlaenge trifft ungefaehr
        // die Reaktionszeit des harten Fensters gleicher Laenge.
        var tau = window.TotalSeconds / 2.0;
        if (tau <= 0)
            return 0;

        var weightedSum = 0.0;
        var weightTotal = 0.0;

        for (var i = 1; i < s.Samples.Count; i++)
        {
            var previous = s.Samples[i - 1];
            var current = s.Samples[i];

            // Messpunkte nach dem Abfragezeitpunkt gehoeren nicht dazu.
            if (previous.At >= now)
                break;

            var span = (current.At - previous.At).TotalSeconds;
            if (span <= 0)
                continue;

            var rate = (current.Total - previous.Total) / span;

            // Gewicht nach dem Alter der Intervallmitte.
            var age = (now - previous.At).TotalSeconds - span / 2.0;
            if (age < 0)
                age = 0;

            var weight = Math.Exp(-age / tau) * span;

            weightedSum += rate * weight;
            weightTotal += weight;
        }

        // Seit dem letzten Messpunkt vergangene Zeit zaehlt als Intervall ohne
        // Schaden mit. Ohne das bliebe der Wert stehen, wenn nichts mehr eintrifft.
        var idle = (now - s.Samples[^1].At).TotalSeconds;
        if (idle > 0)
            weightTotal += idle;

        return weightTotal <= 0 ? 0 : weightedSum / weightTotal;
    }

    /// <summary>
    /// Aufgelaufener Schaden zu einem beliebigen Zeitpunkt, zwischen den Messpunkten
    /// linear interpoliert.
    ///
    /// Die Fenstergrenze liegt fast nie genau auf einem Messpunkt. Wer stattdessen
    /// den letzten Punkt davor nimmt, zaehlt bis zu eine Sekunde Schaden zuviel mit
    /// und laesst den Wert bei jedem Tick springen, sobald ein Punkt herausfaellt.
    /// Interpoliert wandert die Grenze stetig weiter - das ist zugleich genauer und
    /// ruhiger anzusehen.
    ///
    /// Liegt der Zeitpunkt vor dem ersten Messpunkt, ist noch nichts aufgelaufen:
    /// der Kampf hat innerhalb des Fensters begonnen.
    /// </summary>
    private static double TotalAt(Series s, DateTime at)
    {
        if (at <= s.Samples[0].At)
            return 0;

        if (at >= s.Samples[^1].At)
            return s.Samples[^1].Total;

        for (var i = 1; i < s.Samples.Count; i++)
        {
            var current = s.Samples[i];
            if (current.At < at)
                continue;

            var previous = s.Samples[i - 1];
            var span = (current.At - previous.At).TotalSeconds;
            if (span <= 0)
                return current.Total;

            var ratio = (at - previous.At).TotalSeconds / span;
            return previous.Total + (current.Total - previous.Total) * ratio;
        }

        return s.Samples[^1].Total;
    }

    /// <summary>
    /// Setzt den Nullpunkt auf den aktuellen Stand: Ein neuer Kampf beginnt bei
    /// null, auch wenn IINACT seinen alten Encounter weiterzaehlt. Ohne das wuerde
    /// ein frischer Kampf mit dem aufgelaufenen Fremdschaden starten.
    /// </summary>
    public void MarkEncounterStart(DateTime at)
    {
        this.encounterStarted = true;
        this.encounterStartedAt = at;

        foreach (var s in this.series.Values)
        {
            s.Origin = s.HasSample ? s.Cumulative : null;
            s.Samples.Clear();
            s.Samples.Add((at - ZeroPointLead, 0));
        }
    }

    /// <summary>Verwirft alles, auch den zuletzt bekannten Stand.</summary>
    public void Reset()
    {
        this.series.Clear();
        this.encounterStarted = false;
    }
}
