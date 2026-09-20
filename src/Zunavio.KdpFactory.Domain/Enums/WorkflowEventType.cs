namespace Zunavio.KdpFactory.Domain.Enums;

/// <summary>Events recorded on the workflow timeline.</summary>
public enum WorkflowEventType
{
    ProjectCreated = 1,
    AgentStarted = 2,
    AgentCompleted = 3,
    AgentFailed = 4,
    GateAdvanced = 5,
    GateRolledBack = 6,
    HumanReviewRequested = 7,
    HumanReviewApproved = 8,
    HumanReviewRejected = 9,
    HumanReviewCancelled = 10,
    AssetCreated = 11,
    ProjectPaused = 12,
    ProjectResumed = 13,
    ProjectRejected = 14,
    ProjectReadyToPublish = 15,
    ProjectPublished = 16,
    RunRetried = 17,
}