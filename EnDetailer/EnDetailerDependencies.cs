namespace EnDetailer;

/// <summary>EnDetailers Abhängigkeiten für den Dependencies-Tab.</summary>
public static class EnDetailerDependencies
{
    public static readonly IReadOnlyList<DependencyGroup> Groups =
    [
        new("Required", DependencyKind.Required,
        [
            // Repository-Adresse am 2026-10-28 aus InstalledFromUrl im Manifest der installierten IINACT-Version gelesen.
            new("IINACT", "IINACT", "Supplies the combat data.", "https://github.com/marzent/IINACT",
                ["https://raw.githubusercontent.com/marzent/IINACT/main/repo.json"]),
        ]),
    ];
}
