namespace EnDetailer;

/// <summary>Alle Versionen mit Datum des Release-Tags, neueste zuerst.</summary>
public static class EnDetailerChangelog
{
    public static readonly IReadOnlyList<ChangelogEntry> Entries =
    [
        new("0.2.3.0", "2026-10-28",
        [
            new("A welcome window appears after you install EnDetailer, with the first steps and the key settings.",
                "Nach der Installation erscheint ein Willkommensfenster mit den ersten Schritten und den wichtigsten Einstellungen."),
            new("A changelog window appears after every update and lists all versions; new ones are marked.",
                "Nach jedem Update erscheint ein Changelog-Fenster mit allen Versionen; neue sind markiert."),
            new("Both windows can be opened again from the About tab.",
                "Beide Fenster lassen sich im About-Tab erneut öffnen."),
            new("The language switch moved from the title bar into the header band, away from the window buttons.",
                "Der Sprachschalter sitzt jetzt im Kopfband statt in der Titelleiste, weg von den Fensterknöpfen."),
        ]),
        new("0.2.2.0", "2026-10-28",
        [
            new("EnDetailer is available in German and English. A language button in the title bar switches instantly.",
                "EnDetailer gibt es auf Deutsch und Englisch. Ein Sprach-Knopf in der Titelleiste wechselt sofort."),
            new("It starts in Dalamud's language until you choose one yourself.",
                "Es startet in der Sprache von Dalamud, bis du selbst eine wählst."),
            new("The tab bar switches to icons when the window is too narrow.",
                "Die Tab-Leiste schaltet auf Icons um, wenn das Fenster zu schmal ist."),
        ]),
        new("0.2.1.0", "2026-10-28",
        [
            new("New Dependencies tab with an IINACT status card and an install button.",
                "Neuer Abhängigkeiten-Tab mit einer IINACT-Statuskarte und einem Install-Knopf."),
            new("New About tab with description, commands and license.",
                "Neuer About-Tab mit Beschreibung, Befehlen und Lizenz."),
            new("A Diagnostics switch also shows the draw time of the settings window.",
                "Der Diagnose-Schalter zeigt auch die Zeichenzeit des Einstellungsfensters."),
            new("Nothing is drawn during loading screens.",
                "In Ladebildschirmen wird nichts gezeichnet."),
        ]),
        new("0.2.0.0", "2026-10-28",
        [
            new("New look in the shared En style, with a one-line header showing the IINACT status and the metric.",
                "Neues Aussehen im gemeinsamen En-Stil, mit einer Kopfzeile, die den IINACT-Status und die Messgröße zeigt."),
            new("The current rate is calculated anew and follows your damage over an adjustable reaction time.",
                "Die aktuelle Rate wird neu berechnet und folgt deinem Schaden über eine einstellbare Reaktionszeit."),
            new("A fight graph, details on hover and a history of your last ten fights.",
                "Ein Kampfgraph, Details beim Überfahren und ein Verlauf deiner letzten zehn Kämpfe."),
            new("Choose when the meter shows: always, in combat or in duty. It can hide in cutscenes.",
                "Wähle, wann das Meter erscheint: immer, im Kampf oder in der Instanz. In Cutscenes kann es sich verbergen."),
            new("A preview with a made-up party lets you try the meter without a fight.",
                "Eine Vorschau mit erfundener Gruppe lässt dich das Meter ohne Kampf ausprobieren."),
        ]),
        new("0.1.2.0", "2026-08-24",
        [
            new("The meter shows healing and damage taken as well as damage.",
                "Das Meter zeigt neben dem Schaden auch Heilung und erlittenen Schaden."),
            new("A fight freezes at your last hit, not when combat ends.",
                "Ein Kampf friert beim letzten Treffer ein, nicht erst am Ende des Kampfes."),
        ]),
        new("0.1.1.0", "2026-08-24",
        [
            new("A DPS meter for FFXIV that reads its data from IINACT and shows a rolling DPS for you and your party.",
                "Ein DPS-Meter für FFXIV, das seine Daten von IINACT liest und ein gleitendes DPS für dich und deine Gruppe zeigt."),
            new("Fixed an empty display on the first hit of a fight.",
                "Eine leere Anzeige beim ersten Treffer eines Kampfes ist behoben."),
        ]),
    ];
}
