using Dalamud.Plugin;

namespace EnDetailer;

public enum Language { English, German }

/// <summary>Ein Text auf Englisch und Deutsch, für Daten, die vor der Anzeige gebaut werden; ToString liefert die aktuelle Sprache.</summary>
public readonly record struct LocText(string En, string De)
{
    public override string ToString() => Loc.T(En, De);
}

/// <summary>
/// Sprache des Plugins. Ohne gespeicherte Wahl folgt sie der Sprache von Dalamud (Deutsch bei "de", sonst Englisch),
/// auch bei späteren Wechseln; wer über den Schalter im Kopfband umschaltet, behält die eigene Wahl.
/// </summary>
public static class Loc
{
    private static IDalamudPluginInterface? pluginInterface;
    private static bool followsDalamud = true;

    public static Language Current { get; private set; }

    /// <summary>Wird nach jedem Sprachwechsel ausgelöst, etwa um Befehlshilfen neu anzumelden.</summary>
    public static event Action? Changed;

    /// <summary>Text in der aktuellen Sprache.</summary>
    public static string T(string en, string de) => Current == Language.German ? de : en;

    public static string T(LocText text) => T(text.En, text.De);

    /// <param name="saved">Gespeicherte Wahl ("de" oder "en"); null heißt wie Dalamud.</param>
    public static void Init(IDalamudPluginInterface pi, string? saved)
    {
        Dispose();
        pluginInterface = pi;
        followsDalamud = saved is null;
        Current = FromCode(saved ?? pi.UiLanguage);
        pi.LanguageChanged += OnDalamudLanguageChanged;
    }

    public static void Dispose()
    {
        if (pluginInterface is not null)
            pluginInterface.LanguageChanged -= OnDalamudLanguageChanged;

        pluginInterface = null;
        Changed = null;
    }

    /// <summary>Wechselt die Sprache und gibt die Wahl ("de" oder "en") an <paramref name="save"/>; behält sie ab dann gegenüber Dalamud.</summary>
    public static void Toggle(Action<string> save)
    {
        followsDalamud = false;
        var next = Current == Language.German ? Language.English : Language.German;
        Set(next);
        save(Code(next));
    }

    private static void OnDalamudLanguageChanged(string code)
    {
        if (followsDalamud)
            Set(FromCode(code));
    }

    private static void Set(Language language)
    {
        if (language == Current)
            return;

        Current = language;
        Changed?.Invoke();
    }

    private static Language FromCode(string? code) =>
        string.Equals(code, "de", StringComparison.OrdinalIgnoreCase) ? Language.German : Language.English;

    private static string Code(Language language) => language == Language.German ? "de" : "en";
}
