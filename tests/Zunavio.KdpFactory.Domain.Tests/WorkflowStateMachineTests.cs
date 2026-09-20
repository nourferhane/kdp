using Zunavio.KdpFactory.Domain.Enums;
using Zunavio.KdpFactory.Domain.Rules;

namespace Zunavio.KdpFactory.Domain.Tests;

public class WorkflowStateMachineTests
{
    [Theory]
    [InlineData(ProjectGate.Idea, ProjectGate.MarketResearch)]
    [InlineData(ProjectGate.MarketResearch, ProjectGate.MarketValidation)]
    [InlineData(ProjectGate.MarketValidation, ProjectGate.Architecture)]
    [InlineData(ProjectGate.Architecture, ProjectGate.Manuscript)]
    [InlineData(ProjectGate.Manuscript, ProjectGate.VisualProduction)]
    [InlineData(ProjectGate.VisualProduction, ProjectGate.BookProduction)]
    [InlineData(ProjectGate.BookProduction, ProjectGate.Metadata)]
    [InlineData(ProjectGate.Metadata, ProjectGate.Qa)]
    [InlineData(ProjectGate.Qa, ProjectGate.ReadyToPublish)]
    [InlineData(ProjectGate.ReadyToPublish, ProjectGate.Published)]
    public void CanTransition_immediate_successor_ok(ProjectGate current, ProjectGate target)
    {
        Assert.True(WorkflowStateMachine.CanTransition(current, target));
        Assert.Equal(target, WorkflowStateMachine.NextGate(current));
    }

    [Theory]
    [InlineData(ProjectGate.Idea, ProjectGate.Manuscript)]
    [InlineData(ProjectGate.MarketResearch, ProjectGate.Qa)]
    [InlineData(ProjectGate.Manuscript, ProjectGate.Published)]
    public void CanTransition_skipping_gate_blocked(ProjectGate current, ProjectGate target)
    {
        Assert.False(WorkflowStateMachine.CanTransition(current, target));
    }

    [Theory]
    [InlineData(ProjectGate.Manuscript, ProjectGate.MarketResearch)]
    [InlineData(ProjectGate.Qa, ProjectGate.Manuscript)]
    public void CanRollback_to_earlier_gate_ok(ProjectGate current, ProjectGate target)
    {
        Assert.True(WorkflowStateMachine.CanRollback(current, target));
    }

    [Fact]
    public void CanRollback_never_into_idea()
    {
        Assert.False(WorkflowStateMachine.CanRollback(ProjectGate.MarketValidation, ProjectGate.Idea));
    }

    [Fact]
    public void CanRollback_never_from_published_or_idea()
    {
        Assert.False(WorkflowStateMachine.CanRollback(ProjectGate.Published, ProjectGate.Qa));
        Assert.False(WorkflowStateMachine.CanRollback(ProjectGate.Idea, ProjectGate.Published));
    }

    [Fact]
    public void CanRollback_never_forward()
    {
        Assert.False(WorkflowStateMachine.CanRollback(ProjectGate.Manuscript, ProjectGate.VisualProduction));
    }

    [Fact]
    public void NextGate_null_at_terminal()
    {
        Assert.Null(WorkflowStateMachine.NextGate(ProjectGate.Published));
    }

    [Fact]
    public void GateChain_is_exact_linear_order()
    {
        var expected = new[]
        {
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
        };
        Assert.Equal(expected, WorkflowStateMachine.GateChain);
    }
}

public class AgentRoutingTests
{
    [Theory]
    [InlineData(ProjectGate.Idea, AgentCode.Scout)]
    [InlineData(ProjectGate.MarketResearch, AgentCode.Scout)]
    [InlineData(ProjectGate.MarketValidation, AgentCode.Validator)]
    [InlineData(ProjectGate.Architecture, AgentCode.Architect)]
    [InlineData(ProjectGate.Manuscript, AgentCode.Writer)]
    [InlineData(ProjectGate.VisualProduction, AgentCode.ArtDirector)]
    [InlineData(ProjectGate.BookProduction, AgentCode.Production)]
    [InlineData(ProjectGate.Metadata, AgentCode.Metadata)]
    [InlineData(ProjectGate.Qa, AgentCode.Qa)]
    [InlineData(ProjectGate.ReadyToPublish, AgentCode.Launch)]
    public void AgentForGate_known_specialists(ProjectGate gate, AgentCode expected)
    {
        Assert.Equal(expected, AgentRouting.AgentForGate(gate));
    }

    [Fact]
    public void IsExecutableGate_excludes_terminal_and_none()
    {
        Assert.False(AgentRouting.IsExecutableGate(ProjectGate.Published));
        Assert.False(AgentRouting.IsExecutableGate(ProjectGate.None));
        Assert.True(AgentRouting.IsExecutableGate(ProjectGate.Manuscript));
    }
}