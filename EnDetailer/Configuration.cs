using Dalamud.Configuration;
using Dalamud.Plugin;
using EnDetailer.Core;

namespace EnDetailer;

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
    public int Version { get; set; } = 2;

    /// <summary>Mittlere Verzoegerung der Echtzeit-Rate in Sekunden.</summary>
    public int ReactionSeconds { get; set; } = ReactionTime.DefaultSeconds;

    /// <summary>Nur noch zum Einlesen alter Konfigurationen (Version 1); danach ungenutzt.</summary>
    public int RollingWindowSeconds { get; set; } = 25;

    /// <summary>Wie lange ausserhalb des Kampfes gewartet wird, bevor er als beendet gilt.</summary>
    public int GraceSeconds { get; set; } = 10;

    public bool ResetOnZoneChange { get; set; }

    /// <summary>Schliesst IINACTs Encounter mit, wenn unserer endet.</summary>
    public bool EndIinactEncounter { get; set; } = true;

    /// <summary>Gesperrt: nur Anzeige, unverrueckbar, klick-durchlaessig.</summary>
    public bool Locked { get; set; }

    /// <summary>Wann das Meter im Bild steht.</summary>
    public ShowMode ShowMode { get; set; } = ShowMode.Always;

    /// <summary>Nachlauf nach Kampfende im Modus InCombat.</summary>
    public int ShowAfterCombatSeconds { get; set; } = 15;

    public bool HideInCutscenes { get; set; }

    /// <summary>Deckkraft des Fensterhintergrunds.</summary>
    public float BackgroundAlpha { get; set; } = 0.55f;

    /// <summary>Deckkraft der jobfarbenen Balken.</summary>
    public byte BarAlpha { get; set; } = 190;

    public bool ShowJobIcons { get; set; } = true;

    /// <summary>Wie die Schrift vom Hintergrund abgesetzt wird.</summary>
    public TextStyle TextStyle { get; set; } = TextStyle.Shadow;

    /// <summary>Schriftgroesse im Meter. Balkenhoehe und Symbole skalieren mit.</summary>
    public float FontScale { get; set; } = 1.0f;

    /// <summary>Akzentstreifen vor der eigenen Zeile.</summary>
    public bool HighlightSelf { get; set; } = true;

    /// <summary>Abwechselnd leicht abgesetzte Zeilen.</summary>
    public bool RowStripes { get; set; } = true;

    /// <summary>Schmaler Verlauf des Echtzeitwerts der Gruppe unter dem Kopfband.</summary>
    public bool ShowGraph { get; set; } = true;

    /// <summary>Blendet gemessene Werte in der Fusszeile ein. Nur zur Fehlersuche.</summary>
    public bool ShowDiagnostics { get; set; }

    /// <summary>Wie weit der jobfarbene Balken nach rechts reicht.</summary>
    public BarExtent BarExtent { get; set; } = BarExtent.ThroughTotal;

    /// <summary>Anzeigemodus der DPS-Spalte: gleitend (Now) oder ueber den ganzen Kampf (Fight).</summary>
    public DpsMode Mode { get; set; } = DpsMode.Rolling;

    private IDalamudPluginInterface? pluginInterface;

    public void Initialize(IDalamudPluginInterface pi)
    {
        this.pluginInterface = pi;

        if (this.Version < 2)
        {
            this.ReactionSeconds = ReactionTime.FromWindowSeconds(this.RollingWindowSeconds);
            this.Version = 2;
            Save();
        }
    }

    public void Save() => this.pluginInterface?.SavePluginConfig(this);
}
