namespace Zunavio.KdpFactory.Application.Abstractions;

/// <summary>
/// Imports a single project row from the Google Sheet
/// "ZUNAVIO KDP CONTROL CENTER" (legacy Projects tab) into PostgreSQL.
///
/// Implementations must be idempotent: a project that already exists is never
/// modified or duplicated, and the spreadsheet itself is never rewritten
/// (its headers and legacy rows stay untouched).
/// </summary>
public interface IControlCenterProjectImportService
{
    /// <param name="projectCode">Legacy code, for example ZNV-005.</param>
    Task<ProjectImportResult> ImportProjectAsync(string projectCode, CancellationToken ct);
}
