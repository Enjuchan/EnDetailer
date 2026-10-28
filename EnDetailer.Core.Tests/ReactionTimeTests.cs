using EnDetailer.Core;
using Xunit;

public class ReactionTimeTests
{
    [Theory]
    [InlineData(25, 10)]
    [InlineData(120, 30)]
    [InlineData(5, 4)]
    [InlineData(60, 24)]
    public void FromWindowSeconds_ConvertsAndClamps(int window, int expected)
    {
        Assert.Equal(expected, ReactionTime.FromWindowSeconds(window));
    }
}
