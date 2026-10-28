using System;
using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using EnDetailer.Core;
using EnDetailer.Ui;

namespace EnDetailer;

public sealed class ConfigWindow : Window
{
    private static readonly (FontAwesomeIcon Icon, string Label)[] TabsEn =
    [
        (FontAwesomeIcon.Calculator, "Meter"),
        (FontAwesomeIcon.Eye, "Display"),
        (FontAwesomeIcon.PuzzlePiece, "Dependencies"),
        (FontAwesomeIcon.InfoCircle, "About")
    ];

    private static readonly (FontAwesomeIcon Icon, string Label)[] TabsDe =
    [
        (FontAwesomeIcon.Calculator, "Meter"),
        (FontAwesomeIcon.Eye, "Anzeige"),
        (FontAwesomeIcon.PuzzlePiece, "Abhängigkeiten"),
        (FontAwesomeIcon.InfoCircle, "Info")
    ];

    private static readonly TextStyle[] TextStyles = [TextStyle.Shadow, TextStyle.Outline, TextStyle.Plain];
    private static readonly BarExtent[] BarExtents = [BarExtent.NameOnly, BarExtent.ThroughTotal, BarExtent.FullRow];

    private static readonly string Version =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? string.Empty;

    private const string IinactName = "IINACT";

    private readonly Configuration config;
    private readonly EncounterTracker encounters;
    private readonly Dependencies dependencies;
    private readonly AboutInfo about;
    private readonly Func<bool> iinactConnected;
    private readonly Action openWelcome;
    private readonly Action openChangelog;
    private readonly FrameTiming timing;

    private Theme.Scope? themeScope;

    /// <summary>Vorschau mit erfundener Gruppe. Nur zur Laufzeit, wird nicht gespeichert.</summary>
    public bool Preview;

    private int tab;

    public ConfigWindow(
        Configuration config,
        EncounterTracker encounters,
        Dependencies dependencies,
        AboutInfo about,
        IPluginLog log,
        Func<bool> iinactConnected,
        Action openWelcome,
        Action openChangelog)
        // Der Name bleibt "EnDetailer Settings", damit gespeicherte Fensterlage und -größe erhalten bleiben.
        : base("EnDetailer Settings")
    {
        this.config = config;
        this.encounters = encounters;
        this.dependencies = dependencies;
        this.about = about;
        this.iinactConnected = iinactConnected;
        this.openWelcome = openWelcome;
        this.openChangelog = openChangelog;
        this.timing = new FrameTiming(log, "Settings draw", TimeSpan.FromSeconds(5), () => config.ShowDiagnostics);

        // Das Fenster war bisher ein einfaches ImGui-Fenster ohne Escape-Schließen, Töne und Einblenden.
        RespectCloseHotkey = false;
        DisableWindowSounds = true;
        DisableFadeInFadeOut = true;
    }

