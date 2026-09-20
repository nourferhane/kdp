using Zunavio.KdpFactory.Domain.Entities;
using Zunavio.KdpFactory.Domain.Enums;

namespace Zunavio.KdpFactory.Application.Services;

/// <summary>Generates deterministic human-readable codes (section 6/7).</summary>
public interface ICodeGenerator
{
    Task<string> NextProjectCodeAsync(CancellationToken cancellationToken);
    string RunCode(string projectCode, int sequence);
    string AssetCode(string projectCode, AssetType assetType, string version);
}

public sealed class CodeGenerator : ICodeGenerator
{
    public const string ProjectPrefix = "ZNV";

    private readonly Func<CancellationToken, Task<int>> _projectSequenceReader;

    public CodeGenerator(Func<CancellationToken, Task<int>> projectSequenceReader)
    {
        _projectSequenceReader = projectSequenceReader;
    }

    public async Task<string> NextProjectCodeAsync(CancellationToken cancellationToken)
    {
        var nextSequence = await _projectSequenceReader(cancellationToken);
        return $"{ProjectPrefix}-{nextSequence:D3}";
    }

    public string RunCode(string projectCode, int sequence) => $"RUN-{projectCode}-{sequence:D3}";

    public string AssetCode(string projectCode, AssetType assetType, string version)
    {
        var type = Enum.GetName(assetType)!.ToUpperInvariant();
        return $"{projectCode}_{type}_{version}";
    }
}