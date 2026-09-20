using Zunavio.KdpFactory.Domain.ValueObjects;

namespace Zunavio.KdpFactory.Application.Services;

/// <summary>
/// Versioning policy for assets (section 17). Never overwrites an approved asset:
/// a replacement always gets a new version number.
/// </summary>
public interface IVersionService
{
    /// <summary>Next draft number given the current latest version: v0.1, v0.2 ...</summary>
    VersionNumber NextDraft(string? currentLatest);

    /// <summary>First approved version (v1.0) or the next revision (v1.1, v1.2) of an approved asset.</summary>
    VersionNumber NextApproved(string? currentLatest);

    /// <summary>Major reset producing v2.0, v3.0 ...</summary>
    VersionNumber MajorReset(string? currentLatest);

    bool IsApproved(string versusNumber) => VersionNumber.TryParse(versusNumber, out var v) && !v.IsDraft;
}

public sealed class VersionService : IVersionService
{
    public VersionNumber NextDraft(string? currentLatest)
    {
        if (!VersionNumber.TryParse(currentLatest, out var current) || current.Major == 0)
            return VersionNumber.V0_1;
        return V0_1After(current);
    }

    public VersionNumber NextApproved(string? currentLatest)
    {
        if (!VersionNumber.TryParse(currentLatest, out var current) || current.IsDraft)
            return VersionNumber.V1_0;

        return current switch
        {
            { Major: 1, Minor: >= 0 } => current.NextApprovedRevision(),
            { Major: > 1 } => new VersionNumber(current.Major, current.Minor + 1),
            _ => VersionNumber.V1_0,
        };
    }

    public VersionNumber MajorReset(string? currentLatest)
    {
        if (!VersionNumber.TryParse(currentLatest, out var current)) return new VersionNumber(2, 0);
        return current.MajorReset();
    }

    private static VersionNumber V0_1After(VersionNumber approved)
    {
        // A rewritten draft cycle always starts again at v0.1; the number alone is
        // scoped by the asset it applies to.
        return VersionNumber.V0_1;
    }
}