    public override void PreDraw()
    {
        this.themeScope = Theme.Push();

        // Size und SizeConstraints bleiben leer, weil Dalamud sie mit der globalen Skalierung malnehmen würde.
        // Bei kleinerem Fenster scrollt der Inhalt, statt Spalten zu quetschen.
        ImGui.SetNextWindowSize(new Vector2(660, 640) * ImGuiHelpers.GlobalScale, ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSizeConstraints(new Vector2(520, 360) * ImGuiHelpers.GlobalScale, new Vector2(float.MaxValue, float.MaxValue));
    }

    public override void PostDraw()
    {
        this.themeScope?.Dispose();
        this.themeScope = null;
    }

    public override void Draw()
    {
        var started = Stopwatch.GetTimestamp();
        DrawWindow();
        this.timing.Add(Stopwatch.GetElapsedTime(started));
    }

    private void DrawWindow()
    {
        Decor.Background();
        DrawHeader();
        ImGui.Dummy(new Vector2(0, 2));
        Widgets.PillTabs("##tabs", Loc.Current == Language.German ? TabsDe : TabsEn, ref this.tab);
        ImGui.Dummy(new Vector2(0, 4));

        switch (this.tab)
        {
            case 0:
                DrawMeterTab();
                break;
            case 1:
                DrawDisplayTab();
                break;
            case 2:
                DependenciesView.Draw(this.dependencies);
                break;
            default:
                AboutView.Draw(this.about, this.openWelcome, this.openChangelog);
                break;
        }
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

        var previewName = Loc.T("Preview", "Vorschau");
        var pillHeight = ImGui.GetTextLineHeight() + 6;
        var y = start.Y + (height - pillHeight) / 2;
        var gap = ImGui.GetStyle().ItemSpacing.X;

        var iinactWidth = Widgets.PillWidth(IinactName, true);
        var languageWidth = Widgets.LanguagePillWidth();
        var total = iinactWidth + gap + languageWidth;
        if (this.Preview)
            total += gap + Widgets.PillWidth(previewName, false);

        var x = right - total;

        if (this.config.ShowDiagnostics)
        {
            // Rechtsbuendig vor den Pills, aber nie ueber den Namen hinweg.
            var nameEnd = ImGui.GetItemRectMax().X;
            var time = this.timing.Summary();
            var timeX = x - gap * 2 - ImGui.CalcTextSize(time).X;
            if (timeX >= nameEnd + gap)
            {
                ImGui.SetCursorScreenPos(new Vector2(timeX, start.Y + (height - ImGui.GetTextLineHeight()) / 2));
                ImGui.TextColored(Theme.Muted, time);
            }
        }

        ImGui.SetCursorScreenPos(new Vector2(x, y));
        Widgets.StatusPill(IinactName, this.iinactConnected() ? Theme.Success : Theme.Warning);

        if (this.Preview)
        {
            ImGui.SetCursorScreenPos(new Vector2(x + iinactWidth + gap, y));
            Widgets.Pill(previewName, Theme.Accent, Theme.AccentLight);
        }

        ImGui.SetCursorScreenPos(new Vector2(right - languageWidth, y));
        Widgets.LanguagePill(code =>
        {
            this.config.Language = code;
            this.config.Save();
        });

        ImGui.SetCursorScreenPos(new Vector2(start.X, start.Y + height + 10));
        ImGui.Dummy(Vector2.Zero);
    }

    private void DrawMeterTab()
    {
        Widgets.SectionHeader(Loc.T("Calculation", "Berechnung"));
        Widgets.Card("calculation", width =>
        {
            SettingsControls.DisplayModeRow(this.config, width);

            var reaction = this.config.ReactionSeconds;
            if (SliderIntRow(Loc.T("Reaction time", "Reaktionszeit"), ref reaction, ReactionTime.MinSeconds, ReactionTime.MaxSeconds, "%d s", width,
                    Loc.T("How quickly the current DPS follows your damage.\n" +
                          "Short values react faster to bursts.\n" +
                          "Long values show calmer numbers.\n" +
                          "The default is 10 s.",
                          "Wie schnell das aktuelle DPS deinem Schaden folgt.\n" +
                          "Kurze Werte reagieren schneller auf Bursts.\n" +
                          "Lange Werte zeigen ruhigere Zahlen.\n" +
                          "Standard sind 10 s.")))
            {
                this.config.ReactionSeconds = reaction;
                this.config.Save();
            }
        });

        ImGui.Dummy(new Vector2(0, 4));
        Widgets.SectionHeader(Loc.T("Fight", "Kampf"),
            Loc.T("In IINACT, turn OFF 'End encounter automatically after leaving combat'.\n" +
                  "EnDetailer decides that on its own.",
                  "Schalte in IINACT die Option 'End encounter automatically after leaving combat' aus.\n" +
                  "EnDetailer entscheidet das selbst."));
        Widgets.Card("fight", width =>
        {
            var grace = this.config.GraceSeconds;
            if (SliderIntRow(Loc.T("Grace period", "Karenzzeit"), ref grace, 5, 60, "%d s", width,
                    Loc.T("How long combat must stay quiet before the encounter ends.\n" +
                          "Cutscenes do not count - they keep the fight alive.\n" +
                          "Raise this if a fight still gets cut short.",
                          "Wie lange der Kampf ruhig bleiben muss, bevor das Encounter endet.\n" +
                          "Cutscenes zählen nicht, sie halten den Kampf am Leben.\n" +
                          "Erhöhe den Wert, wenn ein Kampf trotzdem zu früh endet.")))
            {
                this.config.GraceSeconds = grace;
                this.encounters.GracePeriod = TimeSpan.FromSeconds(grace);
                this.config.Save();
            }

            var zone = this.config.ResetOnZoneChange;
            if (Widgets.ToggleRow(Loc.T("Reset on zone change", "Bei Gebietswechsel zurücksetzen"), ref zone, width))
            {
                this.config.ResetOnZoneChange = zone;
                this.encounters.ResetOnZoneChange = zone;
                this.config.Save();
            }

            var endIinact = this.config.EndIinactEncounter;
            if (Widgets.ToggleRow(Loc.T("End IINACT encounter", "IINACT-Encounter beenden"), ref endIinact, width,
                    Loc.T("Ends IINACTs encounter once EnDetailer considers the fight over.\n" +
                          "Keeps both in sync and cuts logs cleanly for FFLogs.\n" +
                          "This prints a local echo line 'end' to your chat.",
                          "Beendet das Encounter von IINACT, sobald EnDetailer den Kampf für beendet hält.\n" +
                          "Hält beide synchron und schneidet Logs sauber für FFLogs.\n" +
                          "Dabei erscheint die lokale Echo-Zeile 'end' in deinem Chat.")))
            {
                this.config.EndIinactEncounter = endIinact;
                this.config.Save();
            }
        });
    }

    private void DrawDisplayTab()
    {
        Widgets.SectionHeader(Loc.T("Visibility", "Sichtbarkeit"));
        Widgets.Card("visibility", width =>
        {
            SettingsControls.ShowModeRow(this.config, width);

            if (this.config.ShowMode == ShowMode.InCombat)
            {
                var after = this.config.ShowAfterCombatSeconds;
                if (SliderIntRow(Loc.T("Keep after combat", "Nach dem Kampf behalten"), ref after, 0, 120, "%d s", width))
                {
                    this.config.ShowAfterCombatSeconds = after;
                    this.config.Save();
                }
            }

            var hideCutscenes = this.config.HideInCutscenes;
            if (Widgets.ToggleRow(Loc.T("Hide in cutscenes", "In Cutscenes verbergen"), ref hideCutscenes, width))
            {
                this.config.HideInCutscenes = hideCutscenes;
                this.config.Save();
            }

            SettingsControls.LockRow(this.config, width);
        });

        ImGui.Dummy(new Vector2(0, 4));
        Widgets.SectionHeader(Loc.T("Look", "Aussehen"));
        Widgets.Card("look", width =>
        {
            if (!ImGui.BeginTable("##lookTable", 2, ImGuiTableFlags.SizingStretchSame))
                return;

            ImGui.TableNextColumn();
            var alpha = this.config.BackgroundAlpha;
            if (SliderFloatRow(Loc.T("Background opacity", "Hintergrund-Deckkraft"), ref alpha, 0f, 1f, "%.2f", CellWidth()))
            {
                this.config.BackgroundAlpha = alpha;
                this.config.Save();
            }

            ImGui.TableNextColumn();
            var barAlpha = (int)this.config.BarAlpha;
            if (SliderIntRow(Loc.T("Bar opacity", "Balken-Deckkraft"), ref barAlpha, 40, 255, "%d", CellWidth()))
            {
                this.config.BarAlpha = (byte)barAlpha;
                this.config.Save();
            }

            ImGui.TableNextColumn();
            var fontScale = this.config.FontScale;
            if (SliderFloatRow(Loc.T("Font size", "Schriftgröße"), ref fontScale, 0.7f, 2.0f, "%.2f", CellWidth(),
                    Loc.T("Bar height and job icons scale along.", "Balkenhöhe und Job-Icons skalieren mit.")))
            {
                this.config.FontScale = fontScale;
                this.config.Save();
            }

            ImGui.TableNextColumn();
            var style = this.config.TextStyle;
            if (SettingsControls.SegmentedRow(Loc.T("Text style", "Textstil"), "##style", TextStyles, StyleLabel, ref style, CellWidth(), 170))
            {
                this.config.TextStyle = style;
                this.config.Save();
            }

            ImGui.TableNextColumn();
            var extent = this.config.BarExtent;
            if (SettingsControls.SegmentedRow(Loc.T("Bar length", "Balkenlänge"), "##extent", BarExtents, ExtentLabel, ref extent, CellWidth(), 170,
                    Loc.T("Name: bar behind the name only.\nTotal: through the Total column.\nRow: across the whole row.",
                          "Name: Balken nur hinter dem Namen.\nSumme: bis zur Spalte Summe.\nZeile: über die ganze Zeile.")))
            {
                this.config.BarExtent = extent;
                this.config.Save();
            }

            ImGui.TableNextColumn();
            var icons = this.config.ShowJobIcons;
            if (Widgets.ToggleRow(Loc.T("Job icons", "Job-Icons"), ref icons, CellWidth()))
            {
                this.config.ShowJobIcons = icons;
                this.config.Save();
            }

            ImGui.TableNextColumn();
            var highlightSelf = this.config.HighlightSelf;
            if (Widgets.ToggleRow(Loc.T("Highlight my row", "Meine Zeile hervorheben"), ref highlightSelf, CellWidth()))
            {
                this.config.HighlightSelf = highlightSelf;
                this.config.Save();
            }

            ImGui.TableNextColumn();
            var stripes = this.config.RowStripes;
            if (Widgets.ToggleRow(Loc.T("Row stripes", "Zeilenstreifen"), ref stripes, CellWidth()))
            {
                this.config.RowStripes = stripes;
                this.config.Save();
            }

            ImGui.TableNextColumn();
            var graph = this.config.ShowGraph;
            if (Widgets.ToggleRow(Loc.T("Fight graph", "Kampfgraph"), ref graph, CellWidth(),
                    Loc.T("A narrow graph of the group's current rate under the header.\nBursts show up as peaks.",
                          "Ein schmaler Graph der aktuellen Gruppenrate unter dem Kopf.\nBursts erscheinen als Spitzen.")))
            {
                this.config.ShowGraph = graph;
                this.config.Save();
            }

            ImGui.EndTable();
        });

        ImGui.Dummy(new Vector2(0, 4));
        Widgets.SectionHeader(Loc.T("Tools", "Werkzeuge"));
        Widgets.Card("tools", width =>
        {
            Widgets.ToggleRow(Loc.T("Preview with a made-up party", "Vorschau mit erfundener Gruppe"), ref this.Preview, width,
                Loc.T("Fills the meter with eight made-up players, including burst windows.\n" +
                      "IINACT data is ignored while it runs. Also toggles with /endetailer preview.",
                      "Füllt das Meter mit acht erfundenen Spielern samt Burst-Phasen.\n" +
                      "IINACT-Daten werden währenddessen ignoriert. Schaltet auch mit /endetailer preview um."));

            var diagnostics = this.config.ShowDiagnostics;
            if (Widgets.ToggleRow(Loc.T("Diagnostics", "Diagnose"), ref diagnostics, width,
                    Loc.T("Shows draw and update times in the meter footer and the draw time of this window.\nAlso logs them every 10 seconds.",
                          "Zeigt Zeichen- und Updatezeiten in der Fußzeile des Meters und die Zeichenzeit dieses Fensters.\nSchreibt sie auch alle 10 Sekunden ins Log.")))
            {
                this.config.ShowDiagnostics = diagnostics;
                this.config.Save();
            }
        });
    }

    private static float CellWidth() => ImGui.GetContentRegionAvail().X;

    private static string StyleLabel(TextStyle style) => style switch
    {
        TextStyle.Shadow => Loc.T("Shadow", "Schatten"),
        TextStyle.Outline => Loc.T("Outline", "Kontur"),
        _ => Loc.T("Plain", "Ohne")
    };

    private static string ExtentLabel(BarExtent extent) => extent switch
    {
        BarExtent.NameOnly => "Name",
        BarExtent.ThroughTotal => Loc.T("Total", "Summe"),
        _ => Loc.T("Row", "Zeile")
    };

    /// <summary>Breite des Reglers: wie gewünscht, aber nie über die Beschriftung links hinweg.</summary>
    private static float SliderWidth(float rowWidth, float labelWidth) =>
        Math.Max(80 * ImGuiHelpers.GlobalScale, Math.Min(SettingsControls.ControlWidth(rowWidth, 220), rowWidth - labelWidth - 12));

    private static bool SliderIntRow(string label, ref int value, int min, int max, string format, float width, string? tooltip = null)
    {
        var start = ImGui.GetCursorScreenPos();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(label);
        var hovered = ImGui.IsItemHovered();

        var control = SliderWidth(width, ImGui.GetItemRectSize().X);
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

        var control = SliderWidth(width, ImGui.GetItemRectSize().X);
        ImGui.SetCursorScreenPos(new Vector2(start.X + width - control, start.Y));
        ImGui.SetNextItemWidth(control);
        var changed = ImGui.SliderFloat($"##{label}", ref value, min, max, format);
        hovered |= ImGui.IsItemHovered();

        if (tooltip is not null && hovered)
            ImGui.SetTooltip(tooltip);

        return changed;
    }
}
