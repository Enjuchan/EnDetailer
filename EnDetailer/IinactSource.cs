using System;
using System.Collections.Generic;
using System.Globalization;
using Dalamud.Game.Text;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using EnDetailer.Core;
using Newtonsoft.Json.Linq;

namespace EnDetailer;

/// <summary>
/// Haengt sich per Dalamud-IPC an IINACT, wie LMeter es tut. Kein WebSocket noetig.
/// Die Endpunktnamen stammen aus IINACTs IpcProviders.
/// </summary>
public sealed class IinactSource : IDisposable
{
    private const string SubscriptionEndpoint = "EnDetailer.SubscriptionReceiver";
    private const string ListeningEndpoint = "IINACT.Server.Listening";
    private const string SubscribeEndpoint = "IINACT.CreateSubscriber";
    private const string UnsubscribeEndpoint = "IINACT.Unsubscribe";
    private const string ProviderEndpoint = "IINACT.IpcProvider." + SubscriptionEndpoint;
    private const string SubscriptionMessage = """{"call":"subscribe","events":["CombatData"]}""";

    private readonly IDalamudPluginInterface pluginInterface;
    private readonly IPluginLog log;
    private readonly ICallGateProvider<JObject, bool> receiver;

    public bool IsConnected { get; private set; }

    /// <summary>Verhindert, dass der Wiederholversuch das Log zumuellt.</summary>
    private bool warnedAboutMissingIinact;

    /// <summary>
    /// IINACT setzt fuer den eigenen Charakter den Platzhalter "YOU" ein, auch in
    /// zusammengesetzten Namen wie "Chocobo (YOU)". Das Plugin traegt hier den
    /// echten Namen ein - aus dem Framework-Thread, denn nur dort darf der
    /// Spielzustand gelesen werden.
    /// </summary>
    public string? LocalPlayerName { get; set; }

    public event Action<CombatSnapshot>? SnapshotReceived;

    public IinactSource(IDalamudPluginInterface pluginInterface, IPluginLog log)
    {
        this.pluginInterface = pluginInterface;
        this.log = log;
        this.receiver = pluginInterface.GetIpcProvider<JObject, bool>(SubscriptionEndpoint);
        this.receiver.RegisterFunc(Receive);
    }

    public void Start()
    {
        try
        {
            var listening = this.pluginInterface.GetIpcSubscriber<bool>(ListeningEndpoint).InvokeFunc();
            if (!listening)
            {
                if (!this.warnedAboutMissingIinact)
                {
                    this.log.Warning("IINACT is not running or not ready yet, will keep trying.");
                    this.warnedAboutMissingIinact = true;
                }

                return;
            }

            var subscribed = this.pluginInterface
                .GetIpcSubscriber<string, bool>(SubscribeEndpoint)
                .InvokeFunc(SubscriptionEndpoint);

            if (!subscribed)
            {
                this.log.Warning("IINACT rejected the subscription.");
                return;
            }

            this.pluginInterface
                .GetIpcSubscriber<JObject, bool>(ProviderEndpoint)
                .InvokeAction(JObject.Parse(SubscriptionMessage));

            this.IsConnected = true;
            this.warnedAboutMissingIinact = false;
            this.log.Information("Connected to IINACT.");
        }
        catch (Exception ex)
        {
            if (!this.warnedAboutMissingIinact)
            {
                this.log.Warning(ex, "Connection to IINACT failed, will keep trying.");
                this.warnedAboutMissingIinact = true;
            }
        }
    }

    private bool Receive(JObject data)
    {
        try
        {
            if (data["type"]?.ToString() != "CombatData")
                return true;

            var encounter = data["Encounter"];
            var combatants = new List<CombatantSnapshot>();

            if (data["Combatant"] is JObject table)
            {
                foreach (var entry in table.Properties())
                {
                    var c = entry.Value;
                    combatants.Add(new CombatantSnapshot(
                        Name: ResolveName(c["name"]?.ToString() ?? entry.Name),
                        Job: Jobs.Parse(c["Job"]?.ToString()),
                        TotalDamage: ParseNumber(c["damage"]?.ToString()),
                        EncounterDps: ParseNumber(c["encdps"]?.ToString()),
                        CritPercent: ParsePercent(c["crithit%"]?.ToString()),
                        DirectHitPercent: ParsePercent(c["DirectHitPct"]?.ToString()),
                        Deaths: (int)ParseNumber(c["deaths"]?.ToString())));
                }
            }

            this.SnapshotReceived?.Invoke(new CombatSnapshot(
                At: DateTime.UtcNow,
                Title: encounter?["title"]?.ToString() ?? string.Empty,
                DurationRaw: encounter?["duration"]?.ToString() ?? string.Empty,
                IsActive: bool.TryParse(data["isActive"]?.ToString(), out var active) && active,
                Combatants: combatants));
        }
        catch (Exception ex)
        {
            this.log.Error(ex, "Failed to parse CombatData.");
        }

        return true;
    }

    /// <summary>
    /// Schliesst den Encounter auch auf IINACT-Seite. IINACT wertet eine lokale
    /// Echo-Nachricht mit dem Text "end" als Kommando aus.
    /// </summary>
    public void EndEncounter(IChatGui chatGui) =>
        chatGui.Print(new XivChatEntry { Message = "end", Type = XivChatType.Echo });

    private string ResolveName(string name) =>
        string.IsNullOrEmpty(this.LocalPlayerName)
            ? name
            : name.Replace("YOU", this.LocalPlayerName, StringComparison.Ordinal);

    /// <summary>
    /// IINACT formatiert seine Zahlen in der Systemkultur: auf einem deutschen
    /// Windows kommt "3686,62" mit Komma als Dezimaltrennzeichen. Wer das Komma
    /// als Tausendertrenner ansieht und entfernt, bekommt den hundertfachen Wert.
    /// Deshalb wird zuerst mit der Systemkultur geparst, wie LMeter es auch tut,
    /// und nur als Rueckfallebene invariant.
    /// </summary>
    private static double ParseNumber(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return 0;

        var text = raw.Trim();
        var factor = 1d;
        if (text.EndsWith("K", StringComparison.OrdinalIgnoreCase))
        {
            factor = 1000;
            text = text[..^1];
        }
        else if (text.EndsWith("M", StringComparison.OrdinalIgnoreCase))
        {
            factor = 1_000_000;
            text = text[..^1];
        }

        if (double.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out var value))
            return value * factor;

        return double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out value)
            ? value * factor
            : 0;
    }

    private static double ParsePercent(string? raw) =>
        ParseNumber(raw?.Replace("%", string.Empty));



    public void Stop()
    {
        try
        {
            this.pluginInterface.GetIpcSubscriber<string, bool>(UnsubscribeEndpoint).InvokeFunc(SubscriptionEndpoint);
        }
        catch (Exception)
        {
            // Beim Beenden nicht weiter stoeren
        }

        this.IsConnected = false;
    }

    public void Dispose()
    {
        Stop();
        this.receiver.UnregisterFunc();
    }
}
