using Zunavio.KdpFactory.Domain.Enums;
using Zunavio.KdpFactory.Domain.Rules;

namespace Zunavio.KdpFactory.Web.Components;

/// <summary>Shared CSS/badge/progress helpers for the dashboard pages.</summary>
public static class WorkflowUi
{
    public static string GateClass(ProjectGate gate) => gate switch
    {
        ProjectGate.Idea => "blue",
        ProjectGate.MarketResearch => "teal",
        ProjectGate.MarketValidation => "violet",
        ProjectGate.Architecture => "cyan",
        ProjectGate.Manuscript => "pink",
        ProjectGate.VisualProduction => "orange",
        ProjectGate.BookProduction => "yellow",
        ProjectGate.Metadata => "blue",
        ProjectGate.Qa => "green",
        ProjectGate.ReadyToPublish => "lime",
        ProjectGate.Published => "ok",
        _ => "blue",
    };

    public static string StatusClass(ProjectStatus status) => status switch
    {
        ProjectStatus.Active => "ok",
        ProjectStatus.Paused => "wait",
        ProjectStatus.Rejected => "bad",
        ProjectStatus.Completed => "blue",
        _ => "muted-badge",
    };

    /// <summary>0..100 progress through the workflow gate chain (Published = 100).</summary>
    public static int GatePercent(ProjectGate gate)
    {
        var chain = WorkflowStateMachine.GateChain;
        var index = WorkflowStateMachine.IndexOf(gate);
        if (index < 0) return 0;
        if (chain.Count <= 1) return 100;
        return index * 100 / (chain.Count - 1);
    }
}