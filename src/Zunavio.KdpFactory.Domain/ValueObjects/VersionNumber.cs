using System.Globalization;

namespace Zunavio.KdpFactory.Domain.ValueObjects;

/// <summary>
/// Semantic asset version following the rules of section 17:
/// drafts v0.1..v0.9, first approval v1.0, revised approvals v1.1, v1.2,
/// major reset v2.0. An approved asset is never overwritten: new versions
/// are always created.
/// </summary>
public readonly record struct VersionNumber
{
    public int Major { get; }
    public int Minor { get; }

    public VersionNumber(int major, int minor)
    {
        if (major < 0) throw new ArgumentOutOfRangeException(nameof(major), "Major version cannot be negative.");
        if (minor < 0) throw new ArgumentOutOfRangeException(nameof(minor), "Minor version cannot be negative.");
        Major = major;
        Minor = minor;
    }

    public static VersionNumber V0_1 => new(0, 1);
    public static VersionNumber V1_0 => new(1, 0);

    public bool IsDraft => Major == 0;

    public override string ToString() => $"v{Major}.{Minor}";

    /// <summary>Parses "v1.2" or "1.2".</summary>
    public static VersionNumber Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Version string is required.", nameof(value));

        var trimmed = value.Trim().TrimStart('v', 'V');
        var parts = trimmed.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is 0 or > 2
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major)
            || !int.TryParse(parts.Length == 2 ? parts[1] : "0", NumberStyles.None, CultureInfo.InvariantCulture, out var minor))
        {
            throw new FormatException($"'{value}' is not a valid version. Expected format like 'v1.0'.");
        }

        return new VersionNumber(major, minor);
    }

    public static bool TryParse(string? value, out VersionNumber version)
    {
        try
        {
            version = Parse(value);
            return true;
        }
        catch (Exception)
        {
            version = default;
            return false;
        }
    }

    /// <summary>Next draft revision: v0.2, v0.3 ... </summary>
    public VersionNumber NextDraft() => new(0, Minor + 1);

    /// <summary>First draft of a new major cycle: v0.1.</summary>
    public VersionNumber FirstDraft() => V0_1;

    /// <summary>Revision of an already approved version: v1.1, v1.2 ...</summary>
    public VersionNumber NextApprovedRevision() => new(Major, Minor + 1);

    /// <summary>Major reset: v2.0.</summary>
    public VersionNumber MajorReset() => new(Major + 1, 0);

    /// <summary>Promotes any draft to its first approved state v1.0.</summary>
    public VersionNumber PromoteToApproved() => V1_0;

    /// <summary>
    /// Produces the next version after this one, given the current production state
    /// (used when an existing asset must be superseded).
    /// </summary>
    public VersionNumber NextFor(AssetProductionState state) => state switch
    {
        AssetProductionState.Approved => NextApprovedRevision(),
        AssetProductionState.ExistingApprovedReplacing => MajorReset(),
        _ => NextDraft(),
    };
}

public enum AssetProductionState
{
    Draft,
    Approved,
    ExistingApprovedReplacing,
}