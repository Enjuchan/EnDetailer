using System;
using System.Collections.Generic;

namespace EnDetailer.Core;

/// <summary>
/// Erfundener Kampf einer vollen Gruppe fuer die Vorschau. Liefert Datenpakete in
/// derselben Form wie IINACT, damit sie durch dieselbe Rechnung laufen und Messungen
/// ohne Gruppe vergleichbar sind.
///
/// Jeder Spieler trifft im Takt eines GCD, mit Streuung und Krits; alle zwei Minuten
/// gibt es ein Burst-Fenster, damit der Echtzeitwert sichtbar steigt und faellt.
/// Die Streuung ist aus Spieler und Trefferzahl abgeleitet, also bei jedem Lauf gleich.
/// </summary>
public sealed class PreviewCombat
{
    /// <summary>Titel des erfundenen Kampfes; die Oberfläche zeigt ihn in der gewählten Sprache.</summary>
    public const string Title = "Preview";

    private const double Gcd = 2.5;
    private const double BurstCycle = 120;
    private const double BurstStart = 8;
    private const double BurstLength = 20;
    private const double BurstFactor = 1.6;

    private sealed class Member(
        string name, Job job, double dps, double hps, double dtps, double crit, double directHit,
        string hitName, string healName)
    {
        public readonly string HitName = hitName;
        public readonly string HealName = healName;
        public double MaxHit;
        public double MaxHeal;
        public readonly string Name = name;
        public readonly Job Job = job;
        public readonly double Dps = dps;
        public readonly double Hps = hps;
        public readonly double Dtps = dtps;
        public readonly double Crit = crit;
        public readonly double DirectHit = directHit;
        public double Damage;
        public double Healing;
        public double Taken;
        public int NextHit;
    }

    private readonly Member[] members;
    private readonly DateTime startedAt;

    public PreviewCombat(DateTime startedAt, string? localPlayerName)
    {
        this.startedAt = startedAt;
        this.members =
        [
            new("Preview Paladin", Job.PLD, 9200, 600, 5200, 22, 18, "Holy Spirit", "Clemency"),
            new("Preview Warrior", Job.WAR, 9800, 900, 4800, 24, 20, "Fell Cleave", "Bloodwhetting"),
            new("Preview White Mage", Job.WHM, 6100, 9800, 900, 21, 17, "Glare III", "Afflatus Rapture"),
            new("Preview Scholar", Job.SCH, 5800, 8700, 850, 20, 16, "Broil IV", "Adloquium"),
            new(localPlayerName ?? "Preview Dragoon", Job.DRG, 13900, 300, 1100, 26, 29, "Drakesbane", "Second Wind"),
            new("Preview Ninja", Job.NIN, 13400, 250, 1150, 25, 27, "Forked Raiju", "Second Wind"),
            new("Preview Bard", Job.BRD, 12600, 700, 1000, 24, 33, "Apex Arrow", "Troubadour"),
            new("Preview Black Mage", Job.BLM, 14600, 150, 950, 23, 26, "Flare Star", "Manaward")
        ];
    }

    /// <summary>
    /// Stand des Kampfes zum Zeitpunkt <paramref name="at"/>. Treffer werden bis
    /// dahin aufaddiert; die Werte steigen nur, wie bei IINACT.
    /// </summary>
    public CombatSnapshot Snapshot(DateTime at)
    {
        var elapsed = Math.Max(0, (at - this.startedAt).TotalSeconds);
        var combatants = new List<CombatantSnapshot>(this.members.Length);

        for (var i = 0; i < this.members.Length; i++)
        {
            var m = this.members[i];

            // Versetzter Takt, damit nicht alle im selben Moment treffen.
            var offset = i * 0.31;
            while (offset + m.NextHit * Gcd <= elapsed)
            {
                var hitAt = offset + m.NextHit * Gcd;
                var random = new Random(i * 100_003 + m.NextHit);
                var spread = 0.85 + random.NextDouble() * 0.3;
                var crit = random.NextDouble() < m.Crit / 100 ? 1.5 : 1.0;
                var burst = IsBurst(hitAt) ? BurstFactor : 1.0;

                var hit = m.Dps * Gcd * spread * crit * burst;
                var heal = m.Hps * Gcd * spread;

                m.Damage += hit;
                m.Healing += heal;
                m.MaxHit = Math.Max(m.MaxHit, hit);
                m.MaxHeal = Math.Max(m.MaxHeal, heal);
                m.Taken += m.Dtps * Gcd * spread;
                m.NextHit++;
            }

            var seconds = Math.Max(1, elapsed);
            var isHealer = m.Job is Job.WHM or Job.SCH;
            combatants.Add(new CombatantSnapshot(
                m.Name,
                m.Job,
                m.Damage,
                m.Damage / seconds,
                m.Crit,
                m.DirectHit,
                m.Healing,
                m.Healing / seconds,
                isHealer ? 34 : 12,
                m.Taken,
                0,
                m.Crit * m.DirectHit / 100,
                isHealer ? 22 : 15,
                m.Healing * ShieldShare(m.Job),
                m.Job == Job.WAR ? 12 : 0,
                m.Job == Job.PLD ? 21 : 0,
                m.Taken * 0.7,
                m.MaxHit > 0 ? m.HitName : string.Empty,
                Math.Round(m.MaxHit),
                m.MaxHeal > 0 ? m.HealName : string.Empty,
                Math.Round(m.MaxHeal)));
        }

        var duration = TimeSpan.FromSeconds(elapsed);
        return new CombatSnapshot(at, Title, duration.ToString(@"mm\:ss"), true, combatants);
    }

    /// <summary>Anteil der Heilung, der als Schild gebucht wird: Gelehrte viel, Tanks etwas, Rest nichts.</summary>
    private static double ShieldShare(Job job) => job switch
    {
        Job.SCH => 0.45,
        Job.WHM => 0.12,
        Job.PLD or Job.WAR => 0.05,
        _ => 0
    };

    private static bool IsBurst(double second)
    {
        var inCycle = second % BurstCycle;
        return inCycle >= BurstStart && inCycle < BurstStart + BurstLength;
    }
}
