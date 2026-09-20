namespace Zunavio.KdpFactory.Domain.Enums;

/// <summary>Reason a human approval gate was triggered.</summary>
public enum HumanReviewType
{
    /// <summary>Several validated market concepts; a human must pick the winning concept.</summary>
    ConceptSelection = 1,

    /// <summary>Approval required before expensive or mass image generation.</summary>
    ImageGenerationApproval = 2,

    /// <summary>QA surfaced important unresolved issues that need a human decision.</summary>
    QaUnresolvedIssues = 3,

    /// <summary>Final approval immediately before publication.</summary>
    PrePublication = 4,

    /// <summary>An agent explicitly requested human review.</summary>
    AgentHumanReview = 5,
}