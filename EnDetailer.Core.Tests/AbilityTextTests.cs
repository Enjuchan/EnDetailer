using EnDetailer.Core;
using Xunit;

namespace EnDetailer.Core.Tests;

public class AbilityTextTests
{
    [Fact]
    public void Strips_the_value_suffix()
    {
        Assert.Equal("Einfaches Finale II", AbilityText.NameFrom("Einfaches Finale II-74222", "74222"));
    }

    [Fact]
    public void Keeps_hyphens_inside_the_name()
    {
        Assert.Equal("Dun-Scaith Strike", AbilityText.NameFrom("Dun-Scaith Strike-1234", "1234"));
    }

    [Fact]
    public void Splits_at_last_hyphen_when_value_is_missing_or_differs()
    {
        Assert.Equal("Dun-Scaith Strike", AbilityText.NameFrom("Dun-Scaith Strike-1234", null));
        Assert.Equal("Dun-Scaith Strike", AbilityText.NameFrom("Dun-Scaith Strike-1234", "999"));
    }

    [Fact]
    public void Keeps_whole_text_when_nothing_numeric_follows()
    {
        Assert.Equal("Dun-Scaith Strike", AbilityText.NameFrom("Dun-Scaith Strike", ""));
        Assert.Equal("Fire", AbilityText.NameFrom("Fire", "0"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_value_gives_empty_name(string? raw)
    {
        Assert.Equal(string.Empty, AbilityText.NameFrom(raw, "0"));
    }
}
