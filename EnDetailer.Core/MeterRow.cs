namespace EnDetailer.Core;

public sealed record MeterRow(
    string Name,
    Job Job,
    double TotalDamage,
    double RollingDps,
    double EncounterDps,
    double CritPercent,
    double DirectHitPercent);
