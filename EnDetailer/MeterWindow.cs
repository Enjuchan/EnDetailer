using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Plugin.Services;
using EnDetailer.Core;

namespace EnDetailer;

public enum DpsMode
{
    Rolling,
    Encounter
}

/// <summary>
/// Die Tabelle. Optik an LMeter angelehnt, Bedienung an Details!: Klick auf einen
/// Spaltenkopf sortiert, das Dropdown schaltet die DPS-Spalte um.
/// </summary>
public sealed class MeterWindow(Configuration config, ITextureProvider textures, IUiBuilder uiBuilder)
{
    private const uint ColumnName = 0;
    private const uint ColumnTotal = 1;
    private const uint ColumnCrit = 2;
    private const uint ColumnDirectHit = 3;
    private const uint ColumnDps = 4;

    private IReadOnlyList<MeterRow> rows = [];
    private uint sortColumn = ColumnDps;
    private bool sortAscending;

    // Tatsaechliche Spaltenpositionen, beim Zeichnen gemessen. ImGui.GetColumnWidth
    // mit Index stammt aus der alten Spalten-API und liefert in Tabellen nichts
    // Brauchbares, deshalb wird die Geometrie hier selbst mitgeschrieben. Die Werte
    // stammen aus dem vorherigen Frame - bei 60 Bildern je Sekunde unsichtbar.
    private readonly float[] columnStartX = new float[5];
    private float rowRightEdge;
    private float lastSpan;

    // Angezeigte Balkenlaenge je Combatant, laeuft dem echten Wert weich hinterher.
    private readonly Dictionary<string, float> displayedFraction = [];

    // Dasselbe fuer die Zahlen. Der Schluessel enthaelt die Spalte, damit Total und
    // DPS derselben Zeile getrennt nachlaufen.
    private readonly Dictionary<string, double> displayedValue = [];

    public bool Visible = true;
    public DpsMode Mode = DpsMode.Rolling;
    public string HeaderTitle = string.Empty;
    public string HeaderDuration = "00:00";
    public double TotalDps;
    public int Deaths;
    public bool Frozen;
    public bool Connected;

    /// <summary>Name des eigenen Charakters, fuer die Hervorhebung der eigenen Zeile.</summary>
    public string? LocalPlayerName;

    /// <summary>Wird vom Zahnrad in der Kopfzeile ausgeloest.</summary>
    public Action? OpenConfigRequested;

    public void SetRows(IReadOnlyList<MeterRow> value) => this.rows = value;

    private uint Accent(float alpha = 1f)
    {
        var c = config.AccentColor;
        return ImGui.ColorConvertFloat4ToU32(new Vector4(c[0], c[1], c[2], c[3] * alpha));
    }

    private double ValueFor(MeterRow r, uint column) => column switch
    {
        ColumnTotal => r.TotalDamage,
        ColumnCrit => r.CritPercent,
        ColumnDirectHit => r.DirectHitPercent,
        _ => this.Mode == DpsMode.Rolling ? r.RollingDps : r.EncounterDps
    };

