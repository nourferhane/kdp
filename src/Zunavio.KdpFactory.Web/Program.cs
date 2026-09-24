using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.AspNetCore.Authentication;
using ModelContextProtocol.Authentication;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Zunavio.KdpFactory.Application;
using Zunavio.KdpFactory.Application.Security;
using Zunavio.KdpFactory.Infrastructure;
using Zunavio.KdpFactory.Infrastructure.Configuration;
using Zunavio.KdpFactory.Infrastructure.Health;
using Zunavio.KdpFactory.Web.Mcp;

var builder = WebApplication.CreateBuilder(args);
var environment = builder.Environment;

// ---- Platform port support (Koyeb/Railway/Heroku inject PORT) ----
// When PORT is provided, bind to it on all interfaces; otherwise fall back to
// ASPNETCORE_URLS (the Dockerfile defaults to http://+:8080).
if (int.TryParse(Environment.GetEnvironmentVariable("PORT"), out var platformPort))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{platformPort}");
}

var auth0Domain = builder.Configuration[KdpSettings.Auth0DomainEnvKey]?.Trim().TrimEnd('/');
var auth0Audience = builder.Configuration[KdpSettings.Auth0AudienceEnvKey]?.Trim();

// ---- Fail fast: admin credentials and OAuth issuer are required in production ----
if (environment.IsProduction())
{
    var adminUser = builder.Configuration[KdpSettings.AdminUsernameEnvKey];
    var adminPassword = builder.Configuration[KdpSettings.AdminPasswordEnvKey];
    if (string.IsNullOrWhiteSpace(adminUser) || string.IsNullOrWhiteSpace(adminPassword))
    {
        throw new InvalidOperationException(
            $"Refusing to start in Production: {KdpSettings.AdminUsernameEnvKey} and {KdpSettings.AdminPasswordEnvKey} must be set.");
    }

    if (string.IsNullOrWhiteSpace(auth0Domain) || string.IsNullOrWhiteSpace(auth0Audience))
    {
        throw new InvalidOperationException(
            $"Refusing to start in Production: {KdpSettings.Auth0DomainEnvKey} and {KdpSettings.Auth0AudienceEnvKey} must be set " +
            "so the /mcp endpoint can validate tokens.");
    }
}

builder.Services
    .AddKdpInfrastructure(builder.Configuration, environment)
    .AddKdpApplication();

builder.Services.Configure<JsonOptions>(o => o.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase);

// ---- Authentication ----
// Cookie stays the default scheme for the Blazor dashboard. The /mcp endpoint
// opts in to McpAuth (which forwards to Bearer/JWT validation) via its policy,
// so REST, dashboard and the ChatGPT connector each authenticate separately.
var authBuilder = builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/login";
        o.AccessDeniedPath = "/login";
        o.Cookie.Name = "kdp_factory_auth";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.Cookie.SecurePolicy = environment.IsProduction() ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
        o.Cookie.IsEssential = true;
    });

if (string.IsNullOrWhiteSpace(auth0Domain) || string.IsNullOrWhiteSpace(auth0Audience))
{
    // Development conveniences only: /mcp stays protected (401) but no real
    // OAuth server is reachable. Production refuses to start without Auth0.
    authBuilder.AddScheme<AuthenticationSchemeOptions, DevTokenAuthenticationHandler>(
        McpSecurity.BearerScheme, "Dev-only placeholder - AUTH0_* not configured", _ => { });
    authBuilder.AddMcp();
}
else
{
    authBuilder
        .AddJwtBearer(McpSecurity.BearerScheme, o =>
        {
            o.Authority = $"https://{auth0Domain}/";
            o.Audience = auth0Audience;
            o.MapInboundClaims = false;
            o.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
            {
                NameClaimType = "sub",
                RoleClaimType = "role",
            };
        })
        .AddMcp(o =>
        {
            o.ResourceMetadata = new ProtectedResourceMetadata
            {
                AuthorizationServers = [$"https://{auth0Domain}/"],
                BearerMethodsSupported = ["header"],
                ScopesSupported = [McpScopePolicy.ReadScope, McpScopePolicy.WriteScope],
                ResourceName = "ZUNAVIO KDP Factory MCP",
            };
        });
}

builder.Services.AddAuthorization(o =>
{
    o.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();

    // /mcp endpoint: authenticate through McpAuth, which forwards to Bearer for
    // JWT validation and serves the WWW-Authenticate resource_metadata challenge.
    o.AddPolicy(McpSecurity.McpEndpointPolicy, p => p
        .AddAuthenticationSchemes(McpAuthenticationDefaults.AuthenticationScheme)
        .RequireAuthenticatedUser());

    // Read access to any MCP tool.
    o.AddPolicy(McpSecurity.McpToolsPolicy, p => p
        .RequireAuthenticatedUser()
        .RequireAssertion(c => McpScopePolicy.HasRequiredScope(c.User, McpScopePolicy.ReadScope)));

    // Write access; requires mcp:tools:write. Combined (AND) with McpTools when a
    // method also carries [Authorize(Policy = McpSecurity.McpToolsPolicy)].
    o.AddPolicy(McpSecurity.McpWritePolicy, p => p
        .RequireAuthenticatedUser()
        .RequireAssertion(c => McpScopePolicy.HasWriteScope(c.User)));
});
builder.Services.AddCascadingAuthenticationState();

// ---- MCP ----
builder.Services.AddMcpServer()
    .WithHttpTransport(o => o.SessionMode = HttpServerSessionMode.Stateless)
    .AddAuthorizationFilters()
    .WithToolsFromAssembly();

// ---- REST API ----
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ---- Health (includes a real PostgreSQL readiness probe) ----
builder.Services.AddHealthChecks().AddCheck<KdpDatabaseHealthCheck>("postgres");

// ---- Blazor Server dashboard ----
builder.Services.AddRazorComponents().AddInteractiveServerComponents();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(o => o.SwaggerEndpoint("/swagger/v1/swagger.json", "ZUNAVIO KDP Factory API v1"));
}

// ---- Forwarded headers (Koyeb reverse proxy): MUST run before https/auth ----
var forwardedHeadersOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
    ForwardLimit = 5,
};
if (app.Environment.IsProduction())
{
    // The platform proxy is NOT a loopback peer, so the default loopback-only
    // trust would silently drop its headers. Honor the proxy in production.
    forwardedHeadersOptions.KnownNetworks.Clear();
    forwardedHeadersOptions.KnownProxies.Clear();
}
app.UseForwardedHeaders(forwardedHeadersOptions);

// Serve wwwroot assets (app.css, images, etc.). Without this the Blazor UI renders
// as unstyled HTML in production.
app.UseStaticFiles();

app.UseHttpsRedirection();
app.UseAntiforgery();
app.UseAuthentication();

// The Streamable HTTP transport is POST-only in Stateless mode. ChatGPT's
// connector probes the transport URL with an unauthenticated GET first; the
// dashboard cookie fallback would otherwise answer 302 -> /login and the
// connector reports "Service not found". Answer 405 (Allow: POST) so clients
// fall back to POST; the OAuth discovery is already handled by McpAuth above.
app.UseWhen(
    ctx => ctx.Request.Path.Equals("/mcp", StringComparison.OrdinalIgnoreCase)
        && (HttpMethods.IsGet(ctx.Request.Method) || HttpMethods.IsHead(ctx.Request.Method)),
    branch => branch.Run(async context =>
    {
        context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
        context.Response.Headers.Allow = HttpMethods.Post;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(
            """{"jsonrpc":"2.0","id":null,"error":{"code":-32601,"message":"Method not allowed: this MCP endpoint supports POST only."}}""");
    }));

app.UseAuthorization();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = _ => true,
    ResponseWriter = async (ctx, report) =>
    {
        // Never echo exception message bodies from individual checks to a
        // public probe endpoint; only statuses and check names.
        ctx.Response.ContentType = "application/json; charset=utf-8";
        var checks = string.Join(", ", report.Entries.Select(e => $"{e.Key}: {e.Value.Status}"));
        await ctx.Response.WriteAsync($"{{\"status\":\"{report.Status}\",\"checks\":[{string.Join(",", report.Entries.Select(e => $"\"{e.Key}\""))}],\"detail\":\"{checks}\"}}");
    },
}).AllowAnonymous();

// ---- Auth endpoints (login / logout) ----
app.MapPost("/auth/login", async (HttpContext ctx, IOptionsSnapshot<AdminOptions> admin) =>
{
    var form = await ctx.Request.ReadFormAsync(ctx.RequestAborted);
    var username = form["username"].ToString().Trim();
    var password = form["password"].ToString();
    var returnUrl = form["returnUrl"].ToString();

    var options = admin.Value;
    var valid = options.Enabled
        && !string.IsNullOrEmpty(username)
        && password is not null
        && SecureEquals(username, options.Username!)
        && SecureEquals(password, options.Password!);

    if (!valid)
    {
        return Results.Redirect("/login?error=invalid");
    }

    var identity = new ClaimsIdentity(
        [new Claim(ClaimTypes.Name, username)],
        CookieAuthenticationDefaults.AuthenticationScheme);
    await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

    return Results.Redirect(IsLocalUrl(returnUrl) ? returnUrl : "/");
}).AllowAnonymous();

app.MapPost("/auth/logout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login");
});

app.MapControllers();
// ChatGPT connector discovery must reach the MCP transport without the dashboard
// cookie: /mcp requires an OAuth Bearer token (McpAuth -> JwtBearer/Auth0). The
// McpAuth request-handler also answers /.well-known/oauth-protected-resource/*.
// Write/upload REST endpoints remain separately protected by their API-key controller.
app.MapMcp("/mcp").RequireAuthorization(McpSecurity.McpEndpointPolicy);

// RFC 8414 authorization-server metadata, served on THIS host. The ChatGPT
// connector probes /.well-known/oauth-authorization-server (and the OIDC alias)
// before starting OAuth; the dashboard cookie fallback answered 302 -> /login and
// ChatGPT reported "Service not found". Mirror Auth0's endpoints so discovery
// completes on the authz-server host while DCR still hits Auth0's /oidc/register.
if (!string.IsNullOrWhiteSpace(auth0Domain))
{
    Dictionary<string, object?> AuthServerMetadata() => new()
    {
        ["issuer"] = $"https://{auth0Domain}/",
        ["authorization_endpoint"] = $"https://{auth0Domain}/authorize",
        ["token_endpoint"] = $"https://{auth0Domain}/oauth/token",
        ["userinfo_endpoint"] = $"https://{auth0Domain}/userinfo",
        ["jwks_uri"] = $"https://{auth0Domain}/.well-known/jwks.json",
        ["registration_endpoint"] = $"https://{auth0Domain}/oidc/register",
        ["revocation_endpoint"] = $"https://{auth0Domain}/oauth/revoke",
        ["scopes_supported"] = new[]
        {
            "openid", "profile", "offline_access",
            McpScopePolicy.ReadScope, McpScopePolicy.WriteScope,
        },
        ["response_types_supported"] = new[] { "code" },
        ["response_modes_supported"] = new[] { "query", "form_post" },
        ["grant_types_supported"] = new[]
        {
            "authorization_code", "refresh_token",
        },
        ["token_endpoint_auth_methods_supported"] = new[]
        {
            "none", "private_key_jwt", "client_secret_basic", "client_secret_post",
            "urn:ietf:params:oauth:client-assertion-type:jwt-bearer",
        },
        ["code_challenge_methods_supported"] = new[] { "S256", "plain" },
        ["authorization_response_iss_parameter_supported"] = true,
    };

    var asMetadata = AuthServerMetadata();
    // Path-append discovery (RFC 8414 §3.2): /.well-known/oauth-authorization-server/{resource path}
    app.MapGet("/.well-known/oauth-authorization-server", () => asMetadata).AllowAnonymous();
    app.MapGet("/.well-known/oauth-authorization-server/{**path}", () => asMetadata).AllowAnonymous();
    // Path-insertion layout (RFC 8414 §3.1): {resource path}/.well-known/oauth-authorization-server
    app.MapGet("/mcp/.well-known/oauth-authorization-server", () => asMetadata).AllowAnonymous();
    // OIDC alias — ChatGPT probes both.
    app.MapGet("/.well-known/openid-configuration", () => asMetadata).AllowAnonymous();
    app.MapGet("/mcp/.well-known/openid-configuration", () => asMetadata).AllowAnonymous();
}

app.MapRazorComponents<Zunavio.KdpFactory.Web.Components.App>()
    .AddInteractiveServerRenderMode();

app.Run();

// Constant-time comparison to avoid leaking password length via timing.
static bool SecureEquals(string a, string b)
{
    var aBytes = Encoding.UTF8.GetBytes(a);
    var bBytes = Encoding.UTF8.GetBytes(b);
    if (aBytes.Length != bBytes.Length)
    {
        return false;
    }
    return CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
}

// Local-only redirect guard against open-redirect attacks.
static bool IsLocalUrl(string? url) =>
    !string.IsNullOrEmpty(url)
    && url.StartsWith('/')
    && !url.StartsWith("//", StringComparison.Ordinal)
    && !url.StartsWith("/\\", StringComparison.Ordinal);

public partial class Program;