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

            var title = group.Kind == DependencyKind.AnyOf
                ? $"{group.Title} · at least one · {group.Items.Count(d => dependencies.StateOf(d).State == InstallState.Loaded)} loaded"
                : group.Title;
            DrawGroup(dependencies, group, title);
        }

        ImGui.Spacing();
        ImGui.TextColored(Theme.Muted, "Install adds the plugin's repository to Dalamud if it's missing and installs from there.");
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
                ImGui.TextColored(Theme.Muted, message ?? dependency.Purpose);

                // Knöpfe rechtsbündig in der ersten Zeile.
                var buttons = new List<(string Label, Action Click)> { ("Website", () => Dalamud.Utility.Util.OpenLink(dependency.Website)) };
                if (group.Kind != DependencyKind.Conflict && state != InstallState.Loaded && dependency.Repositories.Length > 0)
                    buttons.Insert(0, (state == InstallState.Installed ? "Open installer" : "Install", () => dependencies.Install(dependency)));

                var x = right;
                foreach (var (label, _) in buttons)
                    x -= ImGui.CalcTextSize(label).X + ImGui.GetStyle().FramePadding.X * 2 + 6;

                ImGui.SetCursorScreenPos(new Vector2(x, origin.Y - 3));
                foreach (var (label, click) in buttons)
                {
                    var busy = label == "Install" && dependencies.IsInstalling(dependency);
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
        (DependencyKind.Conflict, InstallState.Loaded) => "loaded",
        (DependencyKind.Conflict, _) => "not loaded",
        (_, InstallState.Loaded) => "loaded",
        (_, InstallState.Installed) => "installed, turned off",
        _ => "missing",
    };
}
