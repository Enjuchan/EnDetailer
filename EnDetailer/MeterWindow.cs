using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using Dalamud.Plugin.Services;
using EnDetailer.Core;
using EnDetailer.Ui;

namespace EnDetailer;

public enum DpsMode
{
    Rolling,
    Encounter
}

/// <summary>
/// Das Meter-Fenster: ein Overlay im Look der En-Plugins. Es steht dauerhaft und auch im
/// Kampf ueber dem Spiel, deshalb wenig Flaeche und wenig Arbeit pro Frame: Zeilen liegen in
/// wiederverwendeten Arrays, Zahlentexte samt Breite werden nur bei geaendertem Anzeigewert
/// neu gebaut, und gezeichnet wird ueber die Draw-List statt ueber einzelne Elemente.
/// </summary>
public sealed class MeterWindow(Configuration config, ITextureProvider textures)
{
    private const uint ColumnName = 0;
    private const uint ColumnTotal = 1;
    private const uint ColumnCrit = 2;
    private const uint ColumnDirectHit = 3;
    private const uint ColumnDps = 4;
    private const int MaxColumns = 5;

    // Die Werte, die dieses Fenster ueber dem Theme setzt. Als Konstanten, damit Breiten
    // berechnet werden koennen, ohne den Stil erst abzufragen.
    private const float WindowRounding = 10f;
    private const float ItemSpacingX = 6f;
    private const float ItemSpacingY = 3f;
    private const float CellPadX = 4f;
    private const float CellPadY = 1f;
    private const float DotRadius = 3.5f;

    private static readonly (FontAwesomeIcon Icon, string Tooltip)[] MetricTabs =
    [
        (FontAwesomeIcon.FistRaised, "Damage"),
        (FontAwesomeIcon.Heart, "Healing"),
        (FontAwesomeIcon.ShieldAlt, "Damage taken"),
    ];

    private static readonly string HeartIcon = FontAwesomeIcon.Heartbeat.ToIconString();

    private static readonly string[] MetricIcons =
        [.. Array.ConvertAll(MetricTabs, t => t.Icon.ToIconString())];
    private static readonly string[] HeaderIds = ["##h0", "##h1", "##h2", "##h3", "##h4"];
    private const string Ellipsis = "…";

    /// <summary>Farben als fertige ABGR-Werte, damit nichts davon pro Aufruf umgerechnet wird.</summary>
    private static class Palette
    {
        public static readonly uint Text = Theme.U32(Theme.Text);
        public static readonly uint Muted = Theme.U32(Theme.Muted);
        public static readonly uint Accent = Theme.U32(Theme.Accent);
        public static readonly uint AccentLight = Theme.U32(Theme.AccentLight);
        public static readonly uint Success = Theme.U32(Theme.Success);
        public static readonly uint Warning = Theme.U32(Theme.Warning);
        public static readonly uint Shadow = 0xD0000000;
        public static readonly uint SelfFill = Theme.U32(Theme.Accent with { W = 0.14f });
        public static readonly uint Zebra = Theme.U32(Theme.Header with { W = 0.6f });
    }

    /// <summary>Angezeigte Zahl samt gemessener Breite; Schluessel ist der gerundete Anzeigewert.</summary>
    private struct NumCell
    {
        public long Key;
        public string? Text;
        public float Width;

        public void Reset() => this.Text = null;
    }

    private struct RowCells
    {
        public NumCell Total;
        public NumCell Crit;
        public NumCell DirectHit;
        public NumCell Rate;

        public void Reset()
        {
            this.Total.Reset();
            this.Crit.Reset();
            this.DirectHit.Reset();
            this.Rate.Reset();
        }
    }

    private uint sortColumn = ColumnDps;
    private bool sortAscending;

    // Zeilen von aussen. Die Daten kommen vom IPC- und vom Framework-Thread, gezeichnet wird
    // im Draw-Thread; das Sperrobjekt schuetzt nur das Umkopieren weniger Verweise.
    private readonly object rowsGate = new();
    private MeterRow[] inbox = new MeterRow[16];
    private int inboxCount;

    // Nur im Draw-Thread: die sortierte Arbeitskopie samt Sortierwerten und Zahlentexten.
    private MeterRow[] ordered = new MeterRow[16];
    private double[] barValues = new double[16];
    private RowCells[] rowCells = new RowCells[16];
    private int orderedCount;

    // Spalten dieses Frames, einmal aufgebaut.
    private readonly uint[] columnIds = new uint[MaxColumns];
    private readonly string[] columnLabels = new string[MaxColumns];
    private int columnCount;

    // Spaltengeometrie aus der Kopfzeile desselben Frames. ImGui.GetColumnWidth taugt in
    // Tabellen nicht, deshalb wird sie hier selbst mitgeschrieben.
    private readonly float[] columnStartX = new float[MaxColumns];
    private readonly float[] columnAvail = new float[MaxColumns];
    private float rowRightEdge;
    private float barSpan;

    // Zwischenspeicher, die von der Schriftgroesse abhaengen.
    private float metricsFontSize = -1f;
    private float lineHeight;
    private float frameHeight;
    private float heartWidth;
    private float heartHeight;
    private float tabsWidth;
    private float cogWidth;
    private float durationWidth;
    private float endedWidth;
    private float ellipsisWidth;

    private readonly string?[] headerLabel = new string?[MaxColumns];
    private readonly int[] headerArrow = new int[MaxColumns];
    private readonly string[] headerText = new string[MaxColumns];
    private readonly float[] headerWidth = new float[MaxColumns];
    private readonly string?[] setupLabel = new string?[MaxColumns];
    private readonly float[] setupWidth = new float[MaxColumns];

    private string? titleSource;
    private float titleAvail = -1f;
    private string titleShown = string.Empty;
    private float titleShownWidth;

    private NumCell footerTotal;
    private string? footerLabel;
    private float footerLabelWidth;
    private int footerDeaths = -1;
    private string footerDeathsText = string.Empty;
    private float footerDeathsWidth;

    // Tooltip der gehoverten Zeile. Die Texte werden nur gebaut, solange gehovert wird, und nur
    // neu, wenn sich Datensatz, Messgroesse, Anteil, Rate oder Schriftgroesse geaendert haben.
    private const int MaxTipLines = 8;
    private const float TipGap = 16f;
    private int hoveredRow = -1;
    private float headerBottom;
    private float tableBottom;

    private CombatantSnapshot? tipSource;
    private MeterMetric tipMetric;
    private float tipFontSize = -1f;
    private long tipShareKey = -1;
    private long tipRateKey = -1;
    private int tipLineCount;
    private readonly string[] tipLabels = new string[MaxTipLines];
    private readonly string[] tipValues = new string[MaxTipLines];
    private readonly float[] tipValueWidths = new float[MaxTipLines];
    private string tipName = string.Empty;
    private string tipJob = string.Empty;
    private float tipNameWidth;
    private string tipAbility = string.Empty;
    private float tipAbilityWidth;
    private float tipWidth;

    private readonly uint[] barColors = new uint[64];
    private int barColorAlpha = -1;

