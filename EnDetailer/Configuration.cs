using Dalamud.Configuration;
using Dalamud.Plugin;

namespace EnDetailer;

public enum DpsMethod
{
    /// <summary>Harter Schnitt: die letzten N Sekunden zaehlen gleich viel.</summary>
    FlatWindow,

    /// <summary>Weiches Ausklingen: juengerer Schaden zaehlt mehr.</summary>
    Weighted
}

public enum TextStyle
{
    /// <summary>Nichts. Am saubersten auf dunklem Grund.</summary>
    Plain,

    /// <summary>Ein versetzter dunkler Schatten. Bei kleiner Schrift die beste Wahl.</summary>
    Shadow,

    /// <summary>Kontur ringsum. Kraeftig, macht kleine Schrift aber matschig.</summary>
    Outline
}

public enum BarExtent
{
    /// <summary>Nur hinter dem Namen.</summary>
    NameOnly,

    /// <summary>Bis zum Ende der Total-Spalte, wie in LMeter.</summary>
    ThroughTotal,

    /// <summary>Ueber die ganze Zeile, unter allen Zahlen hindurch.</summary>
    FullRow
}

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    /// <summary>Wie der aktuelle DPS gewichtet wird.</summary>
    public DpsMethod DpsMethod { get; set; } = DpsMethod.Weighted;

    /// <summary>Laenge des gleitenden Fensters fuer den aktuellen DPS.</summary>
    // 25 Sekunden sind rund zehn Aktionen: eine grosse Faehigkeit hebt den Wert
    // spuerbar, verdoppelt ihn aber nicht mehr. Im Spiel erprobt.
    public int RollingWindowSeconds { get; set; } = 25;

    /// <summary>Wie lange ausserhalb des Kampfes gewartet wird, bevor er als beendet gilt.</summary>
    public int GraceSeconds { get; set; } = 10;

    public bool ResetOnZoneChange { get; set; }

    /// <summary>Schliesst IINACTs Encounter mit, wenn unserer endet.</summary>
    public bool EndIinactEncounter { get; set; } = true;

    /// <summary>Gesperrt: ohne Titelleiste, unverrueckbar, klick-durchlaessig.</summary>
    public bool Locked { get; set; }

    /// <summary>Deckkraft des Fensterhintergrunds.</summary>
    public float BackgroundAlpha { get; set; } = 0.55f;

    /// <summary>Deckkraft der jobfarbenen Balken.</summary>
    public byte BarAlpha { get; set; } = 190;

    public bool ShowJobIcons { get; set; } = true;

    /// <summary>Wie die Schrift vom Hintergrund abgesetzt wird.</summary>
    public TextStyle TextStyle { get; set; } = TextStyle.Shadow;

    /// <summary>Schriftgroesse im Meter. Balkenhoehe und Symbole skalieren mit.</summary>
    public float FontScale { get; set; } = 1.0f;

    /// <summary>
    /// Traegheit der angezeigten Zahlen in Sekunden. Die Zahl laeuft zum
    /// berechneten Wert, statt zu springen - sie erreicht ihn immer, nur eben
    /// einen Augenblick spaeter. Nichts wird geschaetzt oder vorweggenommen.
    /// </summary>
    public float ValueSmoothing { get; set; } = 2.0f;

    /// <summary>
    /// Traegheit der Balken in Sekunden. Betrifft ausschliesslich die Darstellung -
    /// die angezeigten Zahlen bleiben unberuehrt.
    /// </summary>
    public float BarSmoothing { get; set; } = 0.15f;

    /// <summary>
    /// Zeigt ImGuis eigene Titelleiste. Aus wirkt das Fenster wie ein Overlay statt
    /// wie ein Werkzeugfenster; verschoben wird dann durch Ziehen im Fenster selbst.
    /// </summary>
    public bool ShowTitleBar { get; set; }

    /// <summary>Eckenrundung der Balken in Pixeln. 0 wirkt technischer und klarer.</summary>
    public float BarRounding { get; set; }

    /// <summary>
    /// Akzentfarbe fuer Ueberschriften, Trennlinien und Summen. Als RGBA gespeichert,
    /// damit sie sich im Farbwaehler bearbeiten laesst.
    /// </summary>
    public float[] AccentColor { get; set; } = [0.35f, 0.72f, 0.95f, 1f];

    /// <summary>Feine helle Kante am Fensterrand - macht den Glaseindruck aus.</summary>
    public bool GlassEdge { get; set; } = true;

    /// <summary>Senkrechter Verlauf im Fensterhintergrund, oben etwas heller.</summary>
    public bool GlassGradient { get; set; } = true;

    /// <summary>Heller Verlauf ueber der oberen Balkenhaelfte.</summary>
    public bool BarGloss { get; set; }

    /// <summary>Aufgehellte Kante am Balkenende.</summary>
    public bool BarGlow { get; set; }

    /// <summary>Akzentstreifen vor der eigenen Zeile.</summary>
    public bool HighlightSelf { get; set; } = true;

    /// <summary>Abwechselnd leicht abgesetzte Zeilen.</summary>
    public bool RowStripes { get; set; } = true;

    /// <summary>Blendet gemessene Werte in der Fusszeile ein. Nur zur Fehlersuche.</summary>
    public bool ShowDiagnostics { get; set; }

    /// <summary>Innenabstand des Fensters und der Zellen.</summary>
    public float Padding { get; set; } = 8f;

    /// <summary>Wie weit der jobfarbene Balken nach rechts reicht.</summary>
    public BarExtent BarExtent { get; set; } = BarExtent.ThroughTotal;

    private IDalamudPluginInterface? pluginInterface;

    public void Initialize(IDalamudPluginInterface pi) => this.pluginInterface = pi;

    public void Save() => this.pluginInterface?.SavePluginConfig(this);
}
