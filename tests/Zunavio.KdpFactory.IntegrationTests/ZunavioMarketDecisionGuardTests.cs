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

    [Theory]
    [InlineData("SCOUT_IN_PROGRESS")]
    [InlineData("RESEARCH_IN_PROGRESS")]
    public void ScoutRecommendation_AcceptsInitialResearchButNotArbitraryAction(string qaResult)
    {
        var project = new Project
        {
            CurrentGate = ProjectGate.MarketResearch,
            Status = ProjectStatus.Active,
            NextAction = "RUN_SCOUT_CONTINUE",
            QaResult = qaResult
        };

        Assert.True(ZunavioMarketDecisionTools.IsScoutRecommendationState(project));
        project.NextAction = "RUN_VALIDATOR";
        Assert.False(ZunavioMarketDecisionTools.IsScoutRecommendationState(project));
        project.NextAction = "RUN_SCOUT_CONTINUE";
        project.Status = ProjectStatus.Paused;
        Assert.False(ZunavioMarketDecisionTools.IsScoutRecommendationState(project));
    }

    [Fact]
    public void ScoutRecommendation_RecognizesEnglishDecisionAndRejectsUnrelatedText()
    {
        Assert.True(ZunavioMarketDecisionTools.ContainsScoutRejectionRecommendation(
            "Decision: REJECTION RECOMMENDED; Orchestrator review required."));
        Assert.False(ZunavioMarketDecisionTools.ContainsScoutRejectionRecommendation(
            "Decision: CONTINUE_RESEARCH. Revisit competitors later."));
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
