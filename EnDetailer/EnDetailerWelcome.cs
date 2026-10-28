namespace EnDetailer;

/// <summary>Inhalte des Willkommensfensters: Zweck, erste Schritte und die wichtigsten Einstellungen.</summary>
public static class EnDetailerWelcome
{
    public static WelcomeContent Create(string version, Configuration config) => new(
        "EnDetailer",
        version,
        new("EnDetailer is a DPS meter that shows what you and your party deal, heal and take in a fight. The current rate follows your damage over a short reaction time, so bursts show up as they happen. It reads its data from IINACT.",
            "EnDetailer ist ein DPS-Meter, das zeigt, wie viel du und deine Gruppe in einem Kampf austeilt, heilt und einsteckt. Die aktuelle Rate folgt deinem Schaden über eine kurze Reaktionszeit, sodass Bursts sofort sichtbar werden. Die Daten kommen von IINACT."),
        [
            new("Make sure IINACT is installed. The list below shows whether it is missing and installs it for you.",
                "Stell sicher, dass IINACT installiert ist. Die Liste unten zeigt, ob es fehlt, und installiert es für dich."),
            new("In IINACT, turn off 'End encounter automatically after leaving combat'. EnDetailer decides when a fight ends on its own.",
                "Schalte in IINACT die Option 'End encounter automatically after leaving combat' aus. EnDetailer entscheidet selbst, wann ein Kampf endet."),
            new("Check the settings below. Use the preview in the settings window to see the meter with a made-up party, or step into a fight.",
                "Prüfe die Einstellungen unten. Mit der Vorschau im Einstellungsfenster siehst du das Meter mit einer erfundenen Gruppe, oder du gehst einfach in einen Kampf."),
        ],
        width =>
        {
            SettingsControls.DisplayModeRow(config, width);
            SettingsControls.ShowModeRow(config, width);
            SettingsControls.LockRow(config, width);
        });
}
