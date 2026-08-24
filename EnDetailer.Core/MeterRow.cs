namespace EnDetailer.Core;

/// <summary>
/// Was gemessen wird. Die Zeitbasis - laufendes Fenster oder Encounter-Durchschnitt -
/// ist davon unabhaengig und steckt in <see cref="MeterRow"/> in beiden Feldern.
/// </summary>
public enum MeterMetric
{
    Damage,
    Healing,
    DamageTaken
}

/// <summary>
/// Eine Zeile der Anzeige. <see cref="Total"/> und die beiden Raten beziehen sich auf
/// die gerade gewaehlte Metrik; die uebrigen Felder sind Zusatzspalten, die nur zu
/// bestimmten Metriken passen.
/// </summary>
public sealed record MeterRow(
    string Name,
    Job Job,
    double Total,
    double RollingRate,
    double EncounterRate,
    double CritPercent,
    double DirectHitPercent,
    double OverhealPercent);
