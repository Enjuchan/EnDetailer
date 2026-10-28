using System;
using System.Collections.Generic;
using System.Linq;

namespace EnDetailer.Core;

/// <summary>
/// Fuehrt je Combatant einen Verlauf des kumulativen Schadens und leitet daraus
/// die Echtzeit-Rate ab.
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

    // Muss 8 tau der laengsten einstellbaren Reaktionszeit abdecken (30 s, also 120 s),
    // sonst fehlen dem Filter die aelteren Messpunkte.
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

    /// <summary>
    /// Echtzeit-Rate: Antwort einer Kaskade aus zwei gleichen Exponentialstufen auf
    /// die Schadensimpulse der Reihe. Jeder Zuwachs zwischen zwei Messpunkten wirkt
    /// mit dem Gewicht a / tau^2 * exp(-a / tau), a ist sein Alter in Sekunden. Das
    /// Gewicht ist bei Alter null gleich null und waechst stetig, daher springt der
    /// Wert bei neuen Treffern nicht, und die Rate faellt ohne Schaden weich gegen
    /// null. tau ist die halbe Reaktionszeit, weil die mittlere Verzoegerung der
    /// Kaskade 2 * tau betraegt.
    ///
    /// Eine Ausnahme: Der erste Zuwachs einer Reihe liegt zwischen dem Nullpunkt und
    /// dem ersten Datenpaket und ist irgendwann in diesem Intervall entstanden. Er
    /// wird in dessen Mitte gelegt; am Messpunkt selbst haette er das Alter null und
    /// damit kein Gewicht, die Zeile bliebe beim ersten Paket leer. Bei den
    /// folgenden Paketen wuerde eine solche Verschiebung den Wert bei jedem
    /// Eintreffen springen lassen.
    ///
    /// Beliebige Auswertungszeitpunkte sind zulaessig: Messpunkte nach now zaehlen
    /// nicht mit. Das braucht das Einfrieren zum Zeitpunkt des letzten Treffers.
    /// </summary>
    public double GetCurrentRate(string name, DateTime now, TimeSpan reaction, DateTime encounterStart)
    {
        if (!this.series.TryGetValue(name, out var s) || s.Samples.Count < 2)
            return 0;

        var tau = reaction.TotalSeconds / 2.0;
        if (tau <= 0)
            return 0;

        // Beitraege, die aelter als 8 tau sind, liegen unter 0,3 Promille.
        var horizon = 8 * tau;
        var sum = 0.0;

        for (var i = s.Samples.Count - 1; i >= 1; i--)
        {
            var current = s.Samples[i];
            if (current.At > now)
                continue;

            var previous = s.Samples[i - 1];
            var age = (now - current.At).TotalSeconds;
            // Der erste Zuwachs liegt in der Mitte seines Intervalls, sonst waere der
            // Wert beim ersten Datenpaket null. Hoechstens eine halbe Sendeperiode:
            // Taucht eine Zeile erst spaet auf (etwa "Limit Break"), ist ihr Schaden
            // gerade erst entstanden und nicht ueber den ganzen Kampf verteilt.
            if (i == 1)
                age += Math.Min((current.At - previous.At).TotalSeconds, ZeroPointLead.TotalSeconds) / 2.0;

            if (age > horizon)
                break;

            var increase = current.Total - previous.Total;
            sum += increase * (age / (tau * tau)) * Math.Exp(-age / tau);
        }

        // Am Kampfanfang deckt der Filter erst einen Teil seiner Antwort ab, ohne
        // Korrektur startet der Wert kuenstlich niedrig. Durch den abgedeckten Anteil
        // statt durch die verstrichene Zeit zu teilen vermeidet den Sprung aufs
        // Doppelte beim Opener. Jedes Paket trifft im Mittel eine halbe Sendeperiode
        // nach dem Schaden ein, der Filter hat also entsprechend weniger abgedeckt.
        // Untergrenze: Beim ersten Datenpaket ist keine Zeit seit Kampfbeginn
        // verstrichen, der Nullpunkt liegt aber ZeroPointLead davor.
        var x = Math.Max(
            ((now - (encounterStart - ZeroPointLead)) - ZeroPointLead / 2).TotalSeconds / tau,
            ZeroPointLead.TotalSeconds / tau);
        var covered = 1 - Math.Exp(-x) * (1 + x);

        return covered <= 0 ? 0 : sum / covered;
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
