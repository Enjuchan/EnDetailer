using System.Reflection;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using ECommons.Reflection;

namespace EnDetailer;

/// <summary>Required: muss da sein. AnyOf: mindestens eines der Gruppe. Conflict: soll nicht gleichzeitig laufen.</summary>
public enum DependencyKind
{
    Required,
    AnyOf,
    Conflict,
}

/// <param name="Repositories">Erste Adresse ist die bevorzugte, weitere sind Spiegel; leer bei Plugins ohne Install-Knopf. Für Plugins aus Dalamuds Haupt-Repository steht <see cref="Dependencies.MainRepository"/> darin.</param>
public sealed record Dependency(
    string DisplayName,
    string InternalName,
    LocText Purpose,
    string Website,
    string[] Repositories);

/// <param name="Title">Überschrift im Tab; bei AnyOf kommt die Zahl der geladenen Plugins dazu.</param>
public sealed record DependencyGroup(LocText Title, DependencyKind Kind, IReadOnlyList<Dependency> Items);

public enum InstallState { Missing, Installed, Loaded }

/// <summary>Fehler beim Installieren mit Text in beiden Sprachen, damit die Anzeige ihn übersetzen kann.</summary>
public sealed class InstallException(LocText text) : Exception(text.En)
{
    public LocText Text { get; } = text;
}

/// <summary>
/// Plugins, mit denen das Plugin zusammenarbeitet, und ihre Installation. Installiert wird wie in Questionable
/// über ECommons DalamudReflector: Repository in Dalamud eintragen, falls es fehlt, dann über Dalamuds internen
/// PluginManager installieren. Das greift per Reflection in Dalamud-Interna und kann nach Dalamud-Updates brechen.
/// </summary>
public sealed class Dependencies(IDalamudPluginInterface pluginInterface, IPluginLog log, IReadOnlyList<DependencyGroup> groups)
{
    /// <summary>Adresse von Dalamuds Haupt-Repository; sie ist dort fest eingebaut und gehört nicht in die Liste der Fremd-Repositories.</summary>
    public const string MainRepository = "https://kamori.goats.dev/Plugin/PluginMaster";

    public IReadOnlyList<DependencyGroup> Groups { get; } = groups;

    // Threadsicher, weil die Installation im Hintergrund läuft, während das Fenster liest.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> installing = new();

    /// <summary>Letzte Meldung pro Plugin für die Anzeige, etwa ein Fehler beim Installieren.</summary>
    public System.Collections.Concurrent.ConcurrentDictionary<string, LocText> Messages { get; } = new();

    public bool IsInstalling(Dependency dependency) => installing.ContainsKey(dependency.InternalName);

    public (InstallState State, string? Version) StateOf(Dependency dependency)
    {
        var plugin = pluginInterface.InstalledPlugins
            .FirstOrDefault(p => string.Equals(p.InternalName, dependency.InternalName, StringComparison.OrdinalIgnoreCase));

        if (plugin is null)
            return (InstallState.Missing, null);

        return (plugin.IsLoaded ? InstallState.Loaded : InstallState.Installed, plugin.Version.ToString());
    }

    /// <summary>Installiert ein fehlendes Plugin; ist es installiert, aber aus, öffnet es den Plugin-Installer.</summary>
    public void Install(Dependency dependency)
    {
        if (StateOf(dependency).State == InstallState.Installed)
        {
            pluginInterface.OpenPluginInstallerTo(Dalamud.Interface.PluginInstallerOpenKind.InstalledPlugins, dependency.DisplayName);
            return;
        }

        if (dependency.Repositories.Length == 0 || !installing.TryAdd(dependency.InternalName, true))
            return;

        Messages[dependency.InternalName] = new("Installing…", "Installiere…");
        _ = InstallAsync(dependency);
    }

    private async Task InstallAsync(Dependency dependency)
    {
        try
        {
            var loaded = await InstallFromRepositoryAsync(dependency).ConfigureAwait(false);
            Messages[dependency.InternalName] = loaded
                ? new("Installed.", "Installiert.")
                : new("Installed, but not loaded. Check the plugin installer.", "Installiert, aber nicht geladen. Prüfe den Plugin-Installer.");
            log.Information("{Plugin} installiert, geladen: {Loaded}.", dependency.DisplayName, loaded);
        }
        catch (Exception e)
        {
            // Fehler beim Installieren gehören vor den Nutzer, nicht nur ins Log, weil er sonst nur einen hängenden Knopf sieht.
            var baseException = e.GetBaseException();
            var reason = baseException is InstallException known ? known.Text : new LocText(baseException.Message, baseException.Message);
            Messages[dependency.InternalName] = new($"Couldn't install: {reason.En}", $"Installieren fehlgeschlagen: {reason.De}");
            log.Error(e, "Installieren von {Plugin} fehlgeschlagen.", dependency.DisplayName);
        }
        finally
        {
            installing.TryRemove(dependency.InternalName, out _);
        }
    }

    private static async Task<bool> InstallFromRepositoryAsync(Dependency dependency)
    {
        var repoUrl = dependency.Repositories.FirstOrDefault(r => r == MainRepository || DalamudReflector.HasRepo(r)) ?? dependency.Repositories[0];

        var manifests = await DalamudReflector.GetPluginMaster(repoUrl).ConfigureAwait(false);
        if (manifests is null || manifests.Count == 0)
            throw new InstallException(new($"No plugins found in {repoUrl}.", $"Keine Plugins in {repoUrl} gefunden."));

        var manifest = manifests.FirstOrDefault(m => string.Equals((string)m.GetFoP("InternalName"), dependency.InternalName, StringComparison.Ordinal))
            ?? throw new InstallException(new($"{dependency.DisplayName} isn't listed in {repoUrl}.", $"{dependency.DisplayName} steht nicht in {repoUrl}."));

        // Das Haupt-Repository ist immer da; als Fremd-Repository eingetragen würde es doppelt geführt.
        if (repoUrl != MainRepository && !DalamudReflector.HasRepo(repoUrl))
            DalamudReflector.AddRepo(repoUrl, enabled: true);

        DalamudReflector.SaveDalamudConfig();
        DalamudReflector.ReloadPluginMasters();

        var pluginManager = DalamudReflector.GetPluginManager();
        var install = pluginManager.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(m => m.Name == "InstallPluginAsync")
            .OrderBy(m => m.GetParameters().Length)
            .FirstOrDefault()
            ?? throw new InstallException(new("Dalamud's PluginManager.InstallPluginAsync wasn't found.", "Dalamuds PluginManager.InstallPluginAsync wurde nicht gefunden."));

        if (install.Invoke(pluginManager, BindArguments(install, manifest)) is not Task task)
            throw new InstallException(new("InstallPluginAsync didn't return a task.", "InstallPluginAsync hat keinen Task geliefert."));

        await task.ConfigureAwait(false);
        var result = task.GetFoP("Result");
        return result is not null && (bool)result.GetFoP("IsLoaded");
    }

    /// <summary>Belegt die Parameter von InstallPluginAsync nach Namen, wie Questionable es tut.</summary>
    private static object?[] BindArguments(MethodInfo method, object manifest) =>
        [.. method.GetParameters().Select(p => p.Name switch
        {
            "repoManifest" => manifest,
            "useTesting" => false,
            "reason" => Dalamud.Plugin.PluginLoadReason.Installer,
            _ => p.HasDefaultValue ? p.DefaultValue : p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null,
        })];
}
