using Zunavio.KdpFactory.Domain.Entities;
using Zunavio.KdpFactory.Domain.Enums;
using Zunavio.KdpFactory.Domain.Rules;
using Zunavio.KdpFactory.Web.Mcp;

namespace Zunavio.KdpFactory.IntegrationTests;

public sealed class ZunavioMarketDecisionGuardTests
{
    [Fact]
    public void ScoutRecommendation_RequiresTheValidatorHoldState()
    {
        var project = new Project
        {
            CurrentGate = ProjectGate.MarketResearch,
            Status = ProjectStatus.Active,
            NextAction = "RUN_SCOUT_TARGETED_EVIDENCE",
            QaResult = "VALIDATOR_HOLD"
        };
        Assert.True(ZunavioMarketDecisionTools.Allowed(
            project, "RUN_SCOUT_TARGETED_EVIDENCE", "VALIDATOR_HOLD"));
        project.NextAction = "RUN_VALIDATOR";
        Assert.False(ZunavioMarketDecisionTools.Allowed(
            project, "RUN_SCOUT_TARGETED_EVIDENCE", "VALIDATOR_HOLD"));
    }

    [Fact]
    public void FinalReview_RequiresPriorScoutRecommendationAndActiveResearch()
    {
        var project = new Project
        {
            CurrentGate = ProjectGate.MarketResearch,
            Status = ProjectStatus.Active,
            NextAction = "REVIEW_SCOUT_DECISION",
            QaResult = "VALIDATOR_HOLD"
        };

        Assert.False(ZunavioMarketDecisionTools.Allowed(
            project, "REVIEW_SCOUT_DECISION", "SCOUT_REJECTION_RECOMMENDED"));

        project.QaResult = "SCOUT_REJECTION_RECOMMENDED";
        Assert.True(ZunavioMarketDecisionTools.Allowed(
            project, "REVIEW_SCOUT_DECISION", "SCOUT_REJECTION_RECOMMENDED"));

        project.Status = ProjectStatus.Rejected;
        Assert.False(ZunavioMarketDecisionTools.Allowed(
            project, "REVIEW_SCOUT_DECISION", "SCOUT_REJECTION_RECOMMENDED"));
    }

    [Fact]
    public void ArchivedLegacyRow_RemainsRejectedOnReadback()
    {
        Assert.Equal(ProjectStatus.Rejected,
            ControlCenterLegacyMapper.MapStatus("REJECTED", "ARCHIVED"));
    }
}
