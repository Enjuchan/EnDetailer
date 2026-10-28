using System;
using EnDetailer.Core;
using Xunit;

public class MeterVisibilityTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private sealed class FakeConditions : IGameConditions
    {
        public bool InCombat { get; set; }
        public bool InCutscene { get; set; }
        public uint ZoneId { get; set; }
        public bool InDuty { get; set; }
    }

    [Fact]
    public void Always_IsVisibleOutOfCombat()
    {
        var v = new MeterVisibility(new FakeConditions());

        Assert.True(v.IsVisible(T0));
    }

    [Fact]
    public void InCombat_HiddenBeforeAnyFight()
    {
        var v = new MeterVisibility(new FakeConditions()) { Mode = ShowMode.InCombat };

        Assert.False(v.IsVisible(T0));
    }

    [Fact]
    public void InCombat_StaysForTheAfterCombatTimeThenHides()
    {
        var c = new FakeConditions { InCombat = true };
        var v = new MeterVisibility(c) { Mode = ShowMode.InCombat, AfterCombat = TimeSpan.FromSeconds(15) };

        Assert.True(v.IsVisible(T0));

        c.InCombat = false;
        Assert.True(v.IsVisible(T0.AddSeconds(15)));
        Assert.False(v.IsVisible(T0.AddSeconds(15.1)));
    }

    [Fact]
    public void InCombat_SwitchingModeRightAfterAFightKeepsTheAfterCombatTime()
    {
        var c = new FakeConditions { InCombat = true };
        var v = new MeterVisibility(c);

        v.IsVisible(T0);
        c.InCombat = false;
        v.Mode = ShowMode.InCombat;

        Assert.True(v.IsVisible(T0.AddSeconds(5)));
    }

    [Fact]
    public void InDuty_FollowsTheDutyFlagNotCombat()
    {
        var c = new FakeConditions { InCombat = true };
        var v = new MeterVisibility(c) { Mode = ShowMode.InDuty };

        Assert.False(v.IsVisible(T0));

        c.InCombat = false;
        c.InDuty = true;
        Assert.True(v.IsVisible(T0.AddSeconds(1)));
    }

    [Theory]
    [InlineData(ShowMode.Always)]
    [InlineData(ShowMode.InCombat)]
    [InlineData(ShowMode.InDuty)]
    public void HideInCutscenes_HidesInEveryMode(ShowMode mode)
    {
        var c = new FakeConditions { InCombat = true, InDuty = true, InCutscene = true };
        var v = new MeterVisibility(c) { Mode = mode, HideInCutscenes = true };

        Assert.False(v.IsVisible(T0));
    }

    [Fact]
    public void Cutscene_WithoutTheOption_StaysVisible()
    {
        var c = new FakeConditions { InCutscene = true };
        var v = new MeterVisibility(c);

        Assert.True(v.IsVisible(T0));
    }
}
