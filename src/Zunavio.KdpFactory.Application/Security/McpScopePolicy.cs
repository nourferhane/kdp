using System.Security.Claims;

namespace Zunavio.KdpFactory.Application.Security;

/// <summary>
/// Scope vocabulary and pure helpers for protecting /mcp tools. Only the BCL
/// Claims abstraction is used so the logic stays unit-testable without ASP.NET.
/// </summary>
public static class McpScopePolicy
{
    /// <summary>Read access to any tool exposed at /mcp.</summary>
    public const string ReadScope = "mcp:tools";

    /// <summary>Write access (import succeeded only when <see cref="ReadScope"/> is also granted).</summary>
    public const string WriteScope = "mcp:tools:write";

    /// <summary>Auth0 offers scopes via both the "scope" and "permissions" claims; check both.</summary>
    public static IEnumerable<string> ScopesFrom(ClaimsPrincipal? user)
    {
        if (user is null)
        {
            return [];
        }

        return user.FindAll("scope")
            .Concat(user.FindAll("permissions"))
            .SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    public static bool HasRequiredScope(ClaimsPrincipal? user, string requiredScope) =>
        HasRequiredScope(ScopesFrom(user), requiredScope);

    public static bool HasWriteScope(ClaimsPrincipal? user) =>
        HasRequiredScope(user, ReadScope) && HasRequiredScope(user, WriteScope);

    public static bool HasRequiredScope(IEnumerable<string> scopes, string requiredScope) =>
        scopes.Any(s => string.Equals(s, requiredScope, StringComparison.Ordinal));

    /// <summary>Tolerant check used by tool policies: a token actually used for a
    /// write operation must carry mcp:tools:write regardless of how it got there.</summary>
    public static bool RequiresWriteScope(ClaimsPrincipal? user, string requiredScope) =>
        requiredScope == WriteScope ? HasWriteScope(user) : HasRequiredScope(user, requiredScope);
}