using Dalamud.Plugin.Services;

namespace EnDetailer;

/// <summary>Ein Eintrag im Changelog. <paramref name="Date"/> ist das Datum des Release-Tags, etwa 2026-10-28.</summary>
public sealed record ChangelogEntry(string Version, string Date, IReadOnlyList<LocText> Points);

/// <summary>Inhalte des Willkommensfensters, die je Plugin verschieden sind.</summary>
/// <param name="DrawSettings">Zeichnet die wichtigsten Einstellungen in eine Karte der übergebenen Innenbreite.</param>
public sealed record WelcomeContent(
    string Name,
    string Version,
    LocText Purpose,
    IReadOnlyList<LocText> Steps,
    Action<float> DrawSettings);

public enum WelcomeAction
{
    None,
    ShowWelcome,
    ShowChangelog,
}

/// <summary>Reine Regeln für Erkennung und Markierung, getrennt von Dalamud, damit sie testbar sind.</summary>
public static class WelcomeRules
{
    /// <summary>
    /// Ohne gespeicherte Konfiguration ist es eine Neuinstallation. Sonst zeigt das Plugin das Changelog,
    /// wenn die gespeicherte Version fehlt, nicht lesbar oder älter als die laufende ist.
    /// </summary>
    public static WelcomeAction Decide(bool configExists, string? seenVersion, string runningVersion)
    {
        if (!configExists)
            return WelcomeAction.ShowWelcome;

        if (!TryParse(runningVersion, out var running))
            return WelcomeAction.None;

        if (!TryParse(seenVersion, out var seen))
            return WelcomeAction.ShowChangelog;

        return seen < running ? WelcomeAction.ShowChangelog : WelcomeAction.None;
    }

    /// <summary>
    /// Ab welcher Version die Einträge als neu gelten: bei einer Neuinstallation ab der laufenden (nichts ist neu),
    /// sonst ab der gespeicherten. Null heißt, dass nur die laufende Version neu ist.
    /// </summary>
    public static string? Baseline(bool configExists, string? seenVersion, string runningVersion) =>
        configExists ? seenVersion : runningVersion;

    /// <summary>Ob ein Eintrag neuer ist als die Grundlinie. Fehlt sie oder ist sie nicht lesbar, gilt nur die laufende Version als neu.</summary>
    public static bool IsNew(string entryVersion, string? baseline, string runningVersion)
    {
        if (!TryParse(entryVersion, out var entry))
            return false;

        if (!TryParse(baseline, out var from))
            return TryParse(runningVersion, out var running) && entry == running;

        return entry > from;
    }

    /// <summary>Liest "0.2.2.0" oder "v0.2.2.0"; fehlende Stellen zählen als 0, damit "0.2" und "0.2.0.0" gleich sind.</summary>
    public static bool TryParse(string? text, out Version version)
    {
        version = new Version();
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var trimmed = text.Trim();
        if (trimmed.StartsWith('v') || trimmed.StartsWith('V'))
            trimmed = trimmed[1..];

        if (!Version.TryParse(trimmed, out var parsed))
            return false;

        version = new Version(parsed.Major, parsed.Minor, Math.Max(parsed.Build, 0), Math.Max(parsed.Revision, 0));
        return true;
    }
}

/// <summary>
/// Entscheidet beim Laden, ob das Willkommens- oder das Changelog-Fenster erscheint, und öffnet es nach dem Login.
/// Mit dem Öffnen wird die laufende Version gespeichert, damit es beim nächsten Start nicht wiederkommt.
/// </summary>
public sealed class Welcome : IDisposable
{
    private readonly IClientState clientState;
    private readonly string running;
    private readonly Action<string> saveSeen;
    private Action? open;

    /// <param name="configExists">Ob Dalamud schon eine Konfiguration des Plugins hatte (GetPluginConfig war nicht null).</param>
    /// <param name="seenVersion">Gespeicherte zuletzt gesehene Version.</param>
    /// <param name="saveSeen">Speichert die laufende Version in der Konfiguration.</param>
    public Welcome(IClientState clientState, string runningVersion, bool configExists, string? seenVersion, Action<string> saveSeen)
    {
        this.clientState = clientState;
        running = runningVersion;
        this.saveSeen = saveSeen;
        Pending = WelcomeRules.Decide(configExists, seenVersion, runningVersion);
        Baseline = WelcomeRules.Baseline(configExists, seenVersion, runningVersion);
    }

    public WelcomeAction Pending { get; private set; }

    /// <summary>Grundlinie für die Markierung "Neu" im Changelog; bleibt bis zum Ende der Sitzung.</summary>
    public string? Baseline { get; }

    public bool IsNew(string entryVersion) => WelcomeRules.IsNew(entryVersion, Baseline, running);

    /// <summary>Öffnet das passende Fenster, sobald ein Charakter eingeloggt ist, weil Fenster im Titelbildschirm untergehen.</summary>
    public void Start(Action openWelcome, Action openChangelog)
    {
        if (Pending == WelcomeAction.None)
            return;

        open = Pending == WelcomeAction.ShowWelcome ? openWelcome : openChangelog;
        if (clientState.IsLoggedIn)
            Show();
        else
            clientState.Login += Show;
    }

    private void Show()
    {
        clientState.Login -= Show;
        if (Pending == WelcomeAction.None)
            return;

        Pending = WelcomeAction.None;
        open?.Invoke();
        saveSeen(running);
    }

    public void Dispose() => clientState.Login -= Show;
}
