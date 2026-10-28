using EnDetailer;

namespace EnDetailer.Core.Tests;

public class WelcomeTests
{
    private const string Running = "0.2.3.0";

    [Fact]
    public void Decide_NoConfig_IsFreshInstall()
    {
        Assert.Equal(WelcomeAction.ShowWelcome, WelcomeRules.Decide(false, null, Running));
    }

    [Fact]
    public void Decide_NoConfig_IgnoresSavedVersion()
    {
        Assert.Equal(WelcomeAction.ShowWelcome, WelcomeRules.Decide(false, Running, Running));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Decide_ConfigWithoutSavedVersion_IsUpdate(string? seen)
    {
        Assert.Equal(WelcomeAction.ShowChangelog, WelcomeRules.Decide(true, seen, Running));
    }

    [Theory]
    [InlineData("0.2.2.0")]
    [InlineData("v0.2.2.0")]
    [InlineData("0.1.0.0")]
    public void Decide_OlderSavedVersion_IsUpdate(string seen)
    {
        Assert.Equal(WelcomeAction.ShowChangelog, WelcomeRules.Decide(true, seen, Running));
    }

    [Fact]
    public void Decide_SameVersion_ShowsNothing()
    {
        Assert.Equal(WelcomeAction.None, WelcomeRules.Decide(true, Running, Running));
    }

    [Fact]
    public void Decide_MissingPlacesCountAsZero()
    {
        Assert.Equal(WelcomeAction.None, WelcomeRules.Decide(true, "0.2.3", Running));
    }

    [Fact]
    public void Decide_FourthPlaceCounts()
    {
        Assert.Equal(WelcomeAction.ShowChangelog, WelcomeRules.Decide(true, "0.2.3.0", "0.2.3.1"));
    }

    [Fact]
    public void Decide_NewerSavedVersion_ShowsNothing()
    {
        Assert.Equal(WelcomeAction.None, WelcomeRules.Decide(true, "0.3.0.0", Running));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("0.x.1")]
    [InlineData("7")]
    public void Decide_InvalidSavedVersion_IsUpdate(string seen)
    {
        Assert.Equal(WelcomeAction.ShowChangelog, WelcomeRules.Decide(true, seen, Running));
    }

    [Fact]
    public void Decide_InvalidRunningVersion_ShowsNothing()
    {
        Assert.Equal(WelcomeAction.None, WelcomeRules.Decide(true, "0.1.0.0", "unknown"));
    }

    [Fact]
    public void IsNew_MarksOnlyVersionsAfterBaseline()
    {
        var versions = new[] { "0.2.3.0", "0.2.2.0", "0.2.1.0", "0.2.0.0", "0.1.0.0" };
        var marked = versions.Where(v => WelcomeRules.IsNew(v, "0.2.1.0", Running)).ToArray();
        Assert.Equal(["0.2.3.0", "0.2.2.0"], marked);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("garbage")]
    public void IsNew_NoUsableBaseline_MarksOnlyRunningVersion(string? baseline)
    {
        Assert.True(WelcomeRules.IsNew("0.2.3.0", baseline, Running));
        Assert.False(WelcomeRules.IsNew("0.2.2.0", baseline, Running));
    }

    [Fact]
    public void IsNew_AfterFreshInstall_MarksNothing()
    {
        var baseline = WelcomeRules.Baseline(false, null, Running);
        Assert.False(WelcomeRules.IsNew("0.2.3.0", baseline, Running));
        Assert.False(WelcomeRules.IsNew("0.1.0.0", baseline, Running));
    }

    [Fact]
    public void IsNew_NewerSavedVersion_MarksNothing()
    {
        Assert.False(WelcomeRules.IsNew("0.2.3.0", "0.3.0.0", Running));
    }

    [Fact]
    public void IsNew_ToleratesPrefixAndMissingPlaces()
    {
        Assert.True(WelcomeRules.IsNew("v0.2.3.0", "0.2.2", Running));
        Assert.False(WelcomeRules.IsNew("0.2.2.0", "0.2.2", Running));
    }

    [Fact]
    public void IsNew_InvalidEntryVersion_IsNotNew()
    {
        Assert.False(WelcomeRules.IsNew("soon", "0.1.0.0", Running));
    }

    [Fact]
    public void Baseline_UpdateKeepsSavedVersion()
    {
        Assert.Equal("0.2.2.0", WelcomeRules.Baseline(true, "0.2.2.0", Running));
        Assert.Null(WelcomeRules.Baseline(true, null, Running));
    }
}
