namespace Zunavio.KdpFactory.Domain.Enums;

/// <summary>Decision an orchestrator can apply to a project after an agent run.</summary>
public enum OrchestratorDecisionKind
{
    None = 0,
    AdvanceGate = 1,
    Rollback = 2,
    Pause = 3,
    Reject = 4,
    HumanReview = 5,
}