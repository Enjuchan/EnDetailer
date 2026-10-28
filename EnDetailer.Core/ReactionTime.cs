using System;

namespace EnDetailer.Core;

/// <summary>Grenzen und Umrechnung der Reaktionszeit des Echtzeit-Filters.</summary>
public static class ReactionTime
{
    public const int MinSeconds = 4;
    public const int MaxSeconds = 30;
    public const int DefaultSeconds = 10;

    /// <summary>
    /// Rechnet die alte Fensterlaenge (Version 1 der Konfiguration) in eine
    /// Reaktionszeit um, damit bestehende Einstellungen ungefaehr gleich reagieren.
    /// </summary>
    public static int FromWindowSeconds(int windowSeconds) =>
        Math.Clamp((int)Math.Round(windowSeconds / 2.5, MidpointRounding.AwayFromZero), MinSeconds, MaxSeconds);
}
