using Zunavio.KdpFactory.Application.Services;
using Zunavio.KdpFactory.Domain.Enums;

namespace Zunavio.KdpFactory.Application.Tests;

public class VersionServiceTests
{
    private readonly VersionService _service = new();

    [Theory]
    [InlineData(null, "v0.1")]
    [InlineData("", "v0.1")]
    [InlineData("v0.3", "v0.1")]
    [InlineData("v0.6", "v0.1")]
    public void NextDraft_always_returns_v0_1_while_drafting(string? current, string expected)
    {
        Assert.Equal(expected, _service.NextDraft(current).ToString());
    }

    [Theory]
    [InlineData("v1.0", "v0.1")]
    [InlineData("v2.0", "v0.1")]
    public void NextDraft_after_approval_starts_new_draft_cycle(string? current, string expected)
    {
        Assert.Equal(expected, _service.NextDraft(current).ToString());
    }

    [Theory]
    [InlineData(null, "v1.0")]
    [InlineData("v0.9", "v1.0")]
    [InlineData("v1.0", "v1.1")]
    [InlineData("v1.3", "v1.4")]
    [InlineData("v2.0", "v2.1")]
    public void NextApproved_follows_revision_rules(string? current, string expected)
    {
        Assert.Equal(expected, _service.NextApproved(current).ToString());
    }

    [Theory]
    [InlineData("v1.0", "v2.0")]
    [InlineData("v2.1", "v3.0")]
    public void MajorReset_bumps_major(string current, string expected)
    {
        Assert.Equal(expected, _service.MajorReset(current).ToString());
    }
}

public class CodeGeneratorTests
{
    [Fact]
    public async Task Project_codes_are_zero_padded_sequences()
    {
        var generator = new CodeGenerator(_ => Task.FromResult(7));
        Assert.Equal("ZNV-007", await generator.NextProjectCodeAsync(CancellationToken.None));
    }

    [Fact]
    public void Run_and_asset_codes_are_deterministic()
    {
        var generator = new CodeGenerator(_ => Task.FromResult(1));
        Assert.Equal("RUN-ZNV-001-042", generator.RunCode("ZNV-001", 42));
        Assert.Equal("ZNV-001_MANUSCRIPT_v1.0", generator.AssetCode("ZNV-001", AssetType.Manuscript, "v1.0"));
    }
}