namespace Zunavio.KdpFactory.Web.Mcp;

/// <summary>Policy and scheme names used to protect the /mcp endpoint.</summary>
public static class McpSecurity
{
    /// <summary>Applied to the /mcp endpoint: accepts a Bearer token or the MCP handler.</summary>
    public const string McpEndpointPolicy = "McpEndpoint";

    /// <summary>Read access to every MCP tool (needs the mcp:tools scope).</summary>
    public const string McpToolsPolicy = "McpTools";

    /// <summary>Write access to mutating MCP tools (needs + mcp:tools:write).</summary>
    public const string McpWritePolicy = "McpWrite";

    public const string BearerScheme = "Bearer";
    public const string McpAuthScheme = "McpAuth";
}