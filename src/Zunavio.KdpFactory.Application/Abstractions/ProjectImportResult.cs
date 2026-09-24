using Zunavio.KdpFactory.Domain.Enums;

namespace Zunavio.KdpFactory.Application.Abstractions;

/// <summary>
/// Result of importing one legacy Control Center project into PostgreSQL.
/// Shared by the REST route and the MCP tool so both report the same shape.
/// </summary>
public sealed record ProjectImportResult
{
    public bool Success { get; init; }

    /// <summary>Stable machine-readable failure code when <see cref="Success"/> is false.</summary>
    public string? ErrorCode { get; init; }

    /// <summary>Human-readable detail (offending cell value, missing row, ...).</summary>
    public string? ErrorDetail { get; init; }

    /// <summary>False when the project already existed (idempotent replay).</summary>
    public bool Created { get; init; }

    public string ProjectCode { get; init; } = string.Empty;
    public Guid? ProjectId { get; init; }

    public ProjectGate? Gate { get; init; }
    public ProjectStatus? DatabaseStatus { get; init; }

    /// <summary>Raw Status cell as it appeared in the sheet.</summary>
    public string? SourceStatus { get; init; }

    /// <summary>Raw Current_Gate cell as it appeared in the sheet.</summary>
    public string? SourceGate { get; init; }

    public string? NextAction { get; init; }

    public static ProjectImportResult Fail(string code, string projectCode, string? detail = null) => new()
    {
        Success = false,
        ErrorCode = code,
        ErrorDetail = detail,
        ProjectCode = projectCode,
    };
}
