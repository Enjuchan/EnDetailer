using System;

namespace EnDetailer.Core;

public enum EncounterState
{
    Idle,
    Running,
    Ended
}

/// <summary>
/// Bestimmt die Encounter-Grenzen selbst, statt sie von IINACT zu uebernehmen.
/// Cutscenes und Bosspausen beenden einen Kampf nicht.
/// </summary>
public sealed class EncounterTracker(IGameConditions conditions)
{
    private double currentDamage;
    private double lastEvaluatedDamage;
    private DateTime? outOfCombatSince;
    private uint? zoneAtStart;

    public EncounterState State { get; private set; } = EncounterState.Idle;
    public DateTime? StartedAt { get; private set; }
    public DateTime? EndedAt { get; private set; }

    public TimeSpan GracePeriod { get; set; } = TimeSpan.FromSeconds(10);
    public bool ResetOnZoneChange { get; set; }

    /// <summary>Ein neuer Kampf beginnt. Empfaenger muessen ihre Daten verwerfen.</summary>
    public event Action? EncounterStarted;

    /// <summary>Der Kampf ist vorbei. Die Anzeige friert ein.</summary>
    public event Action? EncounterEnded;

    /// <summary>Zeitpunkt des letzten beobachteten Schadens.</summary>
    public DateTime? LastDamageAt { get; private set; }

    /// <summary>
    /// Wanduhrzeit des Kampfes, inklusive Cutscenes und Pausen. Das ist die Zeit,
    /// die in der Kopfzeile laeuft.
    /// </summary>
    public TimeSpan Duration =>
        this.StartedAt is null
            ? TimeSpan.Zero
            : (this.EndedAt ?? DateTime.UtcNow) - this.StartedAt.Value;

    /// <summary>
    /// Zeit bis zum letzten Treffer. Grundlage fuer den Encounter-DPS, denn ACT
    /// und damit FFLogs rechnen genauso: Ohne Kampfgeschehen waechst der Nenner
    /// nicht weiter. Mit der Wanduhr laege der Wert systematisch zu niedrig und
    /// waere nicht mehr mit dem vergleichbar, was andere anzeigen.
    /// </summary>
    public TimeSpan ActiveDuration =>
        this.StartedAt is null
            ? TimeSpan.Zero
            : (this.LastDamageAt ?? this.StartedAt.Value) - this.StartedAt.Value;

    /// <summary>
    /// Neue Daten von IINACT. Kommt nur, solange der Spieler im Kampf ist.
    /// </summary>
    public void Update(double totalPartyDamage, DateTime now)
    {
        this.currentDamage = totalPartyDamage;
        Evaluate(now);
    }

    /// <summary>
    /// Regelmaessiger Puls aus dem Spiel, unabhaengig von IINACT. Zwingend noetig:
    /// IINACT verstummt beim Verlassen des Kampfes, und genau dann muss die
    /// Karenzzeit ablaufen koennen. Ohne diesen Aufruf endet kein Encounter je.
    /// </summary>
    public void Tick(DateTime now) => Evaluate(now);

    private void Evaluate(DateTime now)
    {
        var totalPartyDamage = this.currentDamage;

        switch (this.State)
        {
            case EncounterState.Idle:
            case EncounterState.Ended:
                // Ein neuer Kampf beginnt, sobald Schaden dazukommt. Bewusst ohne
                // Pruefung auf InCombat: das Spiel setzt dieses Flag erst gut eine
                // Sekunde nach dem ersten Treffer, und bis dahin waere der bereits
                // gemeldete Schaden zum Nullpunkt geworden - der erste Treffer
                // fehlte dann in der Anzeige. Wenn Schaden gemeldet wird, laeuft
                // ohnehin ein Kampf. Fuer das Ende bleibt InCombat massgeblich.
                // Ein blosser Tick ohne neue Daten aendert den Wert nicht und
                // startet deshalb auch nichts.
                if (totalPartyDamage > 0 && totalPartyDamage != this.lastEvaluatedDamage)
                {
                    this.State = EncounterState.Running;
                    this.StartedAt = now;
                    this.EndedAt = null;
                    this.outOfCombatSince = null;
                    this.zoneAtStart = conditions.ZoneId;
                    this.LastDamageAt = now;
                    this.EncounterStarted?.Invoke();
                }

                break;

            case EncounterState.Running:
                if (this.ResetOnZoneChange && this.zoneAtStart is { } zone && conditions.ZoneId != zone)
                {
                    End(now);
                    break;
                }

                if (conditions.InCombat || conditions.InCutscene)
                {
                    // Waehrend einer Cutscene laeuft die Karenzzeit nicht.
                    this.outOfCombatSince = null;
                }
                else
                {
                    this.outOfCombatSince ??= now;
                    if (now - this.outOfCombatSince.Value >= this.GracePeriod)
                        End(now);
                }

                if (totalPartyDamage > this.lastEvaluatedDamage)
                    this.LastDamageAt = now;

                break;
        }

        this.lastEvaluatedDamage = totalPartyDamage;
    }

    private void End(DateTime now)
    {
        this.State = EncounterState.Ended;
        this.EndedAt = now;
        this.outOfCombatSince = null;
        this.EncounterEnded?.Invoke();
    }

    public void ForceReset()
    {
        this.State = EncounterState.Idle;
        this.StartedAt = null;
        this.EndedAt = null;
        this.outOfCombatSince = null;
        this.LastDamageAt = null;
        this.currentDamage = 0;
        this.lastEvaluatedDamage = 0;
        this.EncounterStarted?.Invoke();
    }
}
