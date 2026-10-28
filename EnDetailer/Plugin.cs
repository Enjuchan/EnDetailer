using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using EnDetailer.Core;
using EnDetailer.Ui;

namespace EnDetailer;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/endetailer";

    private readonly ICommandManager commandManager;
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly IPluginLog log;
    private readonly IFramework framework;
    private readonly IPlayerState playerState;
    private readonly IChatGui chatGui;
    private readonly ICondition condition;

    private readonly IinactSource source;
    private readonly GameConditions conditions;
    private readonly EncounterTracker encounters;
    private readonly RollingDpsTracker rollingDamage = new();
    private readonly RollingDpsTracker rollingHealing = new();
    private readonly RollingDpsTracker rollingTaken = new();
    private readonly MeterWindow window;

    private readonly Configuration config;
    private readonly ConfigWindow configWindow;
    private readonly WindowSystem windows = new("EnDetailer");

    private readonly MeterVisibility visibility;
    private readonly FrameTiming drawTiming;
    private readonly FrameTiming updateTiming;
    private readonly Stopwatch stopwatch = new();

    private PreviewCombat? preview;
    private DateTime lastPreviewSnapshot = DateTime.MinValue;
    private DateTime lastDiagnostics = DateTime.MinValue;

    // UpdateRows laeuft jeden Frame und vom IPC-Thread aus; die Zeilenliste wird deshalb
    // wiederverwendet und der Zugriff gesperrt. Das Fenster kopiert sie beim Uebernehmen.
    private readonly object rowsGate = new();
    private readonly List<MeterRow> rowBuffer = [];
    private int lastDurationSecond = -1;

    private CombatSnapshot? lastSnapshot;
    private MeterMetric renderedMetric = MeterMetric.Damage;
    private DpsMode renderedMode = DpsMode.Rolling;

    // Verlauf: gezeigt wird entweder Live (shownRecord null) oder ein Eintrag, dessen Zeilen
    // einmal ins Fenster uebernommen werden. Waehrenddessen rechnet UpdateRows nicht.
    private readonly FightHistory history = new();

    // Verlauf des laufenden Kampfes; das Fenster zeigt ihn in der Live-Ansicht.
    private readonly FightGraph liveGraph = new();
    private FightRecord? shownRecord;
    private bool liveStale;
    private int shownHistoryVersion = -1;
    private DateTime lastConnectAttempt = DateTime.MinValue;
    private static readonly TimeSpan ConnectRetryDelay = TimeSpan.FromSeconds(5);

    public Plugin(
        IDalamudPluginInterface pluginInterface,
        ICommandManager commandManager,
        IPluginLog log,
        IFramework framework,
        ICondition condition,
        IClientState clientState,
        IPlayerState playerState,
        IChatGui chatGui,
        ITextureProvider textures)
    {
        this.pluginInterface = pluginInterface;
        this.commandManager = commandManager;
        this.log = log;
        this.framework = framework;
        this.playerState = playerState;
        this.chatGui = chatGui;
        this.condition = condition;

        // ECommons braucht das für DalamudReflector, das der Dependencies-Tab zum Installieren nutzt.
        ECommons.ECommonsMain.Init(pluginInterface, this);

        this.config = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Loc.Init(pluginInterface, this.config.Language);
        this.config.Initialize(pluginInterface);
        Widgets.Init(textures);

        this.conditions = new GameConditions(condition, clientState);
        this.visibility = new MeterVisibility(this.conditions);
        this.drawTiming = new FrameTiming(log, "Meter draw", TimeSpan.FromSeconds(5), () => this.config.ShowDiagnostics);
        this.updateTiming = new FrameTiming(log, "Row update", TimeSpan.FromSeconds(5), () => this.config.ShowDiagnostics);
        this.encounters = new EncounterTracker(this.conditions)
        {
            GracePeriod = TimeSpan.FromSeconds(this.config.GraceSeconds),
            ResetOnZoneChange = this.config.ResetOnZoneChange
        };

        var version = pluginInterface.Manifest.AssemblyVersion.ToString(3);
        this.configWindow = new ConfigWindow(
            this.config,
            this.encounters,
            new Dependencies(pluginInterface, log, EnDetailerDependencies.Groups),
            EnDetailerAbout.Create(version),
            log,
            () => this.source?.IsConnected ?? false);
        this.windows.AddWindow(this.configWindow);
        this.window = new MeterWindow(this.config, textures);
        this.window.OpenConfigRequested = OpenConfig;
        this.window.OlderRequested = this.history.GoOlder;
        this.window.NewerRequested = this.history.GoNewer;
        this.window.SelectRequested = this.history.Select;
        this.encounters.EncounterStarted += () =>
        {
            this.history.LiveStarted();
            this.liveGraph.Reset();
            var at = this.encounters.StartedAt ?? DateTime.UtcNow;
            this.rollingDamage.MarkEncounterStart(at);
            this.rollingHealing.MarkEncounterStart(at);
            this.rollingTaken.MarkEncounterStart(at);
        };
        this.encounters.EncounterEnded += CaptureFight;
        this.encounters.EncounterEnded += OnEncounterEnded;
        this.encounters.EncounterEnded += RebuildFrozenRows;

        this.source = new IinactSource(pluginInterface, log);
        this.source.SnapshotReceived += OnIinactSnapshot;

        // Kein Verbindungsversuch im Konstruktor: Beim Spielstart laden alle
        // Plugins gleichzeitig, und IINACT ist dann meist noch nicht bereit.
        // Der Framework-Puls versucht es, bis es klappt.

        // Dalamud übernimmt den Hilfetext bei der Anmeldung, deshalb wird der Befehl beim Sprachwechsel neu angemeldet.
        RegisterCommand();
        Loc.Changed += RegisterCommand;
        Loc.Changed += OnLanguageChanged;

        this.framework.Update += OnFrameworkUpdate;
        this.pluginInterface.UiBuilder.Draw += Draw;
        this.pluginInterface.UiBuilder.OpenConfigUi += OpenConfig;
        this.pluginInterface.UiBuilder.OpenMainUi += OpenMain;
        this.log.Information("EnDetailer loaded.");
    }

    private void RegisterCommand()
    {
        this.commandManager.RemoveHandler(CommandName);
        this.commandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = $"{EnDetailerAbout.ToggleEffect} /endetailer config: {EnDetailerAbout.ConfigEffect} " +
                          $"/endetailer lock: {EnDetailerAbout.LockEffect} /endetailer preview: {EnDetailerAbout.PreviewEffect}"
        });
    }

    /// <summary>Texte, die nur bei Änderung gebaut werden, müssen in der neuen Sprache neu entstehen.</summary>
    private void OnLanguageChanged()
    {
        this.shownHistoryVersion = -1;
        this.shownRecord = null;
    }

    /// <summary>
    /// In Ladebildschirmen wird nichts gezeichnet. Eine Pruefung auf den Login fehlt mit Absicht:
    /// Das Einstellungsfenster soll vom Titelbildschirm aus ueber den Plugin-Installer erreichbar sein.
    /// Das Meter prueft den Login selbst.
    /// </summary>
    private void Draw()
    {
        if (this.condition[ConditionFlag.BetweenAreas] || this.condition[ConditionFlag.BetweenAreas51])
            return;

        DrawMeter();
        this.windows.Draw();
    }

    private void DrawMeter()
    {
        if (!this.conditions.CanShowOverlay)
            return;

        this.visibility.Mode = this.config.ShowMode;
        this.visibility.AfterCombat = TimeSpan.FromSeconds(this.config.ShowAfterCombatSeconds);
        this.visibility.HideInCutscenes = this.config.HideInCutscenes;
        if (!this.visibility.IsVisible(DateTime.UtcNow))
            return;

        this.stopwatch.Restart();
        this.window.Draw();
        this.drawTiming.Add(this.stopwatch.Elapsed);

        // Der Text braucht Auswertung und Zeichenketten; zweimal je Sekunde genuegt und
        // verfaelscht die Messung nicht.
        if (this.config.ShowDiagnostics && DateTime.UtcNow - this.lastDiagnostics >= TimeSpan.FromMilliseconds(500))
        {
            this.lastDiagnostics = DateTime.UtcNow;
            this.window.DiagnosticsText = $"{this.drawTiming.Summary()} | {this.updateTiming.Summary()}";
        }
    }

    private void OnCommand(string command, string args)
    {
        var arg = args.Trim();

        if (arg.Equals("config", StringComparison.OrdinalIgnoreCase))
        {
            OpenConfig();
            return;
        }

        if (arg.Equals("preview", StringComparison.OrdinalIgnoreCase))
        {
            this.configWindow.Preview = !this.configWindow.Preview;
            return;
        }

        if (arg.Equals("lock", StringComparison.OrdinalIgnoreCase))
        {
            this.config.Locked = !this.config.Locked;
            this.config.Save();
            return;
        }

        this.window.Visible = !this.window.Visible;
    }

    private void OpenConfig() => this.configWindow.IsOpen = true;

    /// <summary>
    /// Wird vom Plugin-Installer ausgeloest. Dalamud erwartet diesen Rueckruf fuer
    /// das Hauptfenster und bemaengelt sein Fehlen bei der Pruefung.
    /// </summary>
    private void OpenMain() => this.window.Visible = true;

    /// <summary>
    /// Schliesst IINACTs Encounter mit ab, sobald wir den Kampf fuer beendet
    /// halten. Sonst laeuft dessen Encounter endlos weiter, und Werte wie Crit
    /// und Direct Hit bezoegen sich auf einen ganz anderen Zeitraum als unsere.
    /// Feuert je Kampf genau einmal, weil der Uebergang nach Ended nur einmal
    /// stattfindet.
    /// </summary>
    private void OnEncounterEnded()
    {
        if (!this.config.EndIinactEncounter)
            return;

        this.source.EndEncounter(this.chatGui);
        this.log.Information("Encounter ended, IINACT closed as well.");
    }

    /// <summary>
    /// Der Puls aus dem Spiel. Unverzichtbar: IINACT schweigt ausserhalb des
    /// Kampfes, und genau dann muss die Karenzzeit ablaufen koennen.
    /// </summary>
    private void OnFrameworkUpdate(IFramework _)
    {
        var now = DateTime.UtcNow;

        // Solange keine Verbindung steht, in Ruhe weiter versuchen. Ohne das
        // bleibt das Plugin nach einem Spielstart dauerhaft stumm, weil IINACT
        // beim ersten Versuch noch nicht antwortet.
        if (!this.source.IsConnected && now - this.lastConnectAttempt >= ConnectRetryDelay)
        {
            this.lastConnectAttempt = now;
            this.source.Start();
        }

        UpdatePreview(now);

        this.encounters.Tick(now);
        this.window.Connected = this.preview is not null || this.source.IsConnected;
        this.window.LocalPlayerName = this.source.LocalPlayerName;

        // Nur hier darf der Spielzustand gelesen werden, deshalb den Namen von
        // hier aus durchreichen statt im IPC-Callback abzufragen.
        this.source.LocalPlayerName = this.playerState.IsLoaded ? this.playerState.CharacterName : null;

        this.window.Frozen = this.encounters.State == EncounterState.Ended;

        RecordGraph(now);
        SyncHistoryView();

        var selectedRecord = this.history.Selected;
        this.window.Graph = selectedRecord is not null
            ? selectedRecord.GraphFor(this.window.Metric)
            : this.liveGraph.For(this.window.Metric);

        if (selectedRecord is { } record)
        {
            // Ein Eintrag steht still; neu uebernommen wird nur bei anderem Eintrag, Messgroesse
            // oder Zeitbasis.
            if (!ReferenceEquals(record, this.shownRecord)
                || this.window.Metric != this.renderedMetric
                || this.window.Mode != this.renderedMode)
                ShowRecord(record);

            return;
        }

        if (this.shownRecord is not null)
        {
            this.shownRecord = null;
            this.liveStale = true;
            this.lastDurationSecond = -1;
        }

        if (this.encounters.State == EncounterState.Running)
        {
            // Zwischen den Datenpaketen weiterrechnen, damit der Verlauf stetig bleibt.
            this.renderedMetric = this.window.Metric;
            this.renderedMode = this.window.Mode;
            this.liveStale = false;

            if (this.lastSnapshot is { } snapshot)
            {
                this.stopwatch.Restart();
                UpdateRows(snapshot, now);
                this.updateTiming.Add(this.stopwatch.Elapsed);
            }
        }
        else if (this.liveStale || this.window.Metric != this.renderedMetric || this.window.Mode != this.renderedMode)
        {
            // Nach Kampfende stehen die Zahlen still - beim Wechsel der Metrik oder Zeitbasis
            // und bei der Rueckkehr aus dem Verlauf muessen sie aber einmal neu gebaut werden,
            // sonst zeigt die Tabelle weiter den Stand von vorher.
            this.liveStale = false;
            RebuildFrozenRows();
        }

        // Waehrend des Kampfes laeuft die Wanduhr, auch wenn IINACT gerade nichts
        // sendet. Beim Einfrieren wird auf die Zeit bis zum letzten Treffer
        // korrigiert - so macht es ACT auch, und nur so passt die angezeigte Zeit
        // zum Encounter-DPS, der ohnehin damit rechnet. Sonst stuende dort eine
        // Dauer, die die Karenzzeit mitzaehlt, und die Rechnung ginge nicht auf.
        // Der Text aendert sich nur einmal je Sekunde, gebaut wird er auch nur dann.
        var shown = this.encounters.State == EncounterState.Running
            ? this.encounters.Duration
            : this.encounters.ActiveDuration;
        var shownSecond = (int)shown.TotalSeconds;
        if (shownSecond != this.lastDurationSecond)
        {
            this.lastDurationSecond = shownSecond;
            this.window.HeaderDuration = shown.ToString(@"mm\:ss");
        }
    }

    /// <summary>
    /// Traegt einmal je Sekunde Kampfzeit die Summe der Echtzeitwerte ein. Hier und nicht im Zeichnen,
    /// damit die Rechnung vom Frametakt unabhaengig bleibt; die Vorpruefung kostet sonst nichts.
    /// Laeuft auch, wenn das Fenster einen Verlaufseintrag zeigt, damit der Live-Kampf nicht luecken bekommt.
    /// </summary>
    private void RecordGraph(DateTime now)
    {
        if (this.encounters.State != EncounterState.Running || this.lastSnapshot is not { } snapshot)
            return;

        var elapsed = this.encounters.Duration;
        if (!this.liveGraph.Due(elapsed))
            return;

        var start = this.encounters.StartedAt ?? snapshot.At;
        var reaction = TimeSpan.FromSeconds(this.config.ReactionSeconds);
        this.liveGraph.Add(
            elapsed,
            MeterRows.RateSum(snapshot, this.rollingDamage, now, start, reaction),
            MeterRows.RateSum(snapshot, this.rollingHealing, now, start, reaction),
            MeterRows.RateSum(snapshot, this.rollingTaken, now, start, reaction));
    }

    /// <summary>
    /// Baut die eingefrorenen Zeilen neu auf - zum Zeitpunkt des letzten Treffers,
    /// nicht zu dem des Kampfendes.
    ///
    /// Dazwischen liegt die Karenzzeit, und in der sinkt der gleitende Wert bereits
    /// ab, weil kein Schaden mehr eintrifft. Wer nach dem Kampf auf die Anzeige
    /// schaut, will aber wissen, wo er zuletzt stand, und nicht, wie weit der Wert
    /// waehrend des Wartens schon gefallen war. Dieselbe Ueberlegung wie bei der Uhr.
    /// </summary>
    private void RebuildFrozenRows()
    {
        this.renderedMetric = this.window.Metric;
        this.renderedMode = this.window.Mode;

        if (this.lastSnapshot is not { } frozen)
        {
            ClearRows();
            return;
        }

        UpdateRows(frozen, this.encounters.LastDamageAt ?? frozen.At);
    }

    private RollingDpsTracker TrackerFor(MeterMetric metric) => metric switch
    {
        MeterMetric.Healing => this.rollingHealing,
        MeterMetric.DamageTaken => this.rollingTaken,
        _ => this.rollingDamage
    };

    /// <summary>
    /// Nimmt den soeben beendeten Kampf in den Verlauf auf, mit denselben Zeilen wie die
    /// eingefrorene Anzeige: zum Zeitpunkt des letzten Treffers, fuer alle Messgroessen.
    /// </summary>
    private void CaptureFight()
    {
        // Die Karenzzeit gehoert nicht zum Verlauf: Die Live-Ansicht friert auf demselben Stand ein
        // wie der Eintrag, der gleich davon kopiert wird.
        this.liveGraph.Truncate(this.encounters.ActiveDuration);

        if (this.lastSnapshot is not { } snapshot)
            return;

        var at = this.encounters.LastDamageAt ?? snapshot.At;
        var record = FightRecord.Capture(
            snapshot,
            at,
            this.encounters.StartedAt ?? snapshot.At,
            this.encounters.EndedAt ?? at,
            this.encounters.ActiveDuration,
            TimeSpan.FromSeconds(this.config.ReactionSeconds),
            this.rollingDamage,
            this.rollingHealing,
            this.rollingTaken,
            this.liveGraph);

        this.history.Add(record);
    }

    /// <summary>Uebernimmt die gespeicherten Werte eines Eintrags ins Fenster; laeuft nur bei Aenderung.</summary>
    private void ShowRecord(FightRecord record)
    {
        lock (this.rowsGate)
        {
            this.shownRecord = record;
            this.renderedMetric = this.window.Metric;
            this.renderedMode = this.window.Mode;

            var rows = record.For(this.window.Metric);
            this.window.SetRows(rows.Rows);
            this.window.HeaderTitle = DisplayTitle(record.Title);
            this.window.HeaderDuration = record.ActiveDuration.ToString(@"mm\:ss");
            this.window.Deaths = record.Deaths;
            this.window.TotalDps = rows.Sum(this.window.Mode == DpsMode.Rolling);
        }
    }

    /// <summary>Der Titel der Vorschau kommt aus dem Core ohne Sprache; Titel aus dem Spiel bleiben, wie sie sind.</summary>
    private static string DisplayTitle(string title) =>
        title == PreviewCombat.Title ? Loc.T("Preview", "Vorschau") : title;

    private void ClearRows()
    {
        lock (this.rowsGate)
        {
            this.window.SetRows([]);
            this.window.TotalDps = 0;
            this.window.Deaths = 0;
            this.window.HeaderTitle = string.Empty;
        }
    }

    /// <summary>
    /// Gibt Knoepfen und Menue den Stand des Verlaufs weiter. Texte entstehen nur, wenn sich der
    /// Verlauf geaendert hat; die Version wird vor dem Lesen gemerkt, damit eine Aenderung
    /// dazwischen im naechsten Frame nachgezogen wird.
    /// </summary>
    private void SyncHistoryView()
    {
        var version = this.history.Version;
        if (version == this.shownHistoryVersion)
            return;

        this.shownHistoryVersion = version;

        var count = this.history.Count;
        var selected = this.history.SelectedIndex;
        var first = this.history.FirstBrowsable;

        var labels = new string[count];
        for (var i = 0; i < count; i++)
        {
            var entry = this.history[i];
            var title = entry.Title.Length > 0 ? DisplayTitle(entry.Title) : Loc.T("Fight", "Kampf");
            labels[i] = $"{title}  {entry.ActiveDuration:mm\\:ss}##fight{i}";
        }

        this.window.HistoryLabels = labels;
        this.window.HistoryFirst = first;
        this.window.HistorySelected = selected;
        this.window.HistoryAvailable = count > first || selected >= 0;
        this.window.HistoryText = selected >= 0 ? $"{selected + 1}/{count}" : null;
        this.window.CanOlder = this.history.CanGoOlder;
        this.window.CanNewer = this.history.CanGoNewer;
    }

    private void OnIinactSnapshot(CombatSnapshot snapshot)
    {
        // Waehrend der Vorschau wuerden echte Daten den erfundenen Kampf durchmischen.
        if (this.preview is not null)
            return;

        OnSnapshot(snapshot);
    }

    /// <summary>
    /// Startet oder beendet die Vorschau, wenn der Schalter umgelegt wurde, und
    /// liefert waehrend sie laeuft einmal je Sekunde ein Datenpaket - im Takt von
    /// IINACT, damit die Rechnung genauso belastet wird wie im echten Kampf.
    /// </summary>
    private void UpdatePreview(DateTime now)
    {
        var wanted = this.configWindow.Preview;
        if (wanted != (this.preview is not null))
        {
            this.conditions.Preview = wanted;
            ResetAll();
            this.preview = wanted
                ? new PreviewCombat(now, this.playerState.IsLoaded ? this.playerState.CharacterName : null)
                : null;
            this.lastPreviewSnapshot = DateTime.MinValue;
        }

        if (this.preview is null || now - this.lastPreviewSnapshot < TimeSpan.FromSeconds(1))
            return;

        this.lastPreviewSnapshot = now;
        OnSnapshot(this.preview.Snapshot(now));
    }

    /// <summary>Verwirft Kampf, Verlaeufe und Anzeige, damit Vorschau und echte Daten sich nicht mischen.</summary>
    private void ResetAll()
    {
        this.encounters.ForceReset();
        this.rollingDamage.Reset();
        this.rollingHealing.Reset();
        this.rollingTaken.Reset();
        this.liveGraph.Reset();
        this.lastSnapshot = null;
        this.window.SetRows([]);
        this.window.TotalDps = 0;
        this.window.Deaths = 0;
        this.window.HeaderTitle = string.Empty;
    }

    private void OnSnapshot(CombatSnapshot snapshot)
    {
        var total = snapshot.Combatants.Sum(c => c.TotalDamage);

        // Reihenfolge ist wesentlich: Update kann einen neuen Kampf ausloesen und
        // setzt dann den Nullpunkt auf den zuletzt bekannten Stand. Erst danach
        // darf der neue Wert eingetragen werden, sonst faellt der erste Treffer
        // des Kampfes unter den Tisch.
        this.encounters.Update(total, snapshot.At);

        // Auch ausserhalb des Kampfes mitschreiben: IINACT zaehlt seinen Encounter
        // weiter, und nur so kennen wir beim naechsten Kampfbeginn den Nullpunkt.
        foreach (var c in snapshot.Combatants)
        {
            this.rollingDamage.Record(c.Name, c.TotalDamage, snapshot.At);
            this.rollingHealing.Record(c.Name, c.TotalHealing, snapshot.At);
            this.rollingTaken.Record(c.Name, c.TotalDamageTaken, snapshot.At);
        }

        // Nach Kampfende bleibt die Anzeige stehen, bis der naechste Kampf beginnt.
        if (this.encounters.State != EncounterState.Running)
            return;

        this.lastSnapshot = snapshot;
        UpdateRows(snapshot, snapshot.At);
    }

    /// <summary>
    /// Baut die Anzeigezeilen. Laeuft nicht nur beim Eintreffen neuer Daten, sondern
    /// jeden Frame: Das gleitende Fenster wandert staendig weiter, der Wert aendert
    /// sich also auch zwischen zwei Datenpaketen. Nur einmal je Sekunde gerechnet
    /// steht die Anzeige dazwischen still und springt dann.
    /// </summary>
    private void UpdateRows(CombatSnapshot snapshot, DateTime now)
    {
        lock (this.rowsGate)
        {
            // Zeigt das Fenster einen Verlaufseintrag, gehoeren ihm die Zeilen. Die Pruefung
            // steht im Schloss von ShowRecord, damit sich beide nicht ueberschreiben.
            if (this.history.SelectedIndex != FightHistory.LiveIndex)
                return;

            var totals = MeterRows.Build(
                snapshot,
                TrackerFor(this.window.Metric),
                now,
                this.encounters.StartedAt ?? snapshot.At,
                TimeSpan.FromSeconds(this.config.ReactionSeconds),
                this.encounters.ActiveDuration,
                this.rowBuffer);

            this.window.SetRows(this.rowBuffer);
            this.window.HeaderTitle = DisplayTitle(snapshot.Title);
            this.window.Deaths = totals.Deaths;

            // Kopf- und Fusszeile zeigen dieselbe Groesse wie die Spalte, damit Zeile
            // und Summe nie Verschiedenes meinen.
            this.window.TotalDps = totals.Sum(this.window.Mode == DpsMode.Rolling);
        }
    }

    public void Dispose()
    {
        Loc.Changed -= RegisterCommand;
        Loc.Changed -= OnLanguageChanged;
        Loc.Dispose();
        this.windows.RemoveAllWindows();
        this.framework.Update -= OnFrameworkUpdate;
        this.pluginInterface.UiBuilder.Draw -= Draw;
        this.pluginInterface.UiBuilder.OpenConfigUi -= OpenConfig;
        this.pluginInterface.UiBuilder.OpenMainUi -= OpenMain;
        this.commandManager.RemoveHandler(CommandName);
        this.source.SnapshotReceived -= OnIinactSnapshot;
        this.source.Dispose();
        ECommons.ECommonsMain.Dispose();
    }
}
