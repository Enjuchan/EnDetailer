namespace EnDetailer.Core;

/// <summary>Zerlegt die Felder maxhit und maxheal von IINACT.</summary>
public static class AbilityText
{
    /// <summary>
    /// Holt den Namen der Faehigkeit aus "Name-Wert". Der Name ist lokalisiert und kann selbst
    /// Bindestriche enthalten, deshalb wird zuerst das bekannte Suffix aus dem Wertfeld
    /// abgeschnitten und nur ohne Treffer am letzten Bindestrich getrennt, sofern danach eine
    /// Zahl steht. Sonst gilt der ganze Text als Name.
    /// </summary>
    public static string NameFrom(string? raw, string? valueRaw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        var text = raw.Trim();
        var value = valueRaw?.Trim();

        if (!string.IsNullOrEmpty(value) && text.EndsWith("-" + value, StringComparison.Ordinal))
            return text[..^(value.Length + 1)].Trim();

        var cut = text.LastIndexOf('-');
        if (cut <= 0 || cut == text.Length - 1)
            return text;

        for (var i = cut + 1; i < text.Length; i++)
        {
            if (!char.IsDigit(text[i]) && text[i] != '.' && text[i] != ',')
                return text;
        }

        return text[..cut].Trim();
    }
}