    // Mini-Verlauf. Die projizierten Punkte (relativ zur linken oberen Ecke des Verlaufs, Pixel) werden
    // nur neu gerechnet, wenn sich Reihe, Version, Breite oder Hoehe aendern; je Frame wird nur verschoben
    // und gezeichnet. Die Felder liegen im Draw-Thread, die Arrays wachsen nur mit der Breite.
    private const float GraphBaseHeight = 18f;
    private const float GraphInset = 0.75f;
    private const float GraphLineWidth = 1.5f;
    private const float GraphFillAlpha = 0.18f;
    private GraphSeries? graphSource;
    private int graphVersion = -1;
    private int graphWidth;
    private int graphHeight;
    private Vector2[] graphPoints = new Vector2[512];
    private Vector2[] graphScratch = new Vector2[512];
    private int graphPointCount;
    private float graphOpacity = -1f;
    private uint graphFill;

    /// <summary>Verlauf der gezeigten Messgroesse; null, wenn es keinen gibt. Wird vom Plugin gesetzt.</summary>
    public GraphSeries? Graph;

    public bool Visible = true;

    /// <summary>Liegt in der Konfiguration, damit Einstellungsfenster und Rechtsklick-Menue denselben Wert zeigen.</summary>
    public DpsMode Mode
    {
        get => config.Mode;
        set
        {
            if (config.Mode == value)
                return;

            config.Mode = value;
            config.Save();
        }
    }

    public string HeaderTitle = string.Empty;
    public string HeaderDuration = "00:00";
    public double TotalDps;
    public int Deaths;
    public bool Frozen;
    public bool Connected;

    // Kampfverlauf. Der Plugin-Teil setzt die Werte nur bei Aenderung des Verlaufs; gezeichnet wird
    // aus diesen Feldern, ohne etwas zu kopieren oder zu bauen.

    /// <summary>Wahr, wenn es einen Eintrag gibt, zu dem man blaettern kann; sonst fehlen Knoepfe und Menuepunkte.</summary>
    public bool HistoryAvailable;

    /// <summary>Position des gezeigten Eintrags, etwa "2/10"; null bei Live.</summary>
    public string? HistoryText;

    public bool CanOlder;
    public bool CanNewer;

    /// <summary>Menuetexte aller Eintraege, neuester zuerst, mit eindeutiger ImGui-Kennung.</summary>
    public string[] HistoryLabels = [];

    /// <summary>Index des gezeigten Eintrags, -1 fuer Live.</summary>
    public int HistorySelected = -1;

    /// <summary>Erster im Menue sichtbarer Eintrag; davor liegt der Kampf, den Live schon zeigt.</summary>
    public int HistoryFirst;

    public Action? OlderRequested;
    public Action? NewerRequested;

    /// <summary>Waehlt einen Eintrag; -1 ist Live.</summary>
    public Action<int>? SelectRequested;

    private string? historyTextSource;
    private float historyTextWidth;
    private float chevronLeftWidth;
    private float chevronRightWidth;

    /// <summary>Messwerte fuer die Fusszeile, wenn Diagnostics eingeschaltet ist.</summary>
    public string DiagnosticsText = string.Empty;

    /// <summary>Was gemessen wird. Bestimmt Spalten, Beschriftungen und Datenquelle.</summary>
    public MeterMetric Metric = MeterMetric.Damage;

    /// <summary>Name des eigenen Charakters, fuer die Hervorhebung der eigenen Zeile.</summary>
    public string? LocalPlayerName;

    /// <summary>Wird vom Zahnrad in der Kopfzeile und vom Menue ausgeloest.</summary>
    public Action? OpenConfigRequested;

    /// <summary>
    /// Uebernimmt die Zeilen. Die Liste wird nur kopiert, der Aufrufer darf sie danach
    /// wiederverwenden.
    /// </summary>
    public void SetRows(IReadOnlyList<MeterRow> value)
    {
        lock (this.rowsGate)
        {
            if (this.inbox.Length < value.Count)
                this.inbox = new MeterRow[Math.Max(value.Count, this.inbox.Length * 2)];

            for (var i = 0; i < value.Count; i++)
                this.inbox[i] = value[i];

            this.inboxCount = value.Count;
        }
    }

    private double ValueFor(MeterRow r, uint column) => column switch
    {
        ColumnTotal => r.Total,
        ColumnCrit => this.Metric == MeterMetric.Healing ? r.OverhealPercent : r.CritPercent,
        ColumnDirectHit => r.DirectHitPercent,
        _ => this.Mode == DpsMode.Rolling ? r.RollingRate : r.EncounterRate
    };

    private string RateLabel => this.Metric switch
    {
        MeterMetric.Healing => "HPS",
        MeterMetric.DamageTaken => "DTPS",
        _ => "DPS"
    };

    /// <summary>
    /// "DPS now" und Verwandte als feste Zeichenketten: Der Verweis dient zugleich als
    /// Schluessel des Breiten-Zwischenspeichers.
    /// </summary>
    private string FooterLabel => (this.Metric, this.Mode) switch
    {
        (MeterMetric.Healing, DpsMode.Rolling) => "HPS now",
        (MeterMetric.Healing, _) => "HPS fight",
        (MeterMetric.DamageTaken, DpsMode.Rolling) => "DTPS now",
        (MeterMetric.DamageTaken, _) => "DTPS fight",
        (_, DpsMode.Rolling) => "DPS now",
        _ => "DPS fight"
    };

    private void SetMetric(MeterMetric metric)
    {
        if (metric == this.Metric)
            return;

        this.Metric = metric;

        // Nach DPS sortiert bleibt nach dem Wechsel sinnvoll; eine Sortierung nach
        // CRIT dagegen zeigt bei Heilung auf Overheal und bei erlittenem Schaden ins
        // Leere. In dem Fall auf die Ratenspalte zurueckfallen.
        if (this.sortColumn == ColumnDirectHit && metric != MeterMetric.Damage)
            this.sortColumn = ColumnDps;

        if (this.sortColumn == ColumnCrit && metric == MeterMetric.DamageTaken)
            this.sortColumn = ColumnDps;
    }

