using System;
using System.Numerics;
using System.Reflection;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Plugin;
using EnDetailer.Core;
using EnDetailer.Ui;

namespace EnDetailer;

public sealed class ConfigWindow(
    Configuration config,
    EncounterTracker encounters,
    IDalamudPluginInterface pluginInterface,
    Func<bool> iinactConnected)
{
    private enum IinactInstall
    {
        NotInstalled,
        Disabled,
        Loaded
    }

    private static readonly (FontAwesomeIcon Icon, string Label)[] Tabs =
    [
        (FontAwesomeIcon.Calculator, "Meter"),
        (FontAwesomeIcon.Eye, "Display"),
        (FontAwesomeIcon.InfoCircle, "About")
    ];

    private static readonly DpsMode[] DpsModes = [DpsMode.Rolling, DpsMode.Encounter];
    private static readonly ShowMode[] ShowModes = [ShowMode.Always, ShowMode.InCombat, ShowMode.InDuty];
    private static readonly TextStyle[] TextStyles = [TextStyle.Shadow, TextStyle.Outline, TextStyle.Plain];
    private static readonly BarExtent[] BarExtents = [BarExtent.NameOnly, BarExtent.ThroughTotal, BarExtent.FullRow];

    private static readonly string Version =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? string.Empty;

    private const string IinactName = "IINACT";
    private const long InstallCheckMs = 1000;

    public bool Visible;

    /// <summary>Vorschau mit erfundener Gruppe. Nur zur Laufzeit, wird nicht gespeichert.</summary>
    public bool Preview;

    private int tab;
    private IinactInstall iinactInstall;
    private long nextInstallCheck;

    public void Draw()
    {
        if (!this.Visible)
            return;

        // Bei kleinerem Fenster scrollt der Inhalt, statt Spalten zu quetschen.
        ImGui.SetNextWindowSize(new Vector2(660, 640) * ImGuiHelpers.GlobalScale, ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSizeConstraints(new Vector2(520, 360) * ImGuiHelpers.GlobalScale, new Vector2(float.MaxValue, float.MaxValue));

        using var theme = Theme.Push();
        var open = ImGui.Begin("EnDetailer Settings", ref this.Visible);
        if (open)
        {
            Decor.Background();
            DrawHeader();
            ImGui.Dummy(new Vector2(0, 2));
            Widgets.PillTabs("##tabs", Tabs, ref this.tab);
            ImGui.Dummy(new Vector2(0, 4));

            switch (this.tab)
            {
                case 0:
                    DrawMeterTab();
                    break;
                case 1:
                    DrawDisplayTab();
                    break;
                default:
                    DrawAboutTab();
                    break;
            }
        }

        ImGui.End();
    }

    private void DrawHeader()
    {
        var height = ImGui.GetFrameHeight();
        Decor.HeaderBand(height);

        var start = ImGui.GetCursorScreenPos();
        var right = start.X + ImGui.GetContentRegionAvail().X;

        ImGui.AlignTextToFramePadding();
        Widgets.Icon(FontAwesomeIcon.Heartbeat, Theme.AccentLight);
        ImGui.SameLine();
        ImGui.TextColored(Theme.AccentLight, "EnDetailer");
        ImGui.SameLine();
        ImGui.TextColored(Theme.Muted, Version);

        var pillHeight = ImGui.GetTextLineHeight() + 6;
        var y = start.Y + (height - pillHeight) / 2;
        var gap = ImGui.GetStyle().ItemSpacing.X;

        var iinactWidth = Widgets.PillWidth(IinactName, true);
        var total = iinactWidth;
        if (this.Preview)
            total += gap + Widgets.PillWidth("Preview", false);

        var x = right - total;
        ImGui.SetCursorScreenPos(new Vector2(x, y));
        Widgets.StatusPill(IinactName, iinactConnected() ? Theme.Success : Theme.Warning);

        if (this.Preview)
        {
            ImGui.SetCursorScreenPos(new Vector2(x + iinactWidth + gap, y));
            Widgets.Pill("Preview", Theme.Accent, Theme.AccentLight);
        }

        ImGui.SetCursorScreenPos(new Vector2(start.X, start.Y + height + 10));
        ImGui.Dummy(Vector2.Zero);
    }

    private void DrawMeterTab()
    {
        Widgets.SectionHeader("Calculation");
        Widgets.Card("calculation", width =>
        {
            var mode = config.Mode;
            if (SegmentedRow("Display mode", "##mode", DpsModes, ModeLabel, ref mode, width, 180,
                    "Now follows your current damage over the reaction time.\nFight averages over the whole encounter.\nAlso in the meter's right-click menu."))
            {
                config.Mode = mode;
                config.Save();
            }

            var reaction = config.ReactionSeconds;
            if (SliderIntRow("Reaction time", ref reaction, ReactionTime.MinSeconds, ReactionTime.MaxSeconds, "%d s", width,
                    "How quickly the current DPS follows your damage.\n" +
                    "Short values react faster to bursts.\n" +
                    "Long values show calmer numbers.\n" +
                    "The default is 10 s."))
            {
                config.ReactionSeconds = reaction;
                config.Save();
            }
        });

        ImGui.Dummy(new Vector2(0, 4));
        Widgets.SectionHeader("Fight",
            "In IINACT, turn OFF 'End encounter automatically after leaving combat'.\n" +
            "EnDetailer decides that on its own.");
        Widgets.Card("fight", width =>
        {
            var grace = config.GraceSeconds;
            if (SliderIntRow("Grace period", ref grace, 5, 60, "%d s", width,
                    "How long combat must stay quiet before the encounter ends.\n" +
                    "Cutscenes do not count - they keep the fight alive.\n" +
                    "Raise this if a fight still gets cut short."))
            {
                config.GraceSeconds = grace;
                encounters.GracePeriod = TimeSpan.FromSeconds(grace);
                config.Save();
            }

            var zone = config.ResetOnZoneChange;
            if (Widgets.ToggleRow("Reset on zone change", ref zone, width))
            {
                config.ResetOnZoneChange = zone;
                encounters.ResetOnZoneChange = zone;
                config.Save();
            }

            var endIinact = config.EndIinactEncounter;
            if (Widgets.ToggleRow("End IINACT encounter", ref endIinact, width,
                    "Ends IINACTs encounter once EnDetailer considers the fight over.\n" +
                    "Keeps both in sync and cuts logs cleanly for FFLogs.\n" +
                    "This prints a local echo line 'end' to your chat."))
            {
                config.EndIinactEncounter = endIinact;
                config.Save();
            }
        });
    }

    private void DrawDisplayTab()
    {
        Widgets.SectionHeader("Visibility");
        Widgets.Card("visibility", width =>
        {
            var show = config.ShowMode;
            if (SegmentedRow("Show meter", "##show", ShowModes, ShowLabel, ref show, width, 300))
            {
                config.ShowMode = show;
                config.Save();
            }

            if (config.ShowMode == ShowMode.InCombat)
            {
                var after = config.ShowAfterCombatSeconds;
                if (SliderIntRow("Keep after combat", ref after, 0, 120, "%d s", width))
                {
                    config.ShowAfterCombatSeconds = after;
                    config.Save();
                }
            }

            var hideCutscenes = config.HideInCutscenes;
            if (Widgets.ToggleRow("Hide in cutscenes", ref hideCutscenes, width))
            {
                config.HideInCutscenes = hideCutscenes;
                config.Save();
            }

            var locked = config.Locked;
            if (Widgets.ToggleRow("Lock window", ref locked, width,
                    "Locked: fixed in place and click-through.\n" +
                    "In combat you hit the boss instead of the window.\n" +
                    "Unlock again to sort or move it.\n" +
                    "Also toggles with /endetailer lock."))
            {
                config.Locked = locked;
                config.Save();
            }
        });

        ImGui.Dummy(new Vector2(0, 4));
        Widgets.SectionHeader("Look");
        Widgets.Card("look", width =>
        {
            if (!ImGui.BeginTable("##lookTable", 2, ImGuiTableFlags.SizingStretchSame))
                return;

            ImGui.TableNextColumn();
            var alpha = config.BackgroundAlpha;
            if (SliderFloatRow("Background opacity", ref alpha, 0f, 1f, "%.2f", CellWidth()))
            {
                config.BackgroundAlpha = alpha;
                config.Save();
            }

            ImGui.TableNextColumn();
            var barAlpha = (int)config.BarAlpha;
            if (SliderIntRow("Bar opacity", ref barAlpha, 40, 255, "%d", CellWidth()))
            {
                config.BarAlpha = (byte)barAlpha;
                config.Save();
            }

            ImGui.TableNextColumn();
            var fontScale = config.FontScale;
            if (SliderFloatRow("Font size", ref fontScale, 0.7f, 2.0f, "%.2f", CellWidth(), "Bar height and job icons scale along."))
            {
                config.FontScale = fontScale;
                config.Save();
            }

            ImGui.TableNextColumn();
            var style = config.TextStyle;
            if (SegmentedRow("Text style", "##style", TextStyles, StyleLabel, ref style, CellWidth(), 170))
            {
                config.TextStyle = style;
                config.Save();
            }

            ImGui.TableNextColumn();
            var extent = config.BarExtent;
            if (SegmentedRow("Bar length", "##extent", BarExtents, ExtentLabel, ref extent, CellWidth(), 170,
                    "Name: bar behind the name only.\nTotal: through the Total column.\nRow: across the whole row."))
            {
                config.BarExtent = extent;
                config.Save();
            }

            ImGui.TableNextColumn();
            var icons = config.ShowJobIcons;
            if (Widgets.ToggleRow("Job icons", ref icons, CellWidth()))
            {
                config.ShowJobIcons = icons;
                config.Save();
            }

            ImGui.TableNextColumn();
            var highlightSelf = config.HighlightSelf;
            if (Widgets.ToggleRow("Highlight my row", ref highlightSelf, CellWidth()))
            {
                config.HighlightSelf = highlightSelf;
                config.Save();
            }

            ImGui.TableNextColumn();
            var stripes = config.RowStripes;
            if (Widgets.ToggleRow("Row stripes", ref stripes, CellWidth()))
            {
                config.RowStripes = stripes;
                config.Save();
            }

            ImGui.TableNextColumn();
            var graph = config.ShowGraph;
            if (Widgets.ToggleRow("Fight graph", ref graph, CellWidth(),
                    "A narrow graph of the group's current rate under the header.\nBursts show up as peaks."))
            {
                config.ShowGraph = graph;
                config.Save();
            }

            ImGui.EndTable();
        });

        ImGui.Dummy(new Vector2(0, 4));
        Widgets.SectionHeader("Tools");
        Widgets.Card("tools", width =>
        {
            Widgets.ToggleRow("Preview with a made-up party", ref this.Preview, width,
                "Fills the meter with eight made-up players, including burst windows.\n" +
                "IINACT data is ignored while it runs. Also toggles with /endetailer preview.");

            var diagnostics = config.ShowDiagnostics;
            if (Widgets.ToggleRow("Diagnostics in footer", ref diagnostics, width))
            {
                config.ShowDiagnostics = diagnostics;
                config.Save();
            }
        });
    }

    private void DrawAboutTab()
    {
        Widgets.SectionHeader("Dependency");
        Widgets.Card("dependency", width =>
        {
            var state = CurrentInstall();
            var connected = iinactConnected();

            var (icon, color, status) = state switch
            {
                IinactInstall.NotInstalled => (FontAwesomeIcon.TimesCircle, Theme.Danger, "not installed"),
                IinactInstall.Disabled => (FontAwesomeIcon.PowerOff, Theme.Warning, "disabled"),
                _ => connected
                    ? (FontAwesomeIcon.CheckCircle, Theme.Success, "connected")
                    : (FontAwesomeIcon.ExclamationTriangle, Theme.Warning, "waiting")
            };

            var start = ImGui.GetCursorScreenPos();
            ImGui.SetCursorScreenPos(start + new Vector2(0, ImGui.GetFrameHeight() / 2 - ImGui.GetTextLineHeight() / 2));
            Widgets.Icon(icon, color);

            ImGui.SetCursorScreenPos(new Vector2(start.X + width - ImGui.CalcTextSize(status).X, start.Y));
            ImGui.TextColored(color, status);

            ImGui.SetCursorScreenPos(new Vector2(start.X + 30, start.Y));
            ImGui.TextUnformatted(IinactName);
            ImGui.SetCursorScreenPos(new Vector2(start.X + 30, ImGui.GetCursorScreenPos().Y));
            ImGui.TextColored(Theme.Muted, "Supplies the combat data.");
        });

        ImGui.Dummy(new Vector2(0, 4));
        Widgets.SectionHeader("Commands");
        Widgets.Card("commands", _ =>
        {
            var column = ImGui.CalcTextSize("/endetailer preview").X + 20;
            CommandRow("/endetailer", "Show or hide the meter.", column);
            CommandRow("/endetailer config", "Open these settings.", column);
            CommandRow("/endetailer lock", "Lock or unlock the meter window.", column);
            CommandRow("/endetailer preview", "Start or stop the made-up party.", column);
        });

        ImGui.Dummy(new Vector2(0, 4));
        Widgets.SectionHeader("License");

        // Die AGPL sieht fuer interaktive Programme einen sichtbaren Hinweis auf
        // Urheberschaft, fehlende Gewaehrleistung und Lizenz vor.
        ImGui.TextDisabled("EnDetailer - Copyright (c) 2026 Enjuchan");
        ImGui.TextDisabled("Licensed under GNU AGPL-3.0, without any warranty.");
        ImGui.TextDisabled("github.com/Enjuchan/EnDetailer");
    }

    private static void CommandRow(string command, string description, float column)
    {
        var start = ImGui.GetCursorScreenPos();
        ImGui.TextColored(Theme.AccentLight, command);
        var next = ImGui.GetCursorScreenPos();
        ImGui.SetCursorScreenPos(new Vector2(start.X + column, start.Y));
        ImGui.TextColored(Theme.Muted, description);
        ImGui.SetCursorScreenPos(next);
    }

    /// <summary>
    /// Fragt die Plugin-Liste hoechstens einmal je Sekunde ab, nur solange der About-Tab offen ist.
    /// </summary>
    private IinactInstall CurrentInstall()
    {
        var now = Environment.TickCount64;
        if (now < this.nextInstallCheck)
            return this.iinactInstall;

        this.nextInstallCheck = now + InstallCheckMs;
        var state = IinactInstall.NotInstalled;
        foreach (var plugin in pluginInterface.InstalledPlugins)
        {
            if (!string.Equals(plugin.InternalName, IinactName, StringComparison.OrdinalIgnoreCase))
                continue;

            state = plugin.IsLoaded ? IinactInstall.Loaded : IinactInstall.Disabled;
            if (plugin.IsLoaded)
                break;
        }

        this.iinactInstall = state;
        return state;
    }

    private static float CellWidth() => ImGui.GetContentRegionAvail().X;

    private static string ModeLabel(DpsMode mode) => mode == DpsMode.Rolling ? "Now" : "Fight";

    private static string ShowLabel(ShowMode mode) => mode switch
    {
        ShowMode.Always => "Always",
        ShowMode.InCombat => "In combat",
        _ => "In duty"
    };

    private static string StyleLabel(TextStyle style) => style switch
    {
        TextStyle.Shadow => "Shadow",
        TextStyle.Outline => "Outline",
        _ => "Plain"
    };

    private static string ExtentLabel(BarExtent extent) => extent switch
    {
        BarExtent.NameOnly => "Name",
        BarExtent.ThroughTotal => "Total",
        _ => "Row"
    };

    private static float ControlWidth(float rowWidth, float wanted) => Math.Min(wanted * ImGuiHelpers.GlobalScale, rowWidth * 0.6f);

    /// <summary>Beschriftung links, Umschaltleiste rechtsbuendig. Bildschirmkoordinaten, weil SameLine in Tabellenspalten spaltenbezogen ist.</summary>
    private static bool SegmentedRow<T>(string label, string id, T[] values, Func<T, string> text, ref T selected, float width, float wanted, string? tooltip = null)
        where T : struct, Enum
    {
        var start = ImGui.GetCursorScreenPos();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(label);
        var hovered = ImGui.IsItemHovered();

        var control = ControlWidth(width, wanted);
        ImGui.SetCursorScreenPos(new Vector2(start.X + width - control, start.Y));
        var changed = Widgets.Segmented(id, values, text, ref selected, control);
        hovered |= ImGui.IsItemHovered();

        if (tooltip is not null && hovered)
            ImGui.SetTooltip(tooltip);

        return changed;
    }

    private static bool SliderIntRow(string label, ref int value, int min, int max, string format, float width, string? tooltip = null)
    {
        var start = ImGui.GetCursorScreenPos();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(label);
        var hovered = ImGui.IsItemHovered();

        var control = ControlWidth(width, 220);
        ImGui.SetCursorScreenPos(new Vector2(start.X + width - control, start.Y));
        ImGui.SetNextItemWidth(control);
        var changed = ImGui.SliderInt($"##{label}", ref value, min, max, format);
        hovered |= ImGui.IsItemHovered();

        if (tooltip is not null && hovered)
            ImGui.SetTooltip(tooltip);

        return changed;
    }

    private static bool SliderFloatRow(string label, ref float value, float min, float max, string format, float width, string? tooltip = null)
    {
        var start = ImGui.GetCursorScreenPos();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(label);
        var hovered = ImGui.IsItemHovered();

        var control = ControlWidth(width, 220);
        ImGui.SetCursorScreenPos(new Vector2(start.X + width - control, start.Y));
        ImGui.SetNextItemWidth(control);
        var changed = ImGui.SliderFloat($"##{label}", ref value, min, max, format);
        hovered |= ImGui.IsItemHovered();

        if (tooltip is not null && hovered)
            ImGui.SetTooltip(tooltip);

        return changed;
    }
}
