using Zunavio.KdpFactory.Domain.Enums;

namespace Zunavio.KdpFactory.Domain.Rules;

/// <summary>
/// The deterministic KDP workflow state machine (section 3).
///
/// The exact linear chain of gates is the only legal forward path.
/// Agents may never skip a gate; a gate may only advance to its immediate
/// successor. Rollbacks are only allowed to a strictly earlier gate and
/// are never allowed out of PUBLISHED or back into IDEA.
/// </summary>
public static class WorkflowStateMachine
{
    public static readonly IReadOnlyList<ProjectGate> GateChain =
    [
        ProjectGate.Idea,
        ProjectGate.MarketResearch,
        ProjectGate.MarketValidation,
        ProjectGate.Architecture,
        ProjectGate.Manuscript,
        ProjectGate.VisualProduction,
        ProjectGate.BookProduction,
        ProjectGate.Metadata,
        ProjectGate.Qa,
        ProjectGate.ReadyToPublish,
        ProjectGate.Published,
    ];

    public static ProjectGate? NextGate(ProjectGate current)
    {
        var index = IndexOf(current);
        if (index < 0 || index >= GateChain.Count - 1) return null;
        return GateChain[index + 1];
    }

    public static ProjectGate? PreviousGate(ProjectGate current)
    {
        var index = IndexOf(current);
        if (index <= 0) return null;
        return GateChain[index - 1];
    }

    /// <summary>
    /// A forward transition is legal only when target is the exact successor of current.
    /// </summary>
    public static bool CanTransition(ProjectGate current, ProjectGate target)
    {
        if (current == ProjectGate.Published) return false;
        return NextGate(current) == target;
    }

    /// <summary>
    /// A rollback is legal from any non-terminal gate to any strictly earlier gate.
    /// You cannot roll back into IDEA (the project already exists) and you cannot
    /// roll back a PUBLISHED project.
    /// </summary>
    public static bool CanRollback(ProjectGate current, ProjectGate target)
    {
        if (current == ProjectGate.Published || current == ProjectGate.Idea) return false;
        if (target == ProjectGate.None || target == ProjectGate.Idea) return false;

        var currentIndex = IndexOf(current);
        var targetIndex = IndexOf(target);
        return currentIndex > 0 && targetIndex > 0 && targetIndex < currentIndex;
    }

    public static int IndexOf(ProjectGate gate) => GateChain.ToList().IndexOf(gate);

    public static bool IsTerminal(ProjectGate gate) => gate is ProjectGate.Published or ProjectGate.None;
}