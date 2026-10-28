using System;

namespace EnDetailer.Core;

/// <summary>
/// Verlauf des Echtzeitwerts der ganzen Gruppe fuer alle drei Messgroessen. Der Plugin-Puls traegt
/// hoechstens einmal je Sekunde einen Punkt ein; das Fenster liest nur. Schreibzugriffe sind gesperrt,
/// weil ein neuer Kampf auch vom IPC-Thread gemeldet werden kann.
/// </summary>
public sealed class FightGraph
{
    private readonly object gate = new();
    private readonly GraphSeries damage;
    private readonly GraphSeries healing;
    private readonly GraphSeries taken;

    public FightGraph()
        : this(new GraphSeries(GraphSeries.MaxPoints), new GraphSeries(GraphSeries.MaxPoints), new GraphSeries(GraphSeries.MaxPoints))
    {
    }

    private FightGraph(GraphSeries damage, GraphSeries healing, GraphSeries taken)
    {
        this.damage = damage;
        this.healing = healing;
        this.taken = taken;
    }

    internal static FightGraph Empty() => new(new GraphSeries(0), new GraphSeries(0), new GraphSeries(0));

    public GraphSeries For(MeterMetric metric) => metric switch
    {
        MeterMetric.Healing => this.healing,
        MeterMetric.DamageTaken => this.taken,
        _ => this.damage
    };

    /// <summary>Ob ein weiterer Punkt faellig ist; guenstige Vorpruefung, damit die Summen nur dann berechnet werden.</summary>
    public bool Due(TimeSpan elapsed) =>
        elapsed.TotalSeconds >= (this.damage.Count + 1.0) * this.damage.StepSeconds;

    /// <summary>
    /// Traegt die Werte zur Kampfzeit <paramref name="elapsed"/> ein. Versaeumte Sekunden, etwa nach einem
    /// Ruckler, werden mit denselben Werten aufgefuellt, damit die Zeitachse stimmt.
    /// </summary>
    public void Add(TimeSpan elapsed, double damageValue, double healingValue, double takenValue)
    {
        lock (this.gate)
        {
            while (Due(elapsed))
            {
                this.damage.Add(damageValue);
                this.healing.Add(healingValue);
                this.taken.Add(takenValue);
            }
        }
    }

    /// <summary>Behaelt nur die Punkte bis zur Kampfzeit <paramref name="upTo"/>, fuer den Stand beim letzten Treffer.</summary>
    public void Truncate(TimeSpan upTo)
    {
        lock (this.gate)
        {
            var keep = (int)Math.Floor(Math.Max(0, upTo.TotalSeconds) / this.damage.StepSeconds);
            this.damage.Truncate(keep);
            this.healing.Truncate(keep);
            this.taken.Truncate(keep);
        }
    }

    /// <summary>Beginnt eine neue Aufzeichnung. Die Objekte bleiben dieselben, nur ihre Version aendert sich.</summary>
    public void Reset()
    {
        lock (this.gate)
        {
            this.damage.Clear();
            this.healing.Clear();
            this.taken.Clear();
        }
    }

    /// <summary>Unveraenderliche Kopie des jetzigen Stands, unabhaengig von der weiteren Aufzeichnung.</summary>
    public FightGraph Copy()
    {
        lock (this.gate)
            return new FightGraph(this.damage.Copy(), this.healing.Copy(), this.taken.Copy());
    }
}
