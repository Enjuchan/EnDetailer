namespace EnDetailer;

/// <summary>EnDetailers Abhängigkeiten für den Dependencies-Tab.</summary>
public static class EnDetailerDependencies
{
    public static readonly IReadOnlyList<DependencyGroup> Groups =
    [
        new(new("Required", "Erforderlich"), DependencyKind.Required,
        [
            // Repository-Adresse am 2026-10-28 aus InstalledFromUrl im Manifest der installierten IINACT-Version gelesen.
            new("IINACT", "IINACT", new("Supplies the combat data.", "Liefert die Kampfdaten."), "https://github.com/marzent/IINACT",
                ["https://raw.githubusercontent.com/marzent/IINACT/main/repo.json"]),
        ]),
    ];
}
