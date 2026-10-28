using System.Collections.Generic;

namespace EnDetailer.Core;

/// <summary>
/// Die letzten beendeten Kaempfe, neuester vorn, samt der Auswahl, welcher gezeigt wird.
/// "Live" ist der laufende bzw. gerade eingefrorene Kampf und kein Eintrag. Nach Kampfende
/// ist der gerade beendete Kampf zugleich Live und Eintrag 0; er wird beim Blaettern von
/// Live aus uebersprungen, damit er nicht doppelt erscheint.
///
/// Aenderungen kommen vom IPC- und vom Framework-Thread, gelesen wird im Draw-Thread;
/// deshalb ist jeder Zugriff gesperrt. <see cref="Version"/> zaehlt jede sichtbare
/// Aenderung, damit Leser abgeleitete Texte nur bei Aenderung neu bauen.
/// </summary>
public sealed class FightHistory
{
    public const int Capacity = 10;
    public const int LiveIndex = -1;

    private readonly object gate = new();
    private readonly List<FightRecord> entries = [];
    private int selected = LiveIndex;
    private bool liveIsNewest;
    private int version;

    public int Version
    {
        get { lock (this.gate) return this.version; }
    }

    public int Count
    {
        get { lock (this.gate) return this.entries.Count; }
    }

    /// <summary>Index des gezeigten Eintrags; <see cref="LiveIndex"/> heisst Live.</summary>
    public int SelectedIndex
    {
        get { lock (this.gate) return this.selected; }
    }

    /// <summary>Der gezeigte Eintrag, bei Live null.</summary>
    public FightRecord? Selected
    {
        get { lock (this.gate) return this.selected == LiveIndex ? null : this.entries[this.selected]; }
    }

    /// <summary>Erster Index, den man ansteuern kann: 1, solange Live den neuesten Eintrag schon zeigt.</summary>
    public int FirstBrowsable
    {
        get { lock (this.gate) return FirstUnsafe; }
    }

    public bool CanGoOlder
    {
        get { lock (this.gate) return OlderTarget < this.entries.Count; }
    }

    /// <summary>Wahr, solange ein Eintrag gezeigt wird; von dort geht es immer weiter Richtung Live.</summary>
    public bool CanGoNewer
    {
        get { lock (this.gate) return this.selected != LiveIndex; }
    }

    public FightRecord this[int index]
    {
        get { lock (this.gate) return this.entries[index]; }
    }

    private int FirstUnsafe => this.liveIsNewest ? 1 : 0;

    private int OlderTarget => this.selected == LiveIndex ? FirstUnsafe : this.selected + 1;

    /// <summary>
    /// Nimmt den soeben beendeten Kampf auf. Ein gerade gezeigter Eintrag bleibt gezeigt, auch
    /// wenn er dabei um eine Stelle rutscht; faellt er heraus, geht die Ansicht auf Live.
    /// </summary>
    public void Add(FightRecord record)
    {
        lock (this.gate)
        {
            this.entries.Insert(0, record);
            if (this.selected != LiveIndex)
                this.selected++;

            if (this.entries.Count > Capacity)
            {
                this.entries.RemoveAt(this.entries.Count - 1);
                if (this.selected >= this.entries.Count)
                    this.selected = LiveIndex;
            }

            this.liveIsNewest = true;
            this.version++;
        }
    }

    /// <summary>Ein neuer Kampf beginnt oder die Anzeige wurde zurueckgesetzt: Ansicht springt auf Live.</summary>
    public void LiveStarted()
    {
        lock (this.gate)
        {
            if (this.selected == LiveIndex && !this.liveIsNewest)
                return;

            this.selected = LiveIndex;
            this.liveIsNewest = false;
            this.version++;
        }
    }

    public void SelectLive() => Select(LiveIndex);

    /// <summary>Waehlt einen Eintrag oder Live; ein unpassender Index wird ignoriert.</summary>
    public void Select(int index)
    {
        lock (this.gate)
        {
            if (index == this.selected)
                return;

            if (index != LiveIndex && (index < FirstUnsafe || index >= this.entries.Count))
                return;

            this.selected = index;
            this.version++;
        }
    }

    public void GoOlder()
    {
        lock (this.gate)
        {
            var target = OlderTarget;
            if (target >= this.entries.Count)
                return;

            this.selected = target;
            this.version++;
        }
    }

    public void GoNewer()
    {
        lock (this.gate)
        {
            if (this.selected == LiveIndex)
                return;

            var target = this.selected - 1;
            this.selected = target < FirstUnsafe ? LiveIndex : target;
            this.version++;
        }
    }
}
