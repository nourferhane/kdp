using Zunavio.KdpFactory.Domain.Enums;
using Zunavio.KdpFactory.Domain.Rules;

namespace Zunavio.KdpFactory.Application.Orchestration;

/// <summary>
/// Level-1 deterministic validation of gate transitions. The AI Orchestrator
/// may recommend anything — only these rules decide what is actually legal.
/// </summary>
public interface IWorkflowTransitionValidator
{
    bool CanTransition(ProjectGate current, ProjectGate target);
    bool CanRollback(ProjectGate current, ProjectGate target);
}

public sealed class WorkflowTransitionValidator : IWorkflowTransitionValidator
{
    public bool CanTransition(ProjectGate current, ProjectGate target) =>
        WorkflowStateMachine.CanTransition(current, target);

    public bool CanRollback(ProjectGate current, ProjectGate target) =>
        WorkflowStateMachine.CanRollback(current, target);
}