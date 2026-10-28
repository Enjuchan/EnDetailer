using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using EnDetailer.Core;

namespace EnDetailer;

/// <summary>
/// Der Spielzustand direkt aus Dalamud. Genau hierin liegt der Vorteil eines
/// echten Plugins: Wir muessen nicht raten, was gerade im Kampf passiert.
/// </summary>
public sealed class GameConditions(ICondition condition, IClientState clientState) : IGameConditions
{
    /// <summary>Waehrend der Vorschau gilt die Gruppe als im Kampf, sonst endete er nach der Karenzzeit.</summary>
    public bool Preview { get; set; }

    public bool InCombat => this.Preview || condition[ConditionFlag.InCombat];

    public bool InCutscene =>
        condition[ConditionFlag.OccupiedInCutSceneEvent] ||
        condition[ConditionFlag.WatchingCutscene] ||
        condition[ConditionFlag.WatchingCutscene78];

    public uint ZoneId => clientState.TerritoryType;

    public bool InDuty =>
        condition[ConditionFlag.BoundByDuty] ||
        condition[ConditionFlag.BoundByDuty56] ||
        condition[ConditionFlag.BoundByDuty95];

    /// <summary>
    /// Ob ueberhaupt ein Overlay ins Bild darf. UiBuilder.Draw laeuft schon auf dem
    /// Titelbildschirm und in Ladebildschirmen; ohne diese Pruefung stand das Meter dort.
    /// </summary>
    public bool CanShowOverlay =>
        clientState.IsLoggedIn &&
        !condition[ConditionFlag.BetweenAreas] &&
        !condition[ConditionFlag.BetweenAreas51];
}
