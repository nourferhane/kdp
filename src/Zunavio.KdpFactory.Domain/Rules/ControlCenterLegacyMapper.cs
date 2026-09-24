using Zunavio.KdpFactory.Domain.Enums;

namespace Zunavio.KdpFactory.Domain.Rules;

/// <summary>
/// Maps legacy "ZUNAVIO KDP CONTROL CENTER" sheet cells onto the PostgreSQL
/// workflow state without ever modifying the sheet itself.
///
/// The legacy Projects tab uses two free-form text columns: <c>Current_Gate</c>
/// and <c>Status</c>. <c>Current_Gate</c> normally holds a gate
/// (<c>MARKET_RESEARCH</c>, <c>READY_TO_PUBLISH</c>, ...) but some rows carry a
/// terminal state instead (for example ZNV-005 has <c>Current_Gate = REJECTED</c>).
/// Those values are not gates, so they are folded into <see cref="ProjectStatus"/>
/// and the display gate is inferred from the version columns.
/// </summary>
public static class ControlCenterLegacyMapper
{
    /// <summary>
    /// State words that may appear in <c>Current_Gate</c> and are NOT gates.
    /// Gate-like words (<c>READY_TO_PUBLISH</c>, <c>PUBLISHED</c>) are excluded:
    /// they stay gates and the <c>Status</c> column keeps authority over them.
    /// </summary>
    private static readonly HashSet<string> NonGateStateKeywords =
    [
        "PAUSED", "BLOCKED", "BLOCKEDNEEDSHUMAN", "BLOCKEDNEEDSHUMANREVIEW",
        "ONHOLD", "HOLD", "REJECTED", "REJECT", "CANCELLED",
        "COMPLETED", "COMPLETE", "DONE", "FINISHED", "TERMINATED",
    ];

    /// <summary>Upper-cases a cell and drops every separator (_ - space).</summary>
    public static string Normalize(string? value) =>
        string.IsNullOrEmpty(value)
            ? string.Empty
            : new string(value.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

    /// <summary>
    /// Parses a legacy gate cell. Returns null when the cell holds a state word
    /// (REJECTED, PAUSED, ...) or an unknown value.
    /// </summary>
    public static ProjectGate? TryParseGate(string? value)
    {
        var normalized = Normalize(value);
        if (normalized.Length == 0)
        {
            return null;
        }

        foreach (var gate in WorkflowStateMachine.GateChain)
        {
            if (Normalize(gate.ToString()) == normalized)
            {
                return gate;
            }
        }

        return null;
    }

    /// <summary>Parses any known legacy state word (Status column vocabulary).</summary>
    public static ProjectStatus? TryParseStatus(string? value)
    {
        var normalized = Normalize(value);
        if (normalized.Length == 0)
        {
            return null;
        }

        if (normalized is "PAUSED" or "BLOCKED" or "BLOCKEDNEEDSHUMAN"
            or "BLOCKEDNEEDSHUMANREVIEW" or "ONHOLD" or "HOLD")
        {
            return ProjectStatus.Paused;
        }

        if (normalized is "REJECTED" or "REJECT" or "CANCELLED")
        {
            return ProjectStatus.Rejected;
        }

        if (normalized is "COMPLETED" or "COMPLETE" or "DONE" or "FINISHED"
            or "TERMINATED" or "READYTOPUBLISH" or "PUBLISHED")
        {
            return ProjectStatus.Completed;
        }

        if (normalized is "ACTIVE" or "RUNNING" or "RUNNINGAUTONOMOUS"
            or "INPROGRESS" or "ONTRACK" or "QUEUED")
        {
            return ProjectStatus.Active;
        }

        return null;
    }

    /// <summary>
    /// Resolves the project status. A non-gate state word in <c>Current_Gate</c>
    /// (REJECTED, PAUSED, ...) wins over the Status column; otherwise the Status
    /// column decides. Returns null when neither cell is recognised.
    /// </summary>
    public static ProjectStatus? MapStatus(string? currentGate, string? status)
    {
        var gateWord = Normalize(currentGate);
        if (NonGateStateKeywords.Contains(gateWord))
        {
            return TryParseStatus(gateWord);
        }

        return TryParseStatus(status);
    }

    /// <summary>
    /// Infers the workflow gate a legacy row sits on from its version columns.
    /// Used when <c>Current_Gate</c> carries a state word or an unknown value.
    /// </summary>
    public static ProjectGate InferGate(
        string? manuscriptVersion, string? visualBibleVersion, string? productionVersion)
    {
        if (!string.IsNullOrWhiteSpace(productionVersion) || !string.IsNullOrWhiteSpace(visualBibleVersion))
        {
            return ProjectGate.VisualProduction;
        }

        if (!string.IsNullOrWhiteSpace(manuscriptVersion))
        {
            return ProjectGate.Manuscript;
        }

        return ProjectGate.Idea;
    }

    /// <summary>
    /// Full state mapping of a legacy row. Never fails: unknown cells fall back
    /// to Idea + Active so an import can not be blocked by an unexpected value.
    /// </summary>
    public static (ProjectGate Gate, ProjectStatus Status) MapState(
        string? currentGate,
        string? status,
        string? manuscriptVersion,
        string? visualBibleVersion,
        string? productionVersion)
    {
        var gate = TryParseGate(currentGate)
            ?? InferGate(manuscriptVersion, visualBibleVersion, productionVersion);

        var projectStatus = MapStatus(currentGate, status) ?? ProjectStatus.Active;
        return (gate, projectStatus);
    }

    /// <summary>
    /// Next action for an imported project: the sheet value wins when present,
    /// otherwise the deterministic gate/status label is used.
    /// </summary>
    public static string NextAction(
        string? currentGate,
        string? status,
        string? sheetNextAction,
        string? manuscriptVersion,
        string? visualBibleVersion,
        string? productionVersion)
    {
        if (!string.IsNullOrWhiteSpace(sheetNextAction))
        {
            return sheetNextAction.Trim();
        }

        var (gate, projectStatus) = MapState(
            currentGate, status, manuscriptVersion, visualBibleVersion, productionVersion);

        return NextAction(gate, projectStatus) ?? string.Empty;
    }

    /// <summary>Deterministic next-action label for a resolved gate/status pair.</summary>
    public static string? NextAction(ProjectGate gate, ProjectStatus status)
    {
        if (status == ProjectStatus.Paused) return "PAUSED";
        if (status == ProjectStatus.Rejected) return "REJECTED";
        if (status == ProjectStatus.Completed) return "COMPLETED";
        if (!AgentRouting.IsExecutableGate(gate)) return null;

        return $"RUN_{AgentRouting.AgentForGate(gate).ToString().ToUpperInvariant()}";
    }

    /// <summary>Extracts a Drive folder id from a Project_Folder_URL cell.</summary>
    public static string? TryParseDriveFolderId(string? folderUrl)
    {
        if (string.IsNullOrWhiteSpace(folderUrl))
        {
            return null;
        }

        if (Uri.TryCreate(folderUrl, UriKind.Absolute, out var uri)
            && uri.Host == "drive.google.com"
            && uri.AbsolutePath.StartsWith("/drive/folders/", StringComparison.Ordinal))
        {
            return uri.AbsolutePath["/drive/folders/".Length..].Trim('/');
        }

        return null;
    }
}
