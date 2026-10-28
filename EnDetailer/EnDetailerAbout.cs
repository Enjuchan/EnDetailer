using Dalamud.Interface;
using EnDetailer.Ui;

namespace EnDetailer;

/// <summary>Angaben für den About-Tab; Wirkung der Befehle steht auch als Hilfetext im Befehl selbst.</summary>
public static class EnDetailerAbout
{
    public static readonly LocText ToggleEffect = new("Shows or hides the meter.", "Zeigt oder versteckt das Meter.");
    public static readonly LocText ConfigEffect = new("Opens the settings.", "Öffnet die Einstellungen.");
    public static readonly LocText LockEffect = new("Locks or unlocks the meter window.", "Sperrt oder entsperrt das Meter-Fenster.");
    public static readonly LocText PreviewEffect = new("Starts or stops the made-up party.", "Startet oder beendet die erfundene Gruppe.");

    public static AboutInfo Create(string version) => new(
        "EnDetailer",
        version,
        new("EnDetailer reads combat data from IINACT and shows the damage you are actually dealing right now, as a real-time value instead of the whole-fight average.",
            "EnDetailer liest Kampfdaten aus IINACT und zeigt den Schaden, den du gerade wirklich machst, als Echtzeitwert statt als Durchschnitt über den ganzen Kampf."),
        [
            new("/endetailer", ToggleEffect),
            new("/endetailer config", ConfigEffect),
            new("/endetailer lock", LockEffect),
            new("/endetailer preview", PreviewEffect),
        ],
        "AGPL-3.0",
        "EnDetailer - Copyright (c) 2026 Enjuchan",
        [new(new("Open GitHub", "GitHub öffnen"), FontAwesomeIcon.ExternalLinkAlt, "https://github.com/Enjuchan/EnDetailer")]);
}