    public void Draw()
    {
        if (!this.Visible)
            return;

        var opacity = Math.Clamp(config.BackgroundAlpha, 0f, 1f);

        using var theme = Theme.Push();

        // Nach dem Theme gesetzt, damit nur dieses Fenster Deckkraft und Abstaende bekommt.
        // Die Kopfzeile der Tabelle zeichnen wir selbst, ihr Grund bleibt durchsichtig.
        ImGui.PushStyleColor(ImGuiCol.WindowBg, Theme.Background with { W = opacity });
        ImGui.PushStyleColor(ImGuiCol.TableHeaderBg, new Vector4(0, 0, 0, 0));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8, 5));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(ItemSpacingX, ItemSpacingY));
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(CellPadX, CellPadY));
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(6, 2));
        using var own = new Theme.Scope(2, 4);

        // Nie Titelleiste: Das Fenster soll wie ein Overlay wirken. Verschoben wird durch
        // Ziehen an einer freien Stelle im Fenster.
        var flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;

        if (config.Locked)
        {
            // Gesperrt heisst: unverrueckbar und fuer Klicks durchlaessig, damit im Kampf
            // das Spiel getroffen wird und nicht das Fenster. Entsperren ueber die
            // Einstellungen oder /endetailer lock.
            flags |= ImGuiWindowFlags.NoMove
                | ImGuiWindowFlags.NoResize
                | ImGuiWindowFlags.NoInputs
                | ImGuiWindowFlags.NoNav;
        }

        ImGui.SetNextWindowSize(new Vector2(420, 220), ImGuiCond.FirstUseEver);

        if (!ImGui.Begin("EnDetailer", ref this.Visible, flags))
        {
            ImGui.End();
            return;
        }

        ImGui.SetWindowFontScale(config.FontScale);
        RefreshMetrics();
        EnsureBarColors();

        Decor.Background(opacity);

        BuildColumns();
        TakeRows();

        DrawHeader(opacity);
        DrawGraph(opacity);
        DrawTable();
        DrawRowTooltip();
        DrawFooter();

        if (!config.Locked)
            DrawContextMenu();

        ImGui.SetWindowFontScale(1f);
        ImGui.End();
    }

    /// <summary>
    /// Misst alles, was nur von der Schriftgroesse abhaengt, und verwirft die davon
    /// abhaengigen Zwischenspeicher. Laeuft je Frame nur zwei Abfragen weit, solange die
    /// Groesse gleich bleibt.
    /// </summary>
    private void RefreshMetrics()
    {
        this.lineHeight = ImGui.GetTextLineHeight();
        this.frameHeight = ImGui.GetFrameHeight();

        var fontSize = ImGui.GetFontSize();
        if (fontSize == this.metricsFontSize)
            return;

        this.metricsFontSize = fontSize;

        for (var i = 0; i < this.rowCells.Length; i++)
            this.rowCells[i].Reset();

        this.footerTotal.Reset();
        this.footerLabel = null;
        this.footerDeaths = -1;
        this.titleSource = null;
        this.historyTextSource = null;

        for (var i = 0; i < MaxColumns; i++)
        {
            this.headerLabel[i] = null;
            this.setupLabel[i] = null;
        }

        this.durationWidth = ImGui.CalcTextSize("00:00").X;
        this.endedWidth = ImGui.CalcTextSize("ended").X;
        this.ellipsisWidth = ImGui.CalcTextSize(Ellipsis).X;

        (this.heartWidth, this.heartHeight) = IconSize(FontAwesomeIcon.Heartbeat);

        var tabs = 0f;
        foreach (var (icon, _) in MetricTabs)
            tabs += IconSize(icon).Width + 24;

        this.tabsWidth = tabs + ItemSpacingX * (MetricTabs.Length - 1);
        this.cogWidth = Widgets.IconButtonWidth(FontAwesomeIcon.Cog);
        this.chevronLeftWidth = Widgets.IconButtonWidth(FontAwesomeIcon.ChevronLeft);
        this.chevronRightWidth = Widgets.IconButtonWidth(FontAwesomeIcon.ChevronRight);
    }

    private static (float Width, float Height) IconSize(FontAwesomeIcon icon)
    {
        ImGui.PushFont(UiBuilder.IconFont);
        var size = ImGui.CalcTextSize(icon.ToIconString());
        ImGui.PopFont();
        return (size.X, size.Y);
    }

    /// <summary>Balkenfarben je Job fuer die eingestellte Deckkraft, nur bei Aenderung neu gebaut.</summary>
    private void EnsureBarColors()
    {
        if (this.barColorAlpha == config.BarAlpha)
            return;

        this.barColorAlpha = config.BarAlpha;
        for (var i = 0; i < this.barColors.Length; i++)
            this.barColors[i] = JobColors.Bar((Job)i, config.BarAlpha);
    }

    private uint BarColor(Job job) => (uint)job < (uint)this.barColors.Length
        ? this.barColors[(uint)job]
        : JobColors.Bar(job, config.BarAlpha);

    /// <summary>
    /// CRIT und Direct Hit beziehen sich bei IINACT auf ausgeteilten Schaden. Bei
    /// Heilung waeren sie irrefuehrend, dort steht stattdessen Overheal; bei
    /// erlittenem Schaden gibt es nichts Sinnvolles, dann entfallen sie ganz.
    /// </summary>
    private void BuildColumns()
    {
        this.columnCount = 0;
        AddColumn(ColumnName, "Name");
        AddColumn(ColumnTotal, "Total");

        switch (this.Metric)
        {
            case MeterMetric.Healing:
                AddColumn(ColumnCrit, "Over");
                break;
            case MeterMetric.DamageTaken:
                break;
            default:
                AddColumn(ColumnCrit, "CRIT");
                AddColumn(ColumnDirectHit, "DH");
                break;
        }

        AddColumn(ColumnDps, RateLabel);
    }

    private void AddColumn(uint id, string label)
    {
        this.columnIds[this.columnCount] = id;
        this.columnLabels[this.columnCount] = label;
        this.columnCount++;
    }

    /// <summary>
    /// Kopiert die Zeilen in die eigene Arbeitskopie und sortiert sie an Ort und Stelle.
    /// Einfuegesortierung, weil die Reihenfolge von Frame zu Frame fast gleich bleibt und
    /// sie stabil sortiert wie zuvor OrderBy.
    /// </summary>
    private void TakeRows()
    {
        // Die Balkenlaenge richtet sich nach der Groesse, nach der gerade sortiert
        // wird - bei Namenssortierung nach der DPS-Spalte, sonst waere sie sinnlos.
        var barColumn = this.sortColumn == ColumnName ? ColumnDps : this.sortColumn;

        lock (this.rowsGate)
        {
            var count = this.inboxCount;
            if (this.ordered.Length < count)
            {
                var size = Math.Max(count, this.ordered.Length * 2);
                this.ordered = new MeterRow[size];
                this.barValues = new double[size];
                Array.Resize(ref this.rowCells, size);
            }

            Array.Copy(this.inbox, this.ordered, count);
            this.orderedCount = count;
        }

        for (var i = 0; i < this.orderedCount; i++)
            this.barValues[i] = ValueFor(this.ordered[i], barColumn);

        var byName = this.sortColumn == ColumnName;

        for (var i = 1; i < this.orderedCount; i++)
        {
            var row = this.ordered[i];
            var value = this.barValues[i];
            var j = i - 1;

            while (j >= 0 && Before(row, value, this.ordered[j], this.barValues[j], byName))
            {
                this.ordered[j + 1] = this.ordered[j];
                this.barValues[j + 1] = this.barValues[j];
                j--;
            }

            this.ordered[j + 1] = row;
            this.barValues[j + 1] = value;
        }
    }

    private bool Before(MeterRow a, double aValue, MeterRow b, double bValue, bool byName)
    {
        var order = byName
            ? string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase)
            : aValue.CompareTo(bValue);

        return this.sortAscending ? order < 0 : order > 0;
    }

    // ---------------------------------------------------------------- Kopfband

    private void DrawHeader(float opacity)
    {
        var draw = ImGui.GetWindowDrawList();
        var start = ImGui.GetCursorScreenPos();
        var right = start.X + ImGui.GetContentRegionAvail().X;
        var rowHeight = this.frameHeight;
        var textY = start.Y + (rowHeight - this.lineHeight) / 2;
        var dotColor = this.Connected ? Palette.Success : Palette.Warning;

        Decor.HeaderBand(rowHeight, opacity, WindowRounding - 1);

        // Rechts: erst die Elemente von aussen nach innen platzieren, links bleibt der Rest.
        float limit;
        if (config.Locked)
        {
            // Gesperrt ist das Fenster klick-durchlaessig, die Tabs waeren nicht bedienbar.
            // Das Symbol der aktiven Messgroesse bleibt als Anzeige, damit klar ist, was die Zahlen meinen.
            var icon = MetricIcons[(int)this.Metric];
            ImGui.PushFont(UiBuilder.IconFont);
            var iconSize = ImGui.CalcTextSize(icon);
            var iconX = right - iconSize.X;
            draw.AddText(new Vector2(iconX, start.Y + (rowHeight - iconSize.Y) / 2), Palette.AccentLight, icon);
            ImGui.PopFont();

            var center = new Vector2(iconX - ItemSpacingX - DotRadius, start.Y + rowHeight / 2);
            draw.AddCircleFilled(center, DotRadius, dotColor);
            limit = center.X - DotRadius;
        }
        else
        {
            var cogX = right - this.cogWidth;
            ImGui.SetCursorScreenPos(new Vector2(cogX, start.Y));
            if (Widgets.IconButton("cog", FontAwesomeIcon.Cog, "Settings"))
                this.OpenConfigRequested?.Invoke();

            var tabsX = cogX - ItemSpacingX - this.tabsWidth;
            ImGui.SetCursorScreenPos(new Vector2(tabsX, start.Y));
            var selected = (int)this.Metric;
            if (Widgets.IconTabs("metric", MetricTabs, ref selected))
                SetMetric((MeterMetric)selected);

            var center = new Vector2(tabsX - ItemSpacingX - DotRadius, start.Y + rowHeight / 2);
            draw.AddCircleFilled(center, DotRadius, dotColor);
            limit = center.X - DotRadius;

            // Nur gezeichnet, kein Element: So bleibt der Cursor unberuehrt.
            var reach = new Vector2(DotRadius + 3, DotRadius + 3);
            if (ImGui.IsWindowHovered() && ImGui.IsMouseHoveringRect(center - reach, center + reach))
                ImGui.SetTooltip(this.Connected ? "IINACT connected" : "Waiting for IINACT…");

            if (this.HistoryAvailable)
            {
                // Links vor dem Punkt. Am Ende der Liste in Muted und ohne Wirkung, der Knopf bleibt
                // stehen, damit die Leiste nicht springt.
                var pairX = limit - ItemSpacingX - this.chevronLeftWidth - ItemSpacingX - this.chevronRightWidth;
                ImGui.SetCursorScreenPos(new Vector2(pairX, start.Y));
                if (HistoryButton("older", FontAwesomeIcon.ChevronLeft, "Older fight", this.CanOlder))
                    this.OlderRequested?.Invoke();

                ImGui.SetCursorScreenPos(new Vector2(pairX + this.chevronLeftWidth + ItemSpacingX, start.Y));
                if (HistoryButton("newer", FontAwesomeIcon.ChevronRight, "Newer fight", this.CanNewer))
                    this.NewerRequested?.Invoke();

                limit = pairX;
            }
        }

        // Links: Symbol, Dauer, Titel, bei eingefrorenem Kampf "ended".
        var x = start.X;
        ImGui.PushFont(UiBuilder.IconFont);
        draw.AddText(new Vector2(x, start.Y + (rowHeight - this.heartHeight) / 2), Palette.AccentLight, HeartIcon);
        ImGui.PopFont();
        x += this.heartWidth + ItemSpacingX;

        DrawText(draw, new Vector2(x, textY), Palette.AccentLight, this.HeaderDuration);
        x += this.durationWidth + ItemSpacingX;

        // Bei einem Verlaufseintrag steht die Position statt "ended" hinter dem Titel.
        var tag = this.HistoryText;
        if (tag is not null && !ReferenceEquals(tag, this.historyTextSource))
        {
            this.historyTextSource = tag;
            this.historyTextWidth = ImGui.CalcTextSize(tag).X;
        }

        var reserved = tag is not null
            ? ItemSpacingX + this.historyTextWidth
            : this.Frozen ? ItemSpacingX + this.endedWidth : 0f;
        FitTitle(this.HeaderTitle, limit - ItemSpacingX - x - reserved);

        if (this.titleShown.Length > 0)
        {
            DrawText(draw, new Vector2(x, textY), Palette.Text, this.titleShown);
            x += this.titleShownWidth + ItemSpacingX;
        }

        if (tag is not null)
            DrawText(draw, new Vector2(x, textY), Palette.Muted, tag);
        else if (this.Frozen)
            DrawText(draw, new Vector2(x, textY), Palette.Muted, "ended");

        ImGui.SetCursorScreenPos(start);
        ImGui.Dummy(new Vector2(right - start.X, rowHeight + 2));
    }

    // ---------------------------------------------------------------- Verlauf

    /// <summary>
    /// Linie mit Flaeche darunter ueber die volle Innenbreite. Der Platz wird auch ohne Punkte gehalten,
    /// damit die Tabelle beim Kampfbeginn nicht springt; ausgeschaltet gibt es weder Flaeche noch Platz.
    /// </summary>
    private void DrawGraph(float opacity)
    {
        if (!config.ShowGraph)
            return;

        var height = Math.Max(4, (int)MathF.Round(GraphBaseHeight * ImGuiHelpers.GlobalScale * config.FontScale));
        var width = (int)MathF.Floor(ImGui.GetContentRegionAvail().X);
        var cursor = ImGui.GetCursorScreenPos();
        ImGui.Dummy(new Vector2(Math.Max(1, width), height));

        var graph = this.Graph;
        if (graph is null || graph.Count < 2 || width < 4)
            return;

        var version = graph.Version;
        if (!ReferenceEquals(graph, this.graphSource) || version != this.graphVersion
            || width != this.graphWidth || height != this.graphHeight)
        {
            ProjectGraph(graph, version, width, height);
        }

        if (this.graphPointCount < 2)
            return;

        // Auf ganze Pixel gelegt, damit die Linie nicht von Bild zu Bild flimmert.
        var originX = MathF.Round(cursor.X);
        var originY = MathF.Round(cursor.Y);
        var count = this.graphPointCount;

        for (var i = 0; i < count; i++)
            this.graphScratch[i] = new Vector2(originX + this.graphPoints[i].X, originY + this.graphPoints[i].Y);

        if (opacity != this.graphOpacity)
        {
            this.graphOpacity = opacity;
            this.graphFill = Theme.U32(Theme.Accent with { W = GraphFillAlpha * opacity });
        }

        var draw = ImGui.GetWindowDrawList();
        var bottom = originY + height;

        // Ein Viereck je Segment. Ohne Kantenglaettung, weil deren Saum an den gemeinsamen Kanten
        // sonst helle Streifen zwischen den Vierecken zeichnet.
        ref var flags = ref draw.Flags;
        var saved = flags;
        flags &= ~ImDrawListFlags.AntiAliasedFill;

        for (var i = 0; i < count - 1; i++)
        {
            var a = this.graphScratch[i];
            var b = this.graphScratch[i + 1];
            draw.AddQuadFilled(a, b, new Vector2(b.X, bottom), new Vector2(a.X, bottom), this.graphFill);
        }

        flags = saved;
        draw.AddPolyline(ref this.graphScratch[0], count, Palette.Accent, ImDrawFlags.None, GraphLineWidth);
    }

    /// <summary>
    /// Rechnet die Punkte in Pixel um. Gibt es mehr Punkte als Pixelspalten, zaehlt je Spalte der hoechste
    /// Wert, damit Spitzen nicht verschwinden.
    /// </summary>
    private void ProjectGraph(GraphSeries graph, int version, int width, int height)
    {
        this.graphSource = graph;
        this.graphVersion = version;
        this.graphWidth = width;
        this.graphHeight = height;

        var n = graph.Count;
        var max = graph.Max;
        var columns = Math.Min(n, width);

        if (this.graphPoints.Length < columns)
        {
            var size = Math.Max(columns, this.graphPoints.Length * 2);
            this.graphPoints = new Vector2[size];
            this.graphScratch = new Vector2[size];
        }

        var span = width - 2 * GraphInset;
        var baseline = height - 1f;
        var range = height - 2f;

        float YOf(double value) =>
            max > 0 ? baseline - (float)Math.Clamp(value / max, 0, 1) * range : baseline;

        if (n <= width)
        {
            for (var i = 0; i < n; i++)
                this.graphPoints[i] = new Vector2(GraphInset + i * span / (n - 1), YOf(graph[i]));
        }
        else
        {
            for (var c = 0; c < columns; c++)
                this.graphPoints[c] = new Vector2(GraphInset + c * span / (columns - 1), float.MaxValue);

            for (var i = 0; i < n; i++)
            {
                var c = (int)((long)i * columns / n);
                var y = YOf(graph[i]);
                if (y < this.graphPoints[c].Y)
                    this.graphPoints[c].Y = y;
            }
        }

        this.graphPointCount = columns;
    }

    /// <summary>Blaetterknopf; ohne weiteren Eintrag in Muted, und ein Klick bleibt dann ohne Wirkung.</summary>
    private static bool HistoryButton(string id, FontAwesomeIcon icon, string tooltip, bool enabled)
    {
        if (enabled)
            return Widgets.IconButton(id, icon, tooltip);

        ImGui.PushStyleColor(ImGuiCol.Text, Theme.Muted);
        Widgets.IconButton(id, icon, tooltip);
        ImGui.PopStyleColor();
        return false;
    }

    /// <summary>
    /// Kuerzt den Titel mit "..." auf den verfuegbaren Platz. Gemessen wird nur, wenn sich
    /// Titel oder Platz geaendert haben.
    /// </summary>
    private void FitTitle(string title, float available)
    {
        var key = MathF.Floor(available);
        if (this.titleSource is not null && key == this.titleAvail && this.titleSource == title)
            return;

        this.titleSource = title;
        this.titleAvail = key;

        if (title.Length == 0 || available <= this.ellipsisWidth)
        {
            this.titleShown = string.Empty;
            this.titleShownWidth = 0;
            return;
        }

        var full = ImGui.CalcTextSize(title).X;
        if (full <= available)
        {
            this.titleShown = title;
            this.titleShownWidth = full;
            return;
        }

        // Laengster Anfang, der mit Auslassungszeichen noch passt.
        var low = 0;
        var high = title.Length;
        while (low < high)
        {
            var mid = (low + high + 1) / 2;
            if (ImGui.CalcTextSize(title[..mid] + Ellipsis).X <= available)
                low = mid;
            else
                high = mid - 1;
        }

        this.titleShown = title[..low] + Ellipsis;
        this.titleShownWidth = ImGui.CalcTextSize(this.titleShown).X;
    }

    // ---------------------------------------------------------------- Tabelle

    private void DrawTable()
    {
        // ScrollY reserviert dauerhaft Platz fuer die Bildlaufleiste am rechten Rand,
        // auch wenn gar nicht gescrollt wird. Deshalb nur bei vielen Zeilen setzen.
        // PadOuterX, weil sonst die Zellen am Rand klebten und der Akzentstreifen der
        // eigenen Zeile ausserhalb des Zeichenbereichs laege.
        var flags = ImGuiTableFlags.PadOuterX;
        if (this.orderedCount > 12)
            flags |= ImGuiTableFlags.ScrollY;

        // Platz fuer die Fusszeile lassen.
        var height = -(this.lineHeight + ItemSpacingY + 2f);

        // Untere Kante der Tabelle, damit bei Bildlauf keine verdeckte Zeile unter der Maus liegt.
        this.hoveredRow = -1;
        this.tableBottom = ImGui.GetCursorScreenPos().Y + ImGui.GetContentRegionAvail().Y + height;

        if (!ImGui.BeginTable("##endetailer", this.columnCount, flags, new Vector2(0, height), 0))
            return;

        // Der Name nimmt den Rest, die Zahlenspalten liegen fest rechts und bekommen genau
        // so viel Platz, wie Ueberschrift plus Sortierpfeil brauchen. Andersherum - eine
        // dehnbare Zahlenspalte - loest sich vom Rest: sie schnappt sich den ganzen
        // Restplatz und bewegt sich beim Verkleinern nicht mit den anderen.
        for (var i = 0; i < this.columnCount; i++)
        {
            var id = this.columnIds[i];
            var label = this.columnLabels[i];

            if (id == ColumnName)
            {
                ImGui.TableSetupColumn(label, ImGuiTableColumnFlags.WidthStretch, 0, id);
                continue;
            }

            if (!ReferenceEquals(this.setupLabel[i], label))
            {
                this.setupLabel[i] = label;
                this.setupWidth[i] = ImGui.CalcTextSize(label + " v").X + 16f;
            }

            ImGui.TableSetupColumn(label, ImGuiTableColumnFlags.WidthFixed, this.setupWidth[i] + 8, id);
        }

        ImGui.TableSetupScrollFreeze(0, 1);
        DrawHeaderRow();
        DrawRows();

        ImGui.EndTable();
    }

    /// <summary>
    /// Eigene Kopfzeile statt TableHeadersRow. Deren Beschriftung sitzt immer links
    /// und haelt rechts Platz fuer den Sortierpfeil frei - Ueberschrift und Wert
    /// koennen dadurch nie buendig stehen. Selbst gezeichnet liegt beides rechts
    /// auf derselben Kante, und die Sortierung fuehren wir ohnehin selbst. Die Zellen
    /// liefern zugleich die Spaltengeometrie fuer die Zeilen darunter.
    /// </summary>
    private void DrawHeaderRow()
    {
        ImGui.TableNextRow(ImGuiTableRowFlags.Headers);

        var draw = ImGui.GetWindowDrawList();

        for (var i = 0; i < this.columnCount; i++)
        {
            var id = this.columnIds[i];
            ImGui.TableSetColumnIndex(i);

            var pos = ImGui.GetCursorScreenPos();
            var available = ImGui.GetContentRegionAvail().X;
            this.columnStartX[i] = pos.X;
            this.columnAvail[i] = available;
            this.headerBottom = pos.Y + this.lineHeight + CellPadY;

            // Die ganze Zellbreite als Klickflaeche, damit man nicht die Schrift
            // treffen muss.
            if (ImGui.InvisibleButton(HeaderIds[i], new Vector2(Math.Max(1, available), this.lineHeight)))
                ToggleSort(id);

            var label = this.columnLabels[i];
            var arrow = id == this.sortColumn ? (this.sortAscending ? 1 : 2) : 0;

            if (!ReferenceEquals(this.headerLabel[i], label) || this.headerArrow[i] != arrow)
            {
                this.headerLabel[i] = label;
                this.headerArrow[i] = arrow;
                this.headerText[i] = arrow switch
                {
                    1 => label + " ^",
                    2 => label + " v",
                    _ => label
                };
                this.headerWidth[i] = ImGui.CalcTextSize(this.headerText[i]).X;
            }

            var x = id == ColumnName ? pos.X : Math.Max(pos.X, pos.X + available - this.headerWidth[i]);
            DrawText(draw, new Vector2(x, pos.Y), arrow != 0 ? Palette.AccentLight : Palette.Muted, this.headerText[i]);
        }

        var last = this.columnCount - 1;
        this.rowRightEdge = this.columnStartX[last] + this.columnAvail[last] + CellPadX;
        this.barSpan = BarSpan();
    }

    private void ToggleSort(uint column)
    {
        if (this.sortColumn == column)
        {
            this.sortAscending = !this.sortAscending;
            return;
        }

        this.sortColumn = column;
        this.sortAscending = false;
    }

    private void DrawRows()
    {
        var count = this.orderedCount;
        var peak = 1d;
        for (var i = 0; i < count; i++)
            peak = Math.Max(peak, this.barValues[i]);

        var draw = ImGui.GetWindowDrawList();
        var lh = this.lineHeight;
        var barHeight = lh + 3;
        var me = this.LocalPlayerName;

        // Gesperrt ist das Fenster klick-durchlaessig und bekommt keinen Hover. Auch beim
        // Ziehen oder bei offenem Menue meldet ImGui das Fenster nicht als gehovert.
        var canHover = !config.Locked && ImGui.IsWindowHovered(ImGuiHoveredFlags.ChildWindows);
        var mouse = canHover ? ImGui.GetMousePos() : default;

        for (var index = 0; index < count; index++)
        {
            var row = this.ordered[index];
            ref var cells = ref this.rowCells[index];

            ImGui.TableNextRow();

            var rowY = 0f;
            var barEndX = float.MinValue;
            var barColor = BarColor(row.Job);

            for (var i = 0; i < this.columnCount; i++)
            {
                var id = this.columnIds[i];
                ImGui.TableNextColumn();

                if (i == 0)
                {
                    // Erst in der ersten Zelle bekannt: Vor TableNextRow steht der Cursor noch
                    // am Ende der vorigen Zeile.
                    rowY = ImGui.GetCursorScreenPos().Y;
                    barEndX = BarEndX(this.barValues[index], peak);
                    DrawRowBackground(draw, index, rowY, lh, config.HighlightSelf && me is not null && row.Name == me);

                    if (canHover
                        && mouse.Y >= rowY - 1 && mouse.Y < rowY - 1 + barHeight
                        && mouse.Y >= this.headerBottom && mouse.Y < this.tableBottom
                        && mouse.X >= this.columnStartX[0] - CellPadX && mouse.X < this.rowRightEdge)
                    {
                        this.hoveredRow = index;
                    }
                }

                // Der Balken wird je Zelle segmentweise gezeichnet: Ein breites Rechteck
                // aus der ersten Spalte landet in deren Zeichenebene und liegt ueber den
                // Zahlen der anderen. Segmentweise gehoert jedes Stueck zu seiner Zelle und
                // liegt unter deren Text.
                var left = this.columnStartX[i] - CellPadX;
                if (barEndX > left)
                {
                    var cellRight = this.columnStartX[i] + this.columnAvail[i] + CellPadX;
                    draw.AddRectFilled(
                        new Vector2(left, rowY - 1),
                        new Vector2(Math.Min(cellRight, barEndX), rowY - 1 + barHeight),
                        barColor);
                }

                switch (id)
                {
                    case ColumnName:
                        DrawNameCell(draw, row, this.columnStartX[i], rowY, lh);
                        break;
                    case ColumnTotal:
                        DrawNumber(draw, ref cells.Total, NumberKey(row.Total), false, i, rowY);
                        break;
                    case ColumnCrit:
                        DrawNumber(draw, ref cells.Crit,
                            PercentKey(this.Metric == MeterMetric.Healing ? row.OverhealPercent : row.CritPercent),
                            true, i, rowY);
                        break;
                    case ColumnDirectHit:
                        DrawNumber(draw, ref cells.DirectHit, PercentKey(row.DirectHitPercent), true, i, rowY);
                        break;
                    default:
                        DrawNumber(draw, ref cells.Rate,
                            NumberKey(this.Mode == DpsMode.Rolling ? row.RollingRate : row.EncounterRate),
                            false, i, rowY);
                        break;
                }
            }
        }
    }

    /// <summary>Rechte Kante des Balkens in Bildschirmkoordinaten.</summary>
    private float BarEndX(double value, double peak)
    {
        var fraction = (float)Math.Clamp(value / peak, 0, 1);
        if (fraction <= 0)
            return float.MinValue;

        return this.columnStartX[0] - CellPadX + this.barSpan * fraction;
    }

    /// <summary>Wie breit der Balken bei voller Auslastung waere; die Spaltenbreiten stehen erst zur Laufzeit fest.</summary>
    private float BarSpan()
    {
        var left = this.columnStartX[0];
        var right = config.BarExtent switch
        {
            BarExtent.NameOnly => this.columnStartX[1],
            BarExtent.FullRow => this.rowRightEdge,

            // "bis Total" heisst: bis zum Beginn der uebernaechsten Spalte. Bei
            // Metriken mit weniger Spalten gibt es die unter Umstaenden nicht.
            _ => this.columnCount > 2 && this.columnStartX[2] > this.columnStartX[1]
                ? this.columnStartX[2]
                : this.rowRightEdge
        };

        return right <= left ? 0 : right - left + CellPadX;
    }

    /// <summary>
    /// Zeilengrund: Zebra in der Header-Farbe, die eigene Zeile mit leichter Flaeche und
    /// Akzentstreifen links, damit man sich in einer vollen Gruppe sofort findet.
    /// </summary>
    private void DrawRowBackground(ImDrawListPtr draw, int index, float rowY, float lh, bool isSelf)
    {
        if (isSelf)
        {
            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, Palette.SelfFill);

            var left = this.columnStartX[0] - CellPadX;
            draw.AddRectFilled(new Vector2(left, rowY - 1), new Vector2(left + 2.5f, rowY - 1 + lh + 3), Palette.Accent);
            return;
        }

        if (config.RowStripes && index % 2 == 1)
            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, Palette.Zebra);
    }

    private void DrawNameCell(ImDrawListPtr draw, MeterRow row, float x, float y, float lh)
    {
        if (config.ShowJobIcons)
        {
            var iconId = Jobs.IconId(row.Job);
            if (iconId != 0)
            {
                // Die Textur wird bewusst nicht zwischengespeichert: Dalamud rat davon ab
                // und der Gewinn ist gering.
                var texture = textures.GetFromGameIcon(new GameIconLookup(iconId)).GetWrapOrEmpty();
                draw.AddImage(texture.Handle, new Vector2(x, y), new Vector2(x + lh, y + lh));
            }

            x += lh + 4;
        }

        DrawText(draw, new Vector2(x, y), Palette.Text, row.Name);

        // Gibt der Zeile ihre Hoehe; alles andere ist gezeichnet, nicht gesetzt.
        ImGui.Dummy(new Vector2(1, lh));
    }

    /// <summary>Rechtsbuendig in der Zelle, auf derselben Kante wie die Ueberschrift.</summary>
    private void DrawNumber(ImDrawListPtr draw, ref NumCell cell, long key, bool percent, int column, float y)
    {
        if (cell.Text is null || cell.Key != key)
        {
            cell.Key = key;
            cell.Text = percent ? PercentText(key) : NumberText(key);
            cell.Width = ImGui.CalcTextSize(cell.Text).X;
        }

        var left = this.columnStartX[column];
        var x = Math.Max(left, left + this.columnAvail[column] - cell.Width);
        DrawText(draw, new Vector2(x, y), Palette.Text, cell.Text);
    }

    /// <summary>
    /// Schrift vom Hintergrund abgesetzt. ImGui kennt weder Schatten noch Kontur,
    /// beides wird durch vorgezeichnete dunkle Kopien erzeugt. Die Kontur ringsum
    /// wirkt bei kleiner Schrift schnell matschig, deshalb ist der einfache
    /// Schatten die Voreinstellung.
    /// </summary>
    private void DrawText(ImDrawListPtr draw, Vector2 pos, uint color, string text)
    {
        switch (config.TextStyle)
        {
            case TextStyle.Shadow:
                draw.AddText(pos + new Vector2(1, 1), Palette.Shadow, text);
                break;
            case TextStyle.Outline:
                draw.AddText(pos with { X = pos.X - 1 }, Palette.Shadow, text);
                draw.AddText(pos with { X = pos.X + 1 }, Palette.Shadow, text);
                draw.AddText(pos with { Y = pos.Y - 1 }, Palette.Shadow, text);
                draw.AddText(pos with { Y = pos.Y + 1 }, Palette.Shadow, text);
                break;
        }

        draw.AddText(pos, color, text);
    }

    // ---------------------------------------------------------------- Tooltip

    private const string NoValue = "–";

    /// <summary>
    /// Details zur gehoverten Zeile im En-Stil. Die Werte stammen aus dem Datensatz, den die
    /// Zeile mitfuehrt, also aus demselben Stand wie die Tabelle - auch im eingefrorenen Kampf.
    /// </summary>
    private void DrawRowTooltip()
    {
        if (this.hoveredRow < 0 || this.hoveredRow >= this.orderedCount)
            return;

        var row = this.ordered[this.hoveredRow];
        if (row.Detail is not { } detail)
            return;

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10, 10));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, Theme.CardRounding);
        ImGui.BeginTooltip();
        ImGui.PopStyleVar(2);

        ImGui.SetWindowFontScale(config.FontScale);
        EnsureTooltip(row, detail);

        var draw = ImGui.GetWindowDrawList();
        var lh = ImGui.GetTextLineHeight();
        var width = this.tipWidth;

        var pos = ImGui.GetCursorScreenPos();
        draw.AddText(pos, Palette.AccentLight, this.tipName);
        if (this.tipJob.Length > 0)
            draw.AddText(new Vector2(pos.X + this.tipNameWidth + 6, pos.Y), Palette.Muted, this.tipJob);
        ImGui.Dummy(new Vector2(width, lh + 3));

        for (var i = 0; i < this.tipLineCount; i++)
        {
            pos = ImGui.GetCursorScreenPos();
            draw.AddText(pos, Palette.Muted, this.tipLabels[i]);
            draw.AddText(new Vector2(pos.X + width - this.tipValueWidths[i], pos.Y), Palette.Text, this.tipValues[i]);
            ImGui.Dummy(new Vector2(width, lh));

            // Der Name der Faehigkeit steht leise unter dem Wert.
            if (this.tipAbility.Length == 0 || !IsMaxLine(this.tipLabels[i]))
                continue;

            pos = ImGui.GetCursorScreenPos();
            draw.AddText(new Vector2(pos.X + width - this.tipAbilityWidth, pos.Y), Palette.Muted, this.tipAbility);
            ImGui.Dummy(new Vector2(width, lh));
        }

        ImGui.SetWindowFontScale(1f);
        ImGui.EndTooltip();
    }

    private static bool IsMaxLine(string label) => label is "Max hit" or "Max heal";

    /// <summary>Baut Texte und Breiten neu, wenn sich etwas geaendert hat; sonst bleibt alles stehen.</summary>
    private void EnsureTooltip(MeterRow row, CombatantSnapshot detail)
    {
        var sum = 0d;
        for (var i = 0; i < this.orderedCount; i++)
            sum += this.ordered[i].Total;

        var share = sum > 0 ? row.Total / sum : 0;
        var shareKey = Round(share * 1000);
        var rateKey = NumberKey(row.EncounterRate);
        var fontSize = ImGui.GetFontSize();

        if (ReferenceEquals(detail, this.tipSource) && this.Metric == this.tipMetric
            && fontSize == this.tipFontSize && shareKey == this.tipShareKey && rateKey == this.tipRateKey)
            return;

        this.tipSource = detail;
        this.tipMetric = this.Metric;
        this.tipFontSize = fontSize;
        this.tipShareKey = shareKey;
        this.tipRateKey = rateKey;
        this.tipLineCount = 0;
        this.tipAbility = string.Empty;

        var shareText = FormattableString.Invariant($"{shareKey / 10}.{shareKey % 10}%");
        var rateText = NumberText(rateKey);

        switch (this.Metric)
        {
            case MeterMetric.Healing:
                AddTipLine("Share", shareText);
                AddTipLine("Fight HPS", rateText);
                AddTipLine("Max heal", MaxText(detail.MaxHealValue));
                AddTipLine("Overheal", Percent(detail.OverhealPercent));
                AddTipLine("Shields", MaxText(detail.ShieldAmount));
                AddTipLine("Crit heals", Percent(detail.CritHealPercent));
                if (detail.MaxHealValue > 0)
                    this.tipAbility = detail.MaxHealName;
                break;
            case MeterMetric.DamageTaken:
                AddTipLine("Heals taken", MaxText(detail.HealingTaken));
                AddTipLine("Parry", Percent(detail.ParryPercent));
                AddTipLine("Block", Percent(detail.BlockPercent));
                AddTipLine("Deaths", detail.Deaths.ToString(CultureInfo.InvariantCulture));
                break;
            default:
                AddTipLine("Share", shareText);
                AddTipLine("Fight DPS", rateText);
                AddTipLine("Max hit", MaxText(detail.MaxHitValue));
                AddTipLine("Crit", Percent(detail.CritPercent));
                AddTipLine("Direct hit", Percent(detail.DirectHitPercent));
                AddTipLine("Crit DH", Percent(detail.CritDirectHitPercent));
                AddTipLine("Deaths", detail.Deaths.ToString(CultureInfo.InvariantCulture));
                if (detail.MaxHitValue > 0)
                    this.tipAbility = detail.MaxHitName;
                break;
        }

        this.tipName = detail.Name;
        this.tipJob = detail.Job == Job.Unknown ? string.Empty : detail.Job.ToString();
        this.tipNameWidth = ImGui.CalcTextSize(this.tipName).X;
        this.tipAbilityWidth = this.tipAbility.Length > 0 ? ImGui.CalcTextSize(this.tipAbility).X : 0;

        var headerWidth = this.tipNameWidth + (this.tipJob.Length > 0 ? 6 + ImGui.CalcTextSize(this.tipJob).X : 0);
        var labelMax = 0f;
        var valueMax = 0f;
        for (var i = 0; i < this.tipLineCount; i++)
        {
            labelMax = Math.Max(labelMax, ImGui.CalcTextSize(this.tipLabels[i]).X);
            valueMax = Math.Max(valueMax, this.tipValueWidths[i]);
        }

        this.tipWidth = Math.Max(Math.Max(headerWidth, this.tipAbilityWidth), labelMax + TipGap + valueMax);
    }

    private void AddTipLine(string label, string value)
    {
        var i = this.tipLineCount++;
        this.tipLabels[i] = label;
        this.tipValues[i] = value;
        this.tipValueWidths[i] = ImGui.CalcTextSize(value).X;
    }

    /// <summary>Zahl im Zahlenformat des Meters; null heisst nichts vorhanden.</summary>
    private static string MaxText(double value) => value > 0 ? NumberText(NumberKey(value)) : NoValue;

    private static string Percent(double value) => PercentText(PercentKey(value));

    // ---------------------------------------------------------------- Fusszeile

    private void DrawFooter()
    {
        var draw = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();

        if (config.ShowDiagnostics)
        {
            DrawText(draw, pos, Palette.Muted, this.DiagnosticsText);
        }
        else
        {
            var key = NumberKey(this.TotalDps);
            if (this.footerTotal.Text is null || this.footerTotal.Key != key)
            {
                this.footerTotal.Key = key;
                this.footerTotal.Text = NumberText(key);
                this.footerTotal.Width = ImGui.CalcTextSize(this.footerTotal.Text).X;
            }

            var label = FooterLabel;
            if (!ReferenceEquals(label, this.footerLabel))
            {
                this.footerLabel = label;
                this.footerLabelWidth = ImGui.CalcTextSize(label).X;
            }

            if (this.Deaths != this.footerDeaths)
            {
                this.footerDeaths = this.Deaths;
                this.footerDeathsText = "Deaths " + this.Deaths.ToString(CultureInfo.InvariantCulture);
                this.footerDeathsWidth = ImGui.CalcTextSize(this.footerDeathsText).X;
            }

            var x = pos.X;
            DrawText(draw, new Vector2(x, pos.Y), Palette.AccentLight, this.footerTotal.Text);
            x += this.footerTotal.Width + 5;
            DrawText(draw, new Vector2(x, pos.Y), Palette.Muted, label);
            x += this.footerLabelWidth + 12;
            DrawText(draw, new Vector2(x, pos.Y), Palette.Muted, this.footerDeathsText);
        }

        ImGui.Dummy(new Vector2(1, this.lineHeight));
    }

    // ---------------------------------------------------------------- Menue

    /// <summary>
    /// Rechtsklick-Menue im En-Stil. Die Zeitbasis braucht man selten genug, dass sie nur
    /// hier steht; die Messgroesse hat zusaetzlich ihre Tabs im Kopfband.
    /// </summary>
    private void DrawContextMenu()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10, 8));
        var open = ImGui.BeginPopupContextWindow("##menu");
        ImGui.PopStyleVar();

        if (!open)
            return;

        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8, 5));

        ImGui.TextColored(Theme.AccentLight, "EnDetailer");
        ImGui.Separator();

        if (MenuEntry("Damage", this.Metric == MeterMetric.Damage))
            SetMetric(MeterMetric.Damage);
        if (MenuEntry("Healing", this.Metric == MeterMetric.Healing))
            SetMetric(MeterMetric.Healing);
        if (MenuEntry("Damage taken", this.Metric == MeterMetric.DamageTaken))
            SetMetric(MeterMetric.DamageTaken);

        ImGui.Separator();

        if (this.HistoryAvailable)
        {
            ImGui.TextColored(Theme.Muted, "History");

            if (MenuEntry("Live", this.HistorySelected == FightHistory.LiveIndex))
                this.SelectRequested?.Invoke(FightHistory.LiveIndex);

            for (var i = this.HistoryFirst; i < this.HistoryLabels.Length; i++)
            {
                if (MenuEntry(this.HistoryLabels[i], this.HistorySelected == i))
                    this.SelectRequested?.Invoke(i);
            }

            ImGui.Separator();
        }

        if (MenuEntry("Now", this.Mode == DpsMode.Rolling))
            this.Mode = DpsMode.Rolling;
        if (MenuEntry("Fight", this.Mode == DpsMode.Encounter))
            this.Mode = DpsMode.Encounter;

        ImGui.Separator();

        if (MenuEntry("Lock", false))
        {
            config.Locked = true;
            config.Save();
        }

        if (MenuEntry("Settings", false))
            this.OpenConfigRequested?.Invoke();

        ImGui.PopStyleVar();
        ImGui.EndPopup();
    }

    /// <summary>Eintrag des Menues; der aktive steht in AccentLight, weil die Auswahlflaeche kaum vom Hover abweicht.</summary>
    private static bool MenuEntry(string label, bool active)
    {
        if (active)
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.AccentLight);

        var clicked = ImGui.Selectable(label, active);

        if (active)
            ImGui.PopStyleColor();

        return clicked;
    }

    // ---------------------------------------------------------------- Zahlenformat

    private static long PercentKey(double value) => Round(value);

    private static string PercentText(long key) => key.ToString(CultureInfo.InvariantCulture) + "%";

    /// <summary>
    /// Schluessel fuer den Anzeigewert: die gerundete Zahl in der Einheit der Anzeige,
    /// mal vier plus die Stufe. Gleicher Schluessel heisst gleicher Text.
    ///
    /// Ab zehntausend entfaellt die Nachkommastelle: Bei 10.000 DPS entspricht sie
    /// hundert Punkten, also einem Prozent - sie flackert dauernd, ohne etwas
    /// auszusagen. Das Auge liest daraus Unruhe, wo real kaum Bewegung ist. Grober
    /// dargestellt steht die Zahl ruhig, obwohl darunter unveraendert genau
    /// gerechnet wird.
    /// </summary>
    private static long NumberKey(double value)
    {
        if (!double.IsFinite(value) || value < 0)
            value = 0;

        return value switch
        {
            >= 1_000_000 => Round(value / 10_000) * 4 + 3,
            >= 10_000 => Round(value / 1_000) * 4 + 2,
            >= 1_000 => Round(value / 100) * 4 + 1,
            _ => Round(value) * 4
        };
    }

    /// <summary>
    /// Bewusst kulturunabhaengig - der deutsche Tausenderpunkt liest sich in
    /// diesem Zusammenhang wie ein Dezimalkomma und stiftet Verwirrung.
    /// </summary>
    private static string NumberText(long key)
    {
        var units = key >> 2;
        return (key & 3) switch
        {
            3 => FormattableString.Invariant($"{units / 100}.{units % 100:00}M"),
            2 => FormattableString.Invariant($"{units}K"),
            1 => FormattableString.Invariant($"{units / 10}.{units % 10}K"),
            _ => units.ToString(CultureInfo.InvariantCulture)
        };
    }

    private static long Round(double value) => (long)Math.Round(value, MidpointRounding.AwayFromZero);
}
