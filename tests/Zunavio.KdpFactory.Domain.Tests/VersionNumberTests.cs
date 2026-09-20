using Zunavio.KdpFactory.Domain.ValueObjects;

namespace Zunavio.KdpFactory.Domain.Tests;

public class VersionNumberTests
{
    [Theory]
    [InlineData("v0.1", 0, 1)]
    [InlineData("v1.0", 1, 0)]
    [InlineData("1.2", 1, 2)]
    [InlineData("v2", 2, 0)]
    public void Parse_accepts_valid_formats(string input, int major, int minor)
    {
        var version = VersionNumber.Parse(input);
        Assert.Equal(major, version.Major);
        Assert.Equal(minor, version.Minor);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("v1.x")]
    [InlineData("1.2.3")]
    [InlineData("-1.0")]
    public void Parse_rejects_invalid_formats(string input)
    {
        Assert.False(VersionNumber.TryParse(input, out _));
    }

    [Fact]
    public void Draft_characteristics()
    {
        Assert.True(VersionNumber.V0_1.IsDraft);
        Assert.False(VersionNumber.V1_0.IsDraft);
        Assert.Equal("v0.1", VersionNumber.V0_1.ToString());
    }

    [Fact]
    public void Version_progression_rules()
    {
        Assert.Equal("v0.2", VersionNumber.Parse("v0.1").NextDraft().ToString());
        Assert.Equal("v1.0", VersionNumber.Parse("v0.9").PromoteToApproved().ToString());
        Assert.Equal("v1.1", VersionNumber.Parse("v1.0").NextApprovedRevision().ToString());
        Assert.Equal("v2.0", VersionNumber.Parse("v1.5").MajorReset().ToString());
    }

    [Theory]
    [InlineData("v0.1", AssetProductionState.Draft, "v0.2")]
    [InlineData("v1.0", AssetProductionState.Approved, "v1.1")]
    [InlineData("v1.0", AssetProductionState.ExistingApprovedReplacing, "v2.0")]
    public void NextFor_matches_production_state(string current, AssetProductionState state, string expected)
    {
        Assert.Equal(expected, VersionNumber.Parse(current).NextFor(state).ToString());
    }
}