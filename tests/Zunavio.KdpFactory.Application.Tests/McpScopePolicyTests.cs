using System.Security.Claims;
using Zunavio.KdpFactory.Application.Security;

namespace Zunavio.KdpFactory.Application.Tests;

public class McpScopePolicyTests
{
    private static ClaimsPrincipal PrincipalWith(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, "test"));

    [Fact]
    public void ScopesFrom_returns_empty_for_null_principal()
    {
        Assert.Empty(McpScopePolicy.ScopesFrom(null));
    }

    [Fact]
    public void ScopesFrom_splits_both_scope_and_permissions_claims()
    {
        var user = PrincipalWith(
            new Claim("scope", "openid mcp:tools mcp:tools:write"),
            new Claim("permissions", "read:projects"));

        var scopes = McpScopePolicy.ScopesFrom(user).ToHashSet();
        Assert.Contains("mcp:tools", scopes);
        Assert.Contains("mcp:tools:write", scopes);
        Assert.Contains("read:projects", scopes);
        Assert.Contains("openid", scopes);
    }

    [Fact]
    public void ScopesFrom_ignores_unrelated_claims()
    {
        var user = PrincipalWith(new Claim("sub", "tpc_123"));

        Assert.Empty(McpScopePolicy.ScopesFrom(user));
    }

    [Theory]
    [InlineData("mcp:tools", true)]
    [InlineData("mcp:tools:write", false)]
    [InlineData("openid", false)]
    public void HasRequiredScope_requires_the_exact_scope(string required, bool expected)
    {
        var user = PrincipalWith(new Claim("scope", "mcp:tools"));

        Assert.Equal(expected, McpScopePolicy.HasRequiredScope(user, required));
    }

    [Fact]
    public void HasWriteScope_requires_both_read_and_write_scopes()
    {
        Assert.False(McpScopePolicy.HasWriteScope(PrincipalWith(new Claim("scope", "mcp:tools"))));
        Assert.False(McpScopePolicy.HasWriteScope(PrincipalWith(new Claim("scope", "mcp:tools:write"))));
        Assert.False(McpScopePolicy.HasWriteScope(null));

        Assert.True(McpScopePolicy.HasWriteScope(PrincipalWith(new Claim("scope", "mcp:tools mcp:tools:write"))));
    }

    [Fact]
    public void RequiresWriteScope_never_allows_a_token_without_the_write_scope()
    {
        var readToken = PrincipalWith(new Claim("scope", "mcp:tools"));
        var writeToken = PrincipalWith(new Claim("permissions", "mcp:tools mcp:tools:write"));

        Assert.False(McpScopePolicy.RequiresWriteScope(readToken, McpScopePolicy.WriteScope));
        Assert.True(McpScopePolicy.RequiresWriteScope(writeToken, McpScopePolicy.WriteScope));
        Assert.True(McpScopePolicy.RequiresWriteScope(writeToken, McpScopePolicy.ReadScope));
    }

    [Fact]
    public void HasRequiredScope_matches_whole_words_only()
    {
        var scopes = new[] { "mcp:tools" };

        Assert.True(McpScopePolicy.HasRequiredScope(scopes, "mcp:tools"));
        Assert.False(McpScopePolicy.HasRequiredScope(scopes, "tools"));
        Assert.False(McpScopePolicy.HasRequiredScope(scopes, "mcp:tools_extra"));
    }
}