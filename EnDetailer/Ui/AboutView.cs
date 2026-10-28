using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace EnDetailer.Ui;

/// <param name="Input">Befehl samt Argumenten, wie er getippt wird.</param>
public sealed record AboutCommand(string Input, string Effect);

public sealed record AboutLink(string Label, FontAwesomeIcon Icon, string Url);

/// <param name="Copyright">Ganze Zeile, etwa mit Plugin-Name, Jahr und Urheber.</param>
public sealed record AboutInfo(
    string Name,
    string Version,
    string Description,
    IReadOnlyList<AboutCommand> Commands,
    string License,
    string Copyright,
    IReadOnlyList<AboutLink> Links);

/// <summary>Inhalt des About-Tabs: Name und Beschreibung, Befehle, Lizenz mit Link-Knöpfen.</summary>
public static class AboutView
{
    public static void Draw(AboutInfo info)
    {
        Widgets.SectionHeader("About");
        Widgets.Card("about", width =>
        {
            ImGui.TextColored(Theme.AccentLight, info.Name);
            ImGui.SameLine();
            ImGui.TextColored(Theme.Muted, info.Version);

            ImGui.PushTextWrapPos(ImGui.GetCursorScreenPos().X + width);
            ImGui.TextUnformatted(info.Description);
            ImGui.PopTextWrapPos();
        });

        ImGui.Dummy(new Vector2(0, 4));
        Widgets.SectionHeader("Commands");
        Widgets.Card("commands", _ =>
        {
            var column = info.Commands.Count == 0 ? 0f : info.Commands.Max(c => ImGui.CalcTextSize(c.Input).X) + 20;
            foreach (var command in info.Commands)
                CommandRow(command, column);
        });

        ImGui.Dummy(new Vector2(0, 4));
        Widgets.SectionHeader("License");
        Widgets.Card("license", _ =>
        {
            ImGui.TextColored(Theme.Muted, info.Copyright);
            ImGui.TextColored(Theme.Muted, $"Licensed under GNU {info.License}, without any warranty.");

            if (info.Links.Count == 0)
                return;

            ImGui.Spacing();
            for (var i = 0; i < info.Links.Count; i++)
            {
                if (i > 0)
                    ImGui.SameLine();

                if (LinkButton(i, info.Links[i]))
                    Dalamud.Utility.Util.OpenLink(info.Links[i].Url);
            }
        });
    }

    private static void CommandRow(AboutCommand command, float column)
    {
        var start = ImGui.GetCursorScreenPos();
        ImGui.TextColored(Theme.Text, command.Input);
        var next = ImGui.GetCursorScreenPos();
        ImGui.SetCursorScreenPos(new Vector2(start.X + column, start.Y));
        ImGui.TextColored(Theme.Muted, command.Effect);
        ImGui.SetCursorScreenPos(next);
    }

    /// <summary>Umrandeter Knopf mit Icon und Beschriftung; der Tooltip zeigt die Adresse.</summary>
    private static bool LinkButton(int index, AboutLink link)
    {
        var draw = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var iconText = link.Icon.ToIconString();

        ImGui.PushFont(UiBuilder.IconFont);
        var iconSize = ImGui.CalcTextSize(iconText);
        ImGui.PopFont();

        var textSize = ImGui.CalcTextSize(link.Label);
        var size = new Vector2(iconSize.X + textSize.X + 30, ImGui.GetFrameHeight());

        var clicked = ImGui.InvisibleButton($"##link{index}", size);
        var hovered = ImGui.IsItemHovered();
        if (hovered)
            ImGui.SetTooltip(link.Url);

        if (hovered)
            draw.AddRectFilled(pos, pos + size, Theme.U32(Theme.Hover), Theme.Rounding);
        draw.AddRect(pos, pos + size, Theme.U32(hovered ? Theme.AccentLight : Theme.Border), Theme.Rounding, ImDrawFlags.None, 1f);

        ImGui.PushFont(UiBuilder.IconFont);
        draw.AddText(new Vector2(pos.X + 12, pos.Y + (size.Y - iconSize.Y) / 2), Theme.U32(Theme.AccentLight), iconText);
        ImGui.PopFont();
        draw.AddText(new Vector2(pos.X + 18 + iconSize.X, pos.Y + (size.Y - textSize.Y) / 2), Theme.U32(hovered ? Theme.Text : Theme.Muted), link.Label);

        return clicked;
    }
}
