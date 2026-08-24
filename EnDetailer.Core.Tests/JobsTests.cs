using EnDetailer.Core;
using Xunit;

public class JobsTests
{
    [Theory]
    [InlineData("GNB", Job.GNB)]
    [InlineData("gnb", Job.GNB)]
    [InlineData("WHM", Job.WHM)]
    [InlineData("PCT", Job.PCT)]
    [InlineData("SAM", Job.SAM)]
    public void Parse_ReadsTheAbbreviationIinactSends(string raw, Job expected)
    {
        Assert.Equal(expected, Jobs.Parse(raw));
    }

    [Theory]
    [InlineData("37", Job.GNB)]
    [InlineData("24", Job.WHM)]
    public void Parse_StillAcceptsNumericIds(string raw, Job expected)
    {
        Assert.Equal(expected, Jobs.Parse(raw));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("Limit Break")]
    [InlineData("999")]
    public void Parse_ReturnsUnknownForAnythingElse(string? raw)
    {
        Assert.Equal(Job.Unknown, Jobs.Parse(raw));
    }

    [Fact]
    public void IconId_MatchesTheGamesIconRange()
    {
        Assert.Equal(62037u, Jobs.IconId(Job.GNB));
        Assert.Equal(0u, Jobs.IconId(Job.Unknown));
    }

    [Theory]
    [InlineData(Job.GNB, JobRole.Tank)]
    [InlineData(Job.SGE, JobRole.Healer)]
    [InlineData(Job.VPR, JobRole.Melee)]
    [InlineData(Job.DNC, JobRole.RangedPhysical)]
    [InlineData(Job.PCT, JobRole.Caster)]
    public void RoleOf_AssignsEveryModernJob(Job job, JobRole expected)
    {
        Assert.Equal(expected, Jobs.RoleOf(job));
    }
}
