using Zunavio.KdpFactory.Domain.Enums;
using Zunavio.KdpFactory.Domain.Rules;

namespace Zunavio.KdpFactory.Domain.Tests;

public class ControlCenterLegacyMapperTests
{
    [Theory]
    [InlineData("MARKET_RESEARCH", ProjectGate.MarketResearch)]
    [InlineData("Market Research", ProjectGate.MarketResearch)]
    [InlineData("READY_TO_PUBLISH", ProjectGate.ReadyToPublish)]
    [InlineData("PUBLISHED", ProjectGate.Published)]
    public void TryParseGate_parses_known_gates_ignoring_case_and_separators(string value, ProjectGate expected)
    {
        Assert.Equal(expected, ControlCenterLegacyMapper.TryParseGate(value));
    }

    [Theory]
    [InlineData("REJECTED")]
    [InlineData("PAUSED")]
    [InlineData("BLOCKED_NEEDS_HUMAN")]
    [InlineData("ON_HOLD")]
    [InlineData("UNKNOWN_VALUE")]
    [InlineData(null)]
    public void TryParseGate_returns_null_for_non_gate_words(string? value)
    {
        Assert.Null(ControlCenterLegacyMapper.TryParseGate(value));
    }

    [Theory]
    [InlineData("PAUSED", ProjectStatus.Paused)]
    [InlineData("BLOCKED", ProjectStatus.Paused)]
    [InlineData("BLOCKED_NEEDS_HUMAN", ProjectStatus.Paused)]
    [InlineData("REJECTED", ProjectStatus.Rejected)]
    [InlineData("COMPLETED", ProjectStatus.Completed)]
    [InlineData("ACTIVE", ProjectStatus.Active)]
    [InlineData("RUNNING_AUTONOMOUS", ProjectStatus.Active)]
    public void TryParseStatus_parses_legacy_status_vocabulary(string value, ProjectStatus expected)
    {
        Assert.Equal(expected, ControlCenterLegacyMapper.TryParseStatus(value));
    }

    [Fact]
    public void MapStatus_non_gate_word_in_current_gate_overrides_status_column()
    {
        // ZNV-005 style row: Current_Gate carries REJECTED while Status is ignored.
        Assert.Equal(ProjectStatus.Rejected, ControlCenterLegacyMapper.MapStatus("REJECTED", "ACTIVE"));
        Assert.Equal(ProjectStatus.Paused, ControlCenterLegacyMapper.MapStatus("PAUSED", "RUNNING_AUTONOMOUS"));
    }

    [Fact]
    public void MapStatus_normal_gate_leaves_status_column_as_authority()
    {
        Assert.Equal(ProjectStatus.Active, ControlCenterLegacyMapper.MapStatus("MARKET_RESEARCH", "ACTIVE"));
        // READY_TO_PUBLISH / PUBLISHED are gates, not status overrides here.
        Assert.Equal(ProjectStatus.Active, ControlCenterLegacyMapper.MapStatus("READY_TO_PUBLISH", "ACTIVE"));
        Assert.Equal(ProjectStatus.Completed, ControlCenterLegacyMapper.MapStatus("PUBLISHED", "PUBLISHED"));
    }

    [Fact]
    public void MapState_rejected_project_falls_back_to_inferred_gate_and_rejected_status()
    {
        var (gate, status) = ControlCenterLegacyMapper.MapState("REJECTED", null, null, null, null);
        Assert.Equal(ProjectGate.Idea, gate);
        Assert.Equal(ProjectStatus.Rejected, status);
    }

    [Fact]
    public void MapState_rejected_with_versions_keeps_the_version_derived_gate()
    {
        var (gate, status) = ControlCenterLegacyMapper.MapState("REJECTED", "ACTIVE", "v0.3", "v0.1", null);
        Assert.Equal(ProjectGate.VisualProduction, gate);
        Assert.Equal(ProjectStatus.Rejected, status);
    }

    [Fact]
    public void MapState_unknown_display_gate_is_tolerated_and_inferred()
    {
        var (gate, status) = ControlCenterLegacyMapper.MapState("UNKNOWN", "RUMMAGE", "v0.1", null, null);
        Assert.Equal(ProjectGate.Manuscript, gate);
        Assert.Equal(ProjectStatus.Active, status);
    }

    [Theory]
    [InlineData(null, null, null, ProjectGate.Idea)]
    [InlineData("v0.1", null, null, ProjectGate.Manuscript)]
    [InlineData(null, "v0.2", null, ProjectGate.VisualProduction)]
    [InlineData(null, null, "v0.9", ProjectGate.VisualProduction)]
    [InlineData("v0.1", "v0.2", "v0.9", ProjectGate.VisualProduction)]
    public void InferGate_uses_version_columns(string? manuscript, string? visualBible, string? production, ProjectGate expected)
    {
        Assert.Equal(expected, ControlCenterLegacyMapper.InferGate(manuscript, visualBible, production));
    }

    [Fact]
    public void NextAction_for_an_active_gate_returns_run_instruction()
    {
        var (gate, status) = ControlCenterLegacyMapper.MapState("MARKET_RESEARCH", "ACTIVE", null, null, null);
        Assert.Equal("RUN_SCOUT", ControlCenterLegacyMapper.NextAction(gate, status));
    }

    [Fact]
    public void NextAction_for_terminal_states_echoes_the_state()
    {
        var (rejectedGate, rejected) = ControlCenterLegacyMapper.MapState("REJECTED", null, null, null, null);
        Assert.Equal("REJECTED", ControlCenterLegacyMapper.NextAction(rejectedGate, rejected));

        var (pausedGate, paused) = ControlCenterLegacyMapper.MapState("PAUSED", null, null, null, null);
        Assert.Equal("PAUSED", ControlCenterLegacyMapper.NextAction(pausedGate, paused));

        Assert.Equal("COMPLETED", ControlCenterLegacyMapper.NextAction(ProjectGate.Published, ProjectStatus.Completed));
    }

    [Fact]
    public void NextAction_prefers_the_sheet_value_when_provided()
    {
        var action = ControlCenterLegacyMapper.NextAction("PAUSED", null, "Send manuscript to editor", null, null, null);
        Assert.Equal("Send manuscript to editor", action);
    }

    [Theory]
    [InlineData("https://drive.google.com/drive/folders/abc123xyz", "abc123xyz")]
    [InlineData("https://drive.google.com/drive/folders/abc123xyz/", "abc123xyz")]
    [InlineData("not-a-url", null)]
    [InlineData("https://docs.google.com/spreadsheets/d/x", null)]
    [InlineData(null, null)]
    public void TryParseDriveFolderId_pulls_the_id_out_of_a_drive_folder_url(string? url, string? expected)
    {
        Assert.Equal(expected, ControlCenterLegacyMapper.TryParseDriveFolderId(url));
    }

    [Fact]
    public void Normalize_strips_separators_and_uppercases()
    {
        Assert.Equal("BLOCKEDNEEDSHUMAN", ControlCenterLegacyMapper.Normalize("Blocked_Needs_Human "));
        Assert.Equal("READYTOPUBLISH", ControlCenterLegacyMapper.Normalize("ready-to-publish"));
        Assert.Equal(string.Empty, ControlCenterLegacyMapper.Normalize(null));
    }
}