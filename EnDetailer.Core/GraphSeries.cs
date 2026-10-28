using System;

namespace EnDetailer.Core;

/// <summary>
/// Der Verlauf einer Messgroesse: ein Wert je Zeitschritt, der Kampf von Anfang an. Punkt k gehoert zur
/// Kampfzeit (k + 1) * StepSeconds. Nach aussen nur lesbar; geschrieben wird ueber <see cref="FightGraph"/>.
/// </summary>
public sealed class GraphSeries
{
    public const int MaxPoints = 1800;

    private readonly double[] values;

    internal GraphSeries(int capacity) => this.values = new double[capacity];

    public int Count { get; private set; }

    /// <summary>Sekunden zwischen zwei Punkten; verdoppelt sich, wenn die Obergrenze erreicht ist.</summary>
    public int StepSeconds { get; private set; } = 1;

    /// <summary>Hoechster Wert der Reihe, 0 bei leerer Reihe.</summary>
    public double Max { get; private set; }

    /// <summary>Aendert sich bei jeder Veraenderung der Reihe; Zwischenspeicher der Darstellung pruefen darauf.</summary>
    public int Version { get; private set; }

    public double this[int index] => this.values[index];

    internal void Add(double value)
    {
        this.values[this.Count++] = value;
        this.Max = Math.Max(this.Max, value);
        this.Version++;

        // Gerade Obergrenze, damit sich immer ganze Paare zusammenfassen lassen.
        if (this.Count == MaxPoints)
            Merge();
    }

    private void Merge()
    {
        var half = this.Count / 2;
        for (var i = 0; i < half; i++)
            this.values[i] = (this.values[2 * i] + this.values[2 * i + 1]) / 2;

        this.Count = half;
        this.StepSeconds *= 2;
        RecomputeMax();
    }

    internal void Truncate(int keep)
    {
        if (keep >= this.Count)
            return;

        this.Count = Math.Max(0, keep);
        RecomputeMax();
        this.Version++;
    }

    internal void Clear()
    {
        this.Count = 0;
        this.StepSeconds = 1;
        this.Max = 0;
        this.Version++;
    }

    internal GraphSeries Copy()
    {
        var copy = new GraphSeries(this.Count);
        Array.Copy(this.values, copy.values, this.Count);
        copy.Count = this.Count;
        copy.StepSeconds = this.StepSeconds;
        copy.Max = this.Max;
        copy.Version = 1;
        return copy;
    }

    private void RecomputeMax()
    {
        var max = 0d;
        for (var i = 0; i < this.Count; i++)
            max = Math.Max(max, this.values[i]);

        this.Max = max;
        this.Version++;
    }
}
