using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace EnDetailer.Ui;

/// <summary>Inhalt des Dependencies-Tabs: eine Karte je Abhängigkeit, gruppiert, mit Hinweiszeile.</summary>
public static class DependenciesView
{
    public static void Draw(Dependencies dependencies)
    {
        var first = true;
        foreach (var group in dependencies.Groups)
        {
            if (group.Items.Count == 0)
                continue;

            if (!first)
                ImGui.Spacing();
            first = false;

            var loaded = group.Items.Count(d => dependencies.StateOf(d).State == InstallState.Loaded);
            var title = group.Kind == DependencyKind.AnyOf
                ? Loc.T($"{group.Title.En} · at least one · {loaded} loaded", $"{group.Title.De} · mindestens eins · {loaded} geladen")
                : group.Title.ToString();
            DrawGroup(dependencies, group, title);
        }

        ImGui.Spacing();
        ImGui.TextColored(Theme.Muted, Loc.T(
            "Install adds the plugin's repository to Dalamud if it's missing and installs from there.",
            "Installieren trägt das Repository des Plugins in Dalamud ein, falls es fehlt, und installiert von dort."));
    }

    private static void DrawGroup(Dependencies dependencies, DependencyGroup group, string title)
    {
        Widgets.SectionHeader(title);

        foreach (var dependency in group.Items)
        {
            var (state, version) = dependencies.StateOf(dependency);
            dependencies.Messages.TryGetValue(dependency.InternalName, out var message);

            Widgets.Card($"dep{dependency.InternalName}", innerWidth =>
            {
                var origin = ImGui.GetCursorScreenPos();
                var right = origin.X + innerWidth;

                var (icon, color) = (group.Kind, state) switch
                {
                    (DependencyKind.Conflict, InstallState.Loaded) => (FontAwesomeIcon.ExclamationTriangle, Theme.Warning),
                    (DependencyKind.Conflict, _) => (FontAwesomeIcon.Check, Theme.Success),
                    (_, InstallState.Loaded) => (FontAwesomeIcon.Check, Theme.Success),
                    (_, InstallState.Installed) => (FontAwesomeIcon.PowerOff, Theme.Warning),
                    _ => (FontAwesomeIcon.Times, Theme.Danger),
                };

                ImGui.SetCursorScreenPos(origin + new Vector2(2, 1));
                Widgets.Icon(icon, color);

                ImGui.SetCursorScreenPos(origin + new Vector2(28, 0));
                ImGui.TextUnformatted(version is null ? dependency.DisplayName : $"{dependency.DisplayName} v{version}");
                ImGui.SameLine();
                ImGui.TextColored(color, StateText(group.Kind, state));

                ImGui.SetCursorScreenPos(origin + new Vector2(28, ImGui.GetTextLineHeight() + 4));
                ImGui.TextColored(Theme.Muted, (message.Equals(default(LocText)) ? dependency.Purpose : message).ToString());

                // Knöpfe rechtsbündig in der ersten Zeile.
                var buttons = new List<(string Label, Action Click, bool Installs)> { ("Website", () => Dalamud.Utility.Util.OpenLink(dependency.Website), false) };
                if (group.Kind != DependencyKind.Conflict && state != InstallState.Loaded && dependency.Repositories.Length > 0)
                    buttons.Insert(0, (state == InstallState.Installed ? Loc.T("Open installer", "Installer öffnen") : Loc.T("Install", "Installieren"),
                        () => dependencies.Install(dependency), state != InstallState.Installed));

                var x = right;
                foreach (var (label, _, _) in buttons)
                    x -= ImGui.CalcTextSize(label).X + ImGui.GetStyle().FramePadding.X * 2 + 6;

                ImGui.SetCursorScreenPos(new Vector2(x, origin.Y - 3));
                foreach (var (label, click, installs) in buttons)
                {
                    var busy = installs && dependencies.IsInstalling(dependency);
                    ImGui.BeginDisabled(busy);
                    if (ImGui.Button($"{label}##{dependency.InternalName}"))
                        click();
                    ImGui.EndDisabled();
                    ImGui.SameLine(0, 6);
                }

                ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + ImGui.GetTextLineHeight() * 2 + 6));
                ImGui.Dummy(new Vector2(innerWidth, 0));
            });

            ImGui.Spacing();
        }
    }

    private static string StateText(DependencyKind kind, InstallState state) => (kind, state) switch
    {
        (DependencyKind.Conflict, InstallState.Loaded) => Loc.T("loaded", "geladen"),
        (DependencyKind.Conflict, _) => Loc.T("not loaded", "nicht geladen"),
        (_, InstallState.Loaded) => Loc.T("loaded", "geladen"),
        (_, InstallState.Installed) => Loc.T("installed, turned off", "installiert, ausgeschaltet"),
        _ => Loc.T("missing", "fehlt"),
    };
}
