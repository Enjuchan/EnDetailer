using System;
using System.Linq;
using Dalamud.Game.Command;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using EnDetailer.Core;

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

    private readonly IinactSource source;
    private readonly GameConditions conditions;
    private readonly EncounterTracker encounters;
    private readonly RollingDpsTracker rollingDamage = new();
    private readonly RollingDpsTracker rollingHealing = new();
    private readonly RollingDpsTracker rollingTaken = new();
    private readonly MeterWindow window;

    private readonly Configuration config;
    private readonly ConfigWindow configWindow;

    private CombatSnapshot? lastSnapshot;
    private MeterMetric renderedMetric = MeterMetric.Damage;
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

        this.config = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        this.config.Initialize(pluginInterface);

        this.conditions = new GameConditions(condition, clientState);
        this.encounters = new EncounterTracker(this.conditions)
        {
            GracePeriod = TimeSpan.FromSeconds(this.config.GraceSeconds),
            ResetOnZoneChange = this.config.ResetOnZoneChange
        };

        this.configWindow = new ConfigWindow(this.config, this.encounters);
        this.window = new MeterWindow(this.config, textures, pluginInterface.UiBuilder);
        this.window.OpenConfigRequested = OpenConfig;
        this.encounters.EncounterStarted += () =>
        {
            var at = this.encounters.StartedAt ?? DateTime.UtcNow;
            this.rollingDamage.MarkEncounterStart(at);
            this.rollingHealing.MarkEncounterStart(at);
            this.rollingTaken.MarkEncounterStart(at);
        };
        this.encounters.EncounterStarted += () => this.window.ResetSmoothing();
        this.encounters.EncounterEnded += OnEncounterEnded;
        this.encounters.EncounterEnded += RebuildFrozenRows;

        this.source = new IinactSource(pluginInterface, log);
        this.source.SnapshotReceived += OnSnapshot;

        // Kein Verbindungsversuch im Konstruktor: Beim Spielstart laden alle
        // Plugins gleichzeitig, und IINACT ist dann meist noch nicht bereit.
        // Der Framework-Puls versucht es, bis es klappt.

        this.commandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Toggles the EnDetailer window. Use \'config\' or \'lock\' as arguments."
        });

        this.framework.Update += OnFrameworkUpdate;
        this.pluginInterface.UiBuilder.Draw += this.window.Draw;
        this.pluginInterface.UiBuilder.Draw += this.configWindow.Draw;
        this.pluginInterface.UiBuilder.OpenConfigUi += OpenConfig;
        this.pluginInterface.UiBuilder.OpenMainUi += OpenMain;
        this.log.Information("EnDetailer loaded.");
    }

    private void OnCommand(string command, string args)
    {
        var arg = args.Trim();

        if (arg.Equals("config", StringComparison.OrdinalIgnoreCase))
        {
            OpenConfig();
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

    private void OpenConfig() => this.configWindow.Visible = true;

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

        this.encounters.Tick(now);
        this.window.Connected = this.source.IsConnected;
        this.window.LocalPlayerName = this.source.LocalPlayerName;

        // Nur hier darf der Spielzustand gelesen werden, deshalb den Namen von
        // hier aus durchreichen statt im IPC-Callback abzufragen.
        this.source.LocalPlayerName = this.playerState.IsLoaded ? this.playerState.CharacterName : null;

        this.window.Frozen = this.encounters.State == EncounterState.Ended;

        if (this.encounters.State == EncounterState.Running)
        {
            // Zwischen den Datenpaketen weiterrechnen, damit der Verlauf stetig bleibt.
            this.renderedMetric = this.window.Metric;

            if (this.lastSnapshot is { } snapshot)
                UpdateRows(snapshot, now);
        }
        else if (this.window.Metric != this.renderedMetric)
        {
            // Nach Kampfende stehen die Zahlen still - beim Wechsel der Metrik muessen
            // sie aber einmal neu gebaut werden, sonst zeigt die Tabelle weiter die
            // Werte der Metrik, die beim Einfrieren aktiv war.
            RebuildFrozenRows();
        }

        // Waehrend des Kampfes laeuft die Wanduhr, auch wenn IINACT gerade nichts
        // sendet. Beim Einfrieren wird auf die Zeit bis zum letzten Treffer
        // korrigiert - so macht es ACT auch, und nur so passt die angezeigte Zeit
        // zum Encounter-DPS, der ohnehin damit rechnet. Sonst stuende dort eine
        // Dauer, die die Karenzzeit mitzaehlt, und die Rechnung ginge nicht auf.
        this.window.HeaderDuration = this.encounters.State == EncounterState.Running
            ? this.encounters.Duration.ToString(@"mm\:ss")
            : this.encounters.ActiveDuration.ToString(@"mm\:ss");
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
        if (this.lastSnapshot is not { } frozen)
            return;

        this.renderedMetric = this.window.Metric;
        UpdateRows(frozen, this.encounters.LastDamageAt ?? frozen.At);
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
        var start = this.encounters.StartedAt ?? snapshot.At;
        var window = TimeSpan.FromSeconds(this.config.RollingWindowSeconds);

        // Welcher Verlauf gilt, haengt an der Metrik; wie daraus eine Rate wird,
        // an der eingestellten Methode. Beides ist voneinander unabhaengig.
        var tracker = this.window.Metric switch
        {
            MeterMetric.Healing => this.rollingHealing,
            MeterMetric.DamageTaken => this.rollingTaken,
            _ => this.rollingDamage
        };

        double CurrentRate(string name) => this.config.DpsMethod == DpsMethod.Weighted
            ? tracker.GetWeightedDps(name, now, window)
            : tracker.GetRollingDps(name, now, window, start);


        // Den Encounter-DPS selbst rechnen statt IINACTs Wert zu uebernehmen:
        // dessen Encounter kann laenger laufen als unserer, dann bezoege sich die
        // Spalte auf einen anderen Zeitraum als Total und aktueller DPS.
        // Als Nenner dient die Zeit bis zum letzten Treffer, nicht die Wanduhr -
        // sonst druecken Karenzzeit und Nachlauf den Wert unter das, was LMeter
        // und FFLogs zeigen.
        var seconds = Math.Max(1, this.encounters.ActiveDuration.TotalSeconds);

        this.window.SetRows(snapshot.Combatants.Select(c => new MeterRow(
            c.Name,
            c.Job,
            tracker.GetTotalDamage(c.Name),
            CurrentRate(c.Name),
            tracker.GetTotalDamage(c.Name) / seconds,
            c.CritPercent,
            c.DirectHitPercent,
            c.OverhealPercent)).ToList());

        this.window.HeaderTitle = snapshot.Title;
        this.window.Deaths = snapshot.Combatants.Sum(c => c.Deaths);

        // Kopf- und Fusszeile zeigen dieselbe Groesse wie die Spalte, damit Zeile
        // und Summe nie Verschiedenes meinen.
        this.window.TotalDps = this.window.Mode == DpsMode.Rolling
            ? snapshot.Combatants.Sum(c => CurrentRate(c.Name))
            : snapshot.Combatants.Sum(c => tracker.GetTotalDamage(c.Name)) / seconds;
    }

    public void Dispose()
    {
        this.framework.Update -= OnFrameworkUpdate;
        this.pluginInterface.UiBuilder.Draw -= this.window.Draw;
        this.pluginInterface.UiBuilder.Draw -= this.configWindow.Draw;
        this.pluginInterface.UiBuilder.OpenConfigUi -= OpenConfig;
        this.pluginInterface.UiBuilder.OpenMainUi -= OpenMain;
        this.commandManager.RemoveHandler(CommandName);
        this.source.SnapshotReceived -= OnSnapshot;
        this.source.Dispose();
    }
}
