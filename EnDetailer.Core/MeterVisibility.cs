using System;

namespace EnDetailer.Core;

public enum ShowMode
{
    Always,
    InCombat,
    InDuty
}

/// <summary>
/// Entscheidet, ob das Meter gerade im Bild steht. Titel- und Ladebildschirm prueft
/// das Plugin vorher selbst; hier geht es nur um die Wahl des Spielers.
/// </summary>
public sealed class MeterVisibility(IGameConditions conditions)
{
    private DateTime? lastInCombat;

    public ShowMode Mode { get; set; } = ShowMode.Always;

    /// <summary>Wie lange das Meter im Modus InCombat nach Kampfende stehen bleibt, damit das Ergebnis lesbar ist.</summary>
    public TimeSpan AfterCombat { get; set; } = TimeSpan.FromSeconds(15);

    public bool HideInCutscenes { get; set; }

    /// <summary>
    /// Jeden Frame aufrufen. Der Kampf wird unabhaengig vom Modus mitgeschrieben,
    /// damit ein Wechsel auf InCombat direkt nach einem Kampf den Nachlauf kennt.
    /// </summary>
    public bool IsVisible(DateTime now)
    {
        if (conditions.InCombat)
            this.lastInCombat = now;

        if (this.HideInCutscenes && conditions.InCutscene)
            return false;

        return this.Mode switch
        {
            ShowMode.InCombat => this.lastInCombat is { } at && now - at <= this.AfterCombat,
            ShowMode.InDuty => conditions.InDuty,
            _ => true
        };
    }
}
