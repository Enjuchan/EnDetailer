using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using EnDetailer.Core;
using EnDetailer.Ui;

namespace EnDetailer;

/// <summary>
/// Die Bedienelemente, die das Einstellungsfenster und das Willkommensfenster teilen, damit beide dieselben Felder
/// schreiben und sofort speichern: Anzeigemodus, wann das Meter erscheint, Position sperren.
/// </summary>
public static class SettingsControls
{
    private static readonly DpsMode[] DpsModes = [DpsMode.Rolling, DpsMode.Encounter];
    private static readonly ShowMode[] ShowModes = [ShowMode.Always, ShowMode.InCombat, ShowMode.InDuty];

    public static void DisplayModeRow(Configuration config, float width)
    {
        var mode = config.Mode;
        if (SegmentedRow(Loc.T("Display mode", "Anzeigemodus"), "##mode", DpsModes, ModeLabel, ref mode, width, 180,
                Loc.T("Now follows your current damage over the reaction time.\nFight averages over the whole encounter.\nAlso in the meter's right-click menu.",
                      "Jetzt folgt deinem aktuellen Schaden über die Reaktionszeit.\nKampf mittelt über den ganzen Kampf.\nAuch im Rechtsklick-Menü des Meters.")))
        {
            config.Mode = mode;
            config.Save();
        }
    }

    public static void ShowModeRow(Configuration config, float width)
    {
        var show = config.ShowMode;
        if (SegmentedRow(Loc.T("Show meter", "Meter zeigen"), "##show", ShowModes, ShowLabel, ref show, width, 300))
        {
            config.ShowMode = show;
            config.Save();
        }
    }

    public static void LockRow(Configuration config, float width)
    {
        var locked = config.Locked;
        if (Widgets.ToggleRow(Loc.T("Lock window", "Fenster sperren"), ref locked, width,
                Loc.T("Locked: fixed in place and click-through.\n" +
                      "In combat you hit the boss instead of the window.\n" +
                      "Unlock again to sort or move it.\n" +
                      "Also toggles with /endetailer lock.",
                      "Gesperrt: fest und klickdurchlässig.\n" +
                      "Im Kampf triffst du den Boss statt des Fensters.\n" +
                      "Entsperre es wieder, um zu sortieren oder zu verschieben.\n" +
                      "Schaltet auch mit /endetailer lock um.")))
        {
            config.Locked = locked;
            config.Save();
        }
    }

    public static float ControlWidth(float rowWidth, float wanted) => Math.Min(wanted * ImGuiHelpers.GlobalScale, rowWidth * 0.6f);

    /// <summary>Beschriftung links, Umschaltleiste rechtsbuendig. Bildschirmkoordinaten, weil SameLine in Tabellenspalten spaltenbezogen ist.</summary>
    public static bool SegmentedRow<T>(string label, string id, T[] values, Func<T, string> text, ref T selected, float width, float wanted, string? tooltip = null)
        where T : struct, Enum
    {
        var start = ImGui.GetCursorScreenPos();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(label);
        var hovered = ImGui.IsItemHovered();

        // Längere deutsche Texte bekommen so viel Platz, wie jedes Feld braucht.
        var widest = 0f;
        foreach (var value in values)
            widest = Math.Max(widest, ImGui.CalcTextSize(text(value)).X);

        var control = Math.Min(Math.Max(ControlWidth(width, wanted), (widest + 16) * values.Length), width * 0.8f);
        ImGui.SetCursorScreenPos(new Vector2(start.X + width - control, start.Y));
        var changed = Widgets.Segmented(id, values, text, ref selected, control);
        hovered |= ImGui.IsItemHovered();

        if (tooltip is not null && hovered)
            ImGui.SetTooltip(tooltip);

        return changed;
    }

    private static string ModeLabel(DpsMode mode) => mode == DpsMode.Rolling ? Loc.T("Now", "Jetzt") : Loc.T("Fight", "Kampf");

    private static string ShowLabel(ShowMode mode) => mode switch
    {
        ShowMode.Always => Loc.T("Always", "Immer"),
        ShowMode.InCombat => Loc.T("In combat", "Im Kampf"),
        _ => Loc.T("In duty", "In Instanz")
    };
}
