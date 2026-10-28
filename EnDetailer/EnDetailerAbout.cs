using Dalamud.Interface;
using EnDetailer.Ui;

namespace EnDetailer;

/// <summary>Angaben für den About-Tab; Wirkung der Befehle steht auch als Hilfetext im Befehl selbst.</summary>
public static class EnDetailerAbout
{
    public const string ToggleEffect = "Shows or hides the meter.";
    public const string ConfigEffect = "Opens the settings.";
    public const string LockEffect = "Locks or unlocks the meter window.";
    public const string PreviewEffect = "Starts or stops the made-up party.";

    public static AboutInfo Create(string version) => new(
        "EnDetailer",
        version,
        "EnDetailer reads combat data from IINACT and shows the damage you are actually dealing right now, as a real-time value instead of the whole-fight average.",
        [
            new("/endetailer", ToggleEffect),
            new("/endetailer config", ConfigEffect),
            new("/endetailer lock", LockEffect),
            new("/endetailer preview", PreviewEffect),
        ],
        "AGPL-3.0",
        "EnDetailer - Copyright (c) 2026 Enjuchan",
        [new("Open GitHub", FontAwesomeIcon.ExternalLinkAlt, "https://github.com/Enjuchan/EnDetailer")]);
}
