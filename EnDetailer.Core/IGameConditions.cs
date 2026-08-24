namespace EnDetailer.Core;

/// <summary>
/// Der Spielzustand, soweit die Encounter-Logik ihn braucht. Im Plugin von
/// Dalamud bedient, in Tests durch eine Attrappe ersetzt.
/// </summary>
public interface IGameConditions
{
    bool InCombat { get; }
    bool InCutscene { get; }
    uint ZoneId { get; }
}