    public void Draw()
    {
        if (!this.Visible)
            return;

        var flags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;

        // Ohne Titelleiste wirkt es wie ein Overlay statt wie ein Werkzeugfenster.
        // Verschoben wird dann durch Ziehen an einer freien Stelle im Fenster.
        if (!config.ShowTitleBar)
            flags |= ImGuiWindowFlags.NoTitleBar;

        if (config.Locked)
        {
            // Gesperrt heisst: keine Titelleiste, unverrueckbar und fuer Klicks
            // durchlaessig, damit im Kampf das Spiel getroffen wird und nicht das
            // Fenster. Entsperren ueber die Einstellungen oder /endetailer lock.
            flags |= ImGuiWindowFlags.NoTitleBar
                | ImGuiWindowFlags.NoMove
                | ImGuiWindowFlags.NoResize
                | ImGuiWindowFlags.NoInputs
                | ImGuiWindowFlags.NoNav;
        }

        ImGui.SetNextWindowBgAlpha(config.BackgroundAlpha);
        ImGui.SetNextWindowSize(new Vector2(420, 220), ImGuiCond.FirstUseEver);

        var pad = config.Padding;
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(pad, pad * 0.6f));
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(pad * 0.5f, 1f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, config.BarRounding);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);

        if (!ImGui.Begin("EnDetailer", ref this.Visible, flags))
        {
            ImGui.End();
            ImGui.PopStyleVar(4);
            return;
        }

        DrawGlass();
        ImGui.SetWindowFontScale(config.FontScale);

        // Alle Zellen in derselben Farbe, damit keine Spalte heller wirkt als die
        // andere - ImGui hebt sonst die Sortierspalte hervor.
        ImGui.PushStyleColor(ImGuiCol.Text, 0xFFFFFFFF);

        DrawHeader();
        DrawTable();
        DrawFooter();

        ImGui.PopStyleColor();
        ImGui.SetWindowFontScale(1f);

        ImGui.End();
        ImGui.PopStyleVar(4);
    }

    /// <summary>
    /// Der Glaseindruck. Einen echten Weichzeichner kennt ImGui nicht - was den
    /// Eindruck traegt, sind ohnehin andere Dinge: Transparenz, ein leichter Verlauf
    /// von oben und vor allem die feine helle Kante, die Licht auf einer Scheibe
    /// andeutet.
    /// </summary>
    private void DrawGlass()
    {
        var pos = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var drawList = ImGui.GetWindowDrawList();
        var min = pos;
        var max = pos + size;

        if (config.GlassGradient)
        {
            drawList.AddRectFilledMultiColor(min, max, 0x14FFFFFF, 0x14FFFFFF, 0x00FFFFFF, 0x00FFFFFF);
        }

        if (!config.GlassEdge)
            return;

        drawList.AddRect(min, max, 0x28FFFFFF, 0f, ImDrawFlags.None, 1f);

        // Oberkante etwas heller: Licht faellt von oben ein.
        drawList.AddLine(min, min with { X = max.X }, 0x50FFFFFF, 1f);
    }

    private void DrawHeader()
    {
        ImGui.PushStyleColor(ImGuiCol.Text, Accent());
        ImGui.TextUnformatted(this.HeaderDuration);
        ImGui.PopStyleColor();

        if (!string.IsNullOrEmpty(this.HeaderTitle))
        {
            ImGui.SameLine(0, 6);
            ImGui.TextUnformatted(this.HeaderTitle);
        }

        if (this.Frozen)
        {
            ImGui.SameLine();
            ImGui.TextDisabled("(ended)");
        }

        // Im gesperrten Zustand ist das Dropdown ohnehin nicht bedienbar, dann
        // steht dort nur, welche Groesse gerade angezeigt wird.
        var label = this.Mode == DpsMode.Rolling ? "DPS (current)" : "DPS (encounter)";

        if (config.Locked)
        {
            ImGui.SameLine(ImGui.GetContentRegionAvail().X - 100);
            ImGui.TextDisabled(label);
            return;
        }

        // Zahnrad ganz rechts, in Dalamuds Symbolschrift. ImGui laesst keine eigenen
        // Knoepfe in der Titelleiste zu, deshalb sitzt es in der ersten Inhaltszeile.
        var gear = FontAwesomeIcon.Cog.ToIconString();
        float gearWidth;

        using (uiBuilder.IconFontHandle.Push())
            gearWidth = ImGui.CalcTextSize(gear).X + ImGui.GetStyle().FramePadding.X * 2;

        ImGui.SameLine(ImGui.GetContentRegionAvail().X - gearWidth);

        using (uiBuilder.IconFontHandle.Push())
        {
            if (ImGui.Button(gear + "##config", new Vector2(gearWidth, 0)))
                this.OpenConfigRequested?.Invoke();
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Settings");

        // Deutlicher Abstand zum Zahnrad, sonst wirken beide wie ein Element.
        const float modeWidth = 130f;
        ImGui.SameLine(ImGui.GetContentRegionAvail().X - modeWidth - gearWidth - 12f);
        ImGui.SetNextItemWidth(modeWidth);

        if (ImGui.BeginCombo("##dpsmode", label))
        {
            if (ImGui.Selectable("DPS (current)", this.Mode == DpsMode.Rolling))
                this.Mode = DpsMode.Rolling;
            if (ImGui.Selectable("DPS (encounter)", this.Mode == DpsMode.Encounter))
                this.Mode = DpsMode.Encounter;
            ImGui.EndCombo();
        }
    }

    /// <summary>Feine Linie unter der Kopfzeile, in der Akzentfarbe.</summary>
    private void DrawAccentSeparator()
    {
        var pos = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var drawList = ImGui.GetWindowDrawList();

        // Nach rechts auslaufend statt durchgezogen - wirkt leichter.
        drawList.AddRectFilledMultiColor(
            pos with { Y = pos.Y + 1 },
            new Vector2(pos.X + width, pos.Y + 2),
            Accent(0.9f), Accent(0.05f), Accent(0.05f), Accent(0.9f));

        ImGui.Dummy(new Vector2(0, 3));
    }

    private void DrawTable()
    {
        DrawAccentSeparator();

        // ScrollY reserviert dauerhaft Platz fuer die Bildlaufleiste am rechten Rand,
        // auch wenn gar nicht gescrollt wird. Deshalb nur bei vielen Zeilen setzen.
        var flags = ImGuiTableFlags.NoPadOuterX;
        if (this.rows.Count > 12)
            flags |= ImGuiTableFlags.ScrollY;

        var height = -ImGui.GetTextLineHeightWithSpacing() * 1.4f;

        if (!ImGui.BeginTable("##endetailer", 5, flags, new Vector2(0, height), 0))
            return;

        // Die Zahlenspalten bekommen genau so viel Platz, wie Ueberschrift plus
        // Sortierpfeil brauchen - dann steht der rechtsbuendige Wert fast unter
        // seiner Ueberschrift. Rechtsbuendige Ueberschriften waren der falsche Weg:
        // ImGui schneidet sie an der Spaltenkante ab.
        // Platz fuer die Ueberschrift samt Sortierzeichen plus etwas Luft.
        float Width(string header) => ImGui.CalcTextSize(header + " v").X + 8f;

        // Der Name nimmt den Rest, die Zahlenspalten liegen fest rechts. Andersherum
        // - eine dehnbare Zahlenspalte - loest sich vom Rest: sie schnappt sich den
        // ganzen Restplatz und bewegt sich beim Verkleinern nicht mit den anderen.
        ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch, 0, ColumnName);
        ImGui.TableSetupColumn("Total", ImGuiTableColumnFlags.WidthFixed, Width("Total"), ColumnTotal);
        ImGui.TableSetupColumn("CRIT", ImGuiTableColumnFlags.WidthFixed, Width("CRIT"), ColumnCrit);
        ImGui.TableSetupColumn("DH", ImGuiTableColumnFlags.WidthFixed, Width("DH") + 8, ColumnDirectHit);
        ImGui.TableSetupColumn("DPS", ImGuiTableColumnFlags.WidthFixed, Width("DPS") + 8, ColumnDps);
        ImGui.TableSetupScrollFreeze(0, 1);
        DrawHeaderRow();
        DrawRows();

        ImGui.EndTable();
    }

    private static readonly (uint Id, string Label)[] Columns =
    [
        (ColumnName, "Name"),
        (ColumnTotal, "Total"),
        (ColumnCrit, "CRIT"),
        (ColumnDirectHit, "DH"),
        (ColumnDps, "DPS")
    ];

    /// <summary>
    /// Eigene Kopfzeile statt TableHeadersRow. Deren Beschriftung sitzt immer links
    /// und haelt rechts Platz fuer den Sortierpfeil frei - Ueberschrift und Wert
    /// koennen dadurch nie buendig stehen. Selbst gezeichnet liegt beides rechts
    /// auf derselben Kante, und die Sortierung fuehren wir ohnehin selbst.
    /// </summary>
    private void DrawHeaderRow()
    {
        ImGui.TableNextRow(ImGuiTableRowFlags.Headers);

        foreach (var (id, label) in Columns)
        {
            ImGui.TableSetColumnIndex((int)id);

            var text = id == this.sortColumn
                ? label + (this.sortAscending ? " ^" : " v")
                : label;

            var available = ImGui.GetContentRegionAvail().X;

            // Die ganze Zellbreite als Klickflaeche, damit man nicht die Schrift
            // treffen muss.
            var origin = ImGui.GetCursorPos();
            if (ImGui.InvisibleButton($"##h{id}", new Vector2(Math.Max(1, available), ImGui.GetTextLineHeight())))
                ToggleSort(id);

            ImGui.SetCursorPos(origin);

            if (id != ColumnName)
            {
                var width = ImGui.CalcTextSize(text).X;
                if (available > width)
                    ImGui.SetCursorPosX(ImGui.GetCursorPosX() + available - width);
            }

            ImGui.PushStyleColor(ImGuiCol.Text, id == this.sortColumn ? Accent() : Accent(0.62f));
            ImGui.TextUnformatted(text);
            ImGui.PopStyleColor();
        }
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
        var ordered = this.sortColumn == ColumnName
            ? (this.sortAscending
                ? this.rows.OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ToList()
                : this.rows.OrderByDescending(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ToList())
            : (this.sortAscending
                ? this.rows.OrderBy(r => ValueFor(r, this.sortColumn)).ToList()
                : this.rows.OrderByDescending(r => ValueFor(r, this.sortColumn)).ToList());

        // Die Balkenlaenge richtet sich nach der Groesse, nach der gerade sortiert
        // wird - bei Namenssortierung nach der DPS-Spalte, sonst waere sie sinnlos.
        var barColumn = this.sortColumn == ColumnName ? ColumnDps : this.sortColumn;
        var peak = ordered.Count > 0 ? Math.Max(1, ordered.Max(r => ValueFor(r, barColumn))) : 1;

        for (var index = 0; index < ordered.Count; index++)
        {
            var row = ordered[index];

            ImGui.TableNextRow();
            ImGui.TableNextColumn();

            this.columnStartX[0] = ImGui.GetCursorScreenPos().X;

            var barEndX = BarEndX(row, barColumn, peak);
            var barColor = JobColors.Bar(row.Job, config.BarAlpha);
            var isSelf = this.LocalPlayerName is { } me && row.Name == me;

            DrawRowBackground(index, isSelf);
            DrawBarSegment(barEndX, barColor, row.Job, first: true);
            DrawJobIcon(row.Job);
            Text(row.Name);

            ImGui.TableNextColumn();
            this.columnStartX[1] = ImGui.GetCursorScreenPos().X;
            DrawBarSegment(barEndX, barColor, row.Job);
            Centered(Format(SmoothValue(row.Name + "|total", row.TotalDamage)));

            ImGui.TableNextColumn();
            this.columnStartX[2] = ImGui.GetCursorScreenPos().X;
            DrawBarSegment(barEndX, barColor, row.Job);
            Centered($"{row.CritPercent:0}%");

            ImGui.TableNextColumn();
            this.columnStartX[3] = ImGui.GetCursorScreenPos().X;
            DrawBarSegment(barEndX, barColor, row.Job);
            Centered($"{row.DirectHitPercent:0}%");

            ImGui.TableNextColumn();
            this.columnStartX[4] = ImGui.GetCursorScreenPos().X;
            this.rowRightEdge = this.columnStartX[4] + ImGui.GetContentRegionAvail().X;
            DrawBarSegment(barEndX, barColor, row.Job, last: true);
            var dps = this.Mode == DpsMode.Rolling ? row.RollingDps : row.EncounterDps;
            Centered(Format(SmoothValue(row.Name + "|dps", dps)));
        }
    }

    /// <summary>
    /// Rechte Kante des Balkens in Bildschirmkoordinaten.
    /// </summary>
    private float BarEndX(MeterRow row, uint barColumn, double peak)
    {
        var target = (float)Math.Clamp(ValueFor(row, barColumn) / peak, 0, 1);
        var fraction = Smooth(row.Name, target);
        if (fraction <= 0)
            return float.MinValue;

        var padding = ImGui.GetStyle().CellPadding.X;
        var span = BarSpan(padding);
        this.lastSpan = span;

        return ImGui.GetCursorScreenPos().X - padding + span * fraction;
    }

    /// <summary>
    /// Laesst die Balkenlaenge weich dem Zielwert folgen. Rein optisch: Die Zahlen
    /// daneben bleiben exakt, nur der Balken zieht traeger nach. Ohne das springt
    /// er bei jedem Burst und jeder Downtime hart, weil die zugrunde liegenden
    /// Werte nun einmal genau das tun.
    /// </summary>
    private float Smooth(string name, float target)
    {
        if (config.BarSmoothing <= 0.001f)
        {
            this.displayedFraction[name] = target;
            return target;
        }

        if (!this.displayedFraction.TryGetValue(name, out var current))
        {
            this.displayedFraction[name] = target;
            return target;
        }

        // Exponentielle Annaeherung, unabhaengig von der Bildrate.
        var dt = ImGui.GetIO().DeltaTime;
        var factor = 1f - MathF.Exp(-dt / config.BarSmoothing);
        var next = current + (target - current) * factor;

        this.displayedFraction[name] = next;
        return next;
    }

    /// <summary>
    /// Laesst einen angezeigten Zahlenwert weich zum Ziel laufen. Der Wert selbst
    /// wird nicht veraendert - die Anzeige holt ihn nur mit kurzem Nachlauf ein,
    /// statt bei jedem Burst umzuklappen. Es wird nichts geschaetzt.
    /// </summary>
    private double SmoothValue(string key, double target)
    {
        if (config.ValueSmoothing <= 0.001f)
            return target;

        if (!this.displayedValue.TryGetValue(key, out var current))
        {
            this.displayedValue[key] = target;
            return target;
        }

        var dt = ImGui.GetIO().DeltaTime;
        var factor = 1f - MathF.Exp(-dt / config.ValueSmoothing);
        var next = current + (target - current) * factor;

        // Nahe genug am Ziel einrasten, damit die letzte Stelle nicht ewig zappelt.
        if (Math.Abs(target - next) < 1)
            next = target;

        this.displayedValue[key] = next;
        return next;
    }

    /// <summary>Verwirft die Nachlaufwerte, etwa zu Beginn eines neuen Kampfes.</summary>
    public void ResetSmoothing()
    {
        this.displayedValue.Clear();
        this.displayedFraction.Clear();
    }

    /// <summary>
    /// Wie breit der Balken bei voller Auslastung waere. Die Spaltenbreiten stehen
    /// erst zur Laufzeit fest, deshalb hier und nicht als Konstante.
    /// </summary>
    private float BarSpan(float cellPadding)
    {
        var left = this.columnStartX[0];
        var right = config.BarExtent switch
        {
            BarExtent.NameOnly => this.columnStartX[1],
            BarExtent.FullRow => this.rowRightEdge,
            _ => this.columnStartX[2]
        };

        // Im allerersten Frame ist noch nichts vermessen.
        return right <= left ? 0 : right - left + cellPadding;
    }

    /// <summary>
    /// Zeichnet den Teil des Balkens, der in die aktuelle Zelle faellt. Ein einziges
    /// breites Rechteck aus der ersten Spalte heraus landet in deren Zeichenebene
    /// und liegt damit ueber den Zahlen der anderen Spalten - bei hoher Deckkraft
    /// verschwinden sie darunter. Segmentweise gezeichnet gehoert jedes Stueck zu
    /// seiner Zelle und liegt unter deren Text.
    /// </summary>
    private void DrawBarSegment(float barEndX, uint color, Job job, bool first = false, bool last = false)
    {
        var pos = ImGui.GetCursorScreenPos();
        var padding = ImGui.GetStyle().CellPadding.X;
        var left = pos.X - padding;
        var cellRight = pos.X + ImGui.GetContentRegionAvail().X + padding;
        var right = Math.Min(cellRight, barEndX);

        if (right <= left)
            return;

        var top = pos.Y - 1;
        var height = ImGui.GetTextLineHeight() + 3;
        var drawList = ImGui.GetWindowDrawList();

        var topLeft = new Vector2(left, top);
        var bottomRight = new Vector2(right, top + height);

        // Nur die tatsaechlichen Aussenkanten runden, sonst bekommt jedes Segment
        // eigene Ecken und der Balken zerfaellt optisch in Kacheln.
        var endsHere = right < cellRight - 0.5f || last;
        var corners = ImDrawFlags.RoundCornersNone;
        if (first)
            corners |= ImDrawFlags.RoundCornersLeft;
        if (endsHere)
            corners |= ImDrawFlags.RoundCornersRight;

        drawList.AddRectFilled(topLeft, bottomRight, color, config.BarRounding, corners);

        if (config.BarGloss)
        {
            // Heller Verlauf ueber der oberen Haelfte. ImGui kann keinen Weichzeichner,
            // aber ein abnehmendes Weiss erzeugt denselben Eindruck von Woelbung.
            var glossBottom = new Vector2(right, top + height * 0.55f);
            drawList.AddRectFilledMultiColor(topLeft, glossBottom, 0x30FFFFFF, 0x30FFFFFF, 0x00FFFFFF, 0x00FFFFFF);
        }

        if (config.BarGlow && endsHere)
        {
            // Aufgehellte Kante am Balkenende - das Naechste an einem Leuchten,
            // was ohne Shader moeglich ist.
            var glowLeft = new Vector2(Math.Max(left, right - 6), top);
            drawList.AddRectFilledMultiColor(
                glowLeft, bottomRight, 0x00FFFFFF, JobColors.Glow(job), JobColors.Glow(job), 0x00FFFFFF);
        }
    }

    /// <summary>
    /// Zeilenhintergrund: dezent abwechselnd und mit Akzentstreifen vor der eigenen
    /// Zeile, damit man sich in einer vollen Gruppe sofort findet.
    /// </summary>
    private void DrawRowBackground(int index, bool isSelf)
    {
        if (config.RowStripes && index % 2 == 1)
            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, 0x12FFFFFF);

        if (!config.HighlightSelf || !isSelf)
            return;

        ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, 0x1AFFFFFF);

        var pos = ImGui.GetCursorScreenPos();
        var padding = ImGui.GetStyle().CellPadding.X;
        var top = pos.Y - 1;
        var height = ImGui.GetTextLineHeight() + 3;

        ImGui.GetWindowDrawList().AddRectFilled(
            new Vector2(pos.X - padding, top),
            new Vector2(pos.X - padding + 2.5f, top + height),
            Accent());
    }

    private void DrawJobIcon(Job job)
    {
        if (!config.ShowJobIcons)
            return;

        var iconId = Jobs.IconId(job);
        var size = ImGui.GetTextLineHeight();

        if (iconId == 0)
        {
            ImGui.Dummy(new Vector2(size, size));
            ImGui.SameLine(0, 4);
            return;
        }

        var texture = textures.GetFromGameIcon(new GameIconLookup(iconId)).GetWrapOrEmpty();
        ImGui.Image(texture.Handle, new Vector2(size, size));
        ImGui.SameLine(0, 4);
    }

    /// <summary>
    /// Rechtsbuendig in der Zelle, auf derselben Kante wie die Ueberschrift.
    /// GetColumnWidth taugt zum Ausmessen nicht - es stammt aus der alten
    /// Spalten-API und liefert in Tabellen falsche Breiten.
    /// </summary>
    private void Centered(string text)
    {
        var available = ImGui.GetContentRegionAvail().X;
        var width = ImGui.CalcTextSize(text).X;
        if (available > width)
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + available - width);

        Text(text);
    }

    /// <summary>
    /// Schrift vom Hintergrund abgesetzt. ImGui kennt weder Schatten noch Kontur,
    /// beides wird durch vorgezeichnete dunkle Kopien erzeugt. Die Kontur ringsum
    /// wirkt bei kleiner Schrift schnell matschig, deshalb ist der einfache
    /// Schatten die Voreinstellung.
    /// </summary>
    private void Text(string text)
    {
        if (config.TextStyle == TextStyle.Plain)
        {
            ImGui.TextUnformatted(text);
            return;
        }

        var pos = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();
        const uint shadow = 0xD0000000;

        if (config.TextStyle == TextStyle.Shadow)
        {
            drawList.AddText(pos + new Vector2(1, 1), shadow, text);
        }
        else
        {
            drawList.AddText(pos with { X = pos.X - 1 }, shadow, text);
            drawList.AddText(pos with { X = pos.X + 1 }, shadow, text);
            drawList.AddText(pos with { Y = pos.Y - 1 }, shadow, text);
            drawList.AddText(pos with { Y = pos.Y + 1 }, shadow, text);
        }

        ImGui.TextUnformatted(text);
    }

    private void DrawFooter()
    {
        if (config.ShowDiagnostics)
        {
            ImGui.TextUnformatted(
                $"{config.BarExtent} | x0={this.columnStartX[0]:0} x1={this.columnStartX[1]:0} " +
                $"x2={this.columnStartX[2]:0} rand={this.rowRightEdge:0} | span={this.lastSpan:0}");
            return;
        }

        if (!this.Connected)
        {
            ImGui.TextDisabled("Waiting for IINACT...");
            return;
        }

        var label = this.Mode == DpsMode.Rolling ? "current" : "total";
        var total = SmoothValue("##total", this.TotalDps);

        ImGui.PushStyleColor(ImGuiCol.Text, Accent());
        ImGui.TextUnformatted(Format(total));
        ImGui.PopStyleColor();

        ImGui.SameLine(0, 5);
        ImGui.TextUnformatted($"DPS {label}");
        ImGui.SameLine(0, 12);
        ImGui.TextDisabled($"Deaths: {this.Deaths}");
    }

    /// <summary>
    /// Zahlenformat der Anzeige.
    ///
    /// Ab zehntausend entfaellt die Nachkommastelle: Bei 10.000 DPS entspricht sie
    /// hundert Punkten, also einem Prozent - sie flackert dauernd, ohne etwas
    /// auszusagen. Das Auge liest daraus Unruhe, wo real kaum Bewegung ist. Grober
    /// dargestellt steht die Zahl ruhig, obwohl darunter unveraendert genau
    /// gerechnet wird.
    ///
    /// Bewusst kulturunabhaengig - der deutsche Tausenderpunkt liest sich in
    /// diesem Zusammenhang wie ein Dezimalkomma und stiftet Verwirrung.
    /// </summary>
    private static string Format(double value) => value switch
    {
        >= 1_000_000 => (value / 1_000_000).ToString("0.00", CultureInfo.InvariantCulture) + "M",
        >= 10_000 => (value / 1_000).ToString("0", CultureInfo.InvariantCulture) + "K",
        >= 1_000 => (value / 1_000).ToString("0.0", CultureInfo.InvariantCulture) + "K",
        _ => value.ToString("0", CultureInfo.InvariantCulture)
    };
}
