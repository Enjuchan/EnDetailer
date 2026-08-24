using System;
using System.Collections.Generic;

namespace EnDetailer.Core;

public sealed record CombatantSnapshot(
    string Name,
    Job Job,
    double TotalDamage,
    double EncounterDps,
    double CritPercent,
    double DirectHitPercent,
    double TotalHealing,
    double EncounterHps,
    double OverhealPercent,
    double TotalDamageTaken,
    int Deaths);

public sealed record CombatSnapshot(
    DateTime At,
    string Title,
    string DurationRaw,
    bool IsActive,
    IReadOnlyList<CombatantSnapshot> Combatants);
