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
    public bool InCombat => condition[ConditionFlag.InCombat];

    public bool InCutscene =>
        condition[ConditionFlag.OccupiedInCutSceneEvent] ||
        condition[ConditionFlag.WatchingCutscene] ||
        condition[ConditionFlag.WatchingCutscene78];

    public uint ZoneId => clientState.TerritoryType;
}
