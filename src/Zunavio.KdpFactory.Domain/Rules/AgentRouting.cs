using Zunavio.KdpFactory.Domain.Enums;

namespace Zunavio.KdpFactory.Domain.Rules;

/// <summary>
/// Determines which specialist agent is responsible for a gate.
/// This is deterministic routing and is the ONLY way a gate maps to an agent.
/// (section 3 "Routing")
/// </summary>
public static class AgentRouting
{
    public static AgentCode AgentForGate(ProjectGate gate) => gate switch
    {
        ProjectGate.Idea => AgentCode.Scout,
        ProjectGate.MarketResearch => AgentCode.Scout,
        ProjectGate.MarketValidation => AgentCode.Validator,
        ProjectGate.Architecture => AgentCode.Architect,
        ProjectGate.Manuscript => AgentCode.Writer,
        ProjectGate.VisualProduction => AgentCode.ArtDirector,
        ProjectGate.BookProduction => AgentCode.Production,
        ProjectGate.Metadata => AgentCode.Metadata,
        ProjectGate.Qa => AgentCode.Qa,
        ProjectGate.ReadyToPublish => AgentCode.Launch,
        _ => throw new ArgumentOutOfRangeException(nameof(gate), $"Gate '{gate}' cannot be executed by any specialist agent."),
    };

    /// <summary>Returns true when the gate is directly executable by a specialist agent.</summary>
    public static bool IsExecutableGate(ProjectGate gate) =>
        gate is not ProjectGate.None and not ProjectGate.Published;
}