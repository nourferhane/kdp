using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Zunavio.KdpFactory.Application;
using Zunavio.KdpFactory.Infrastructure;
using Zunavio.KdpFactory.Infrastructure.Configuration;
using Zunavio.KdpFactory.Infrastructure.Health;

var builder = WebApplication.CreateBuilder(args);
var environment = builder.Environment;

// ---- Platform port support (Koyeb/Railway/Heroku inject PORT) ----
// When PORT is provided, bind to it on all interfaces; otherwise fall back to
// ASPNETCORE_URLS (the Dockerfile defaults to http://+:8080).
if (int.TryParse(Environment.GetEnvironmentVariable("PORT"), out var platformPort))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{platformPort}");
}

// ---- Fail fast: admin credentials are required in production ----
if (environment.IsProduction())
{
    var adminUser = builder.Configuration[KdpSettings.AdminUsernameEnvKey];
    var adminPassword = builder.Configuration[KdpSettings.AdminPasswordEnvKey];
    if (string.IsNullOrWhiteSpace(adminUser) || string.IsNullOrWhiteSpace(adminPassword))
    {
        throw new InvalidOperationException(
            $"Refusing to start in Production: {KdpSettings.AdminUsernameEnvKey} and {KdpSettings.AdminPasswordEnvKey} must be set.");
    }
}

builder.Services
    .AddKdpInfrastructure(builder.Configuration, environment)
    .AddKdpApplication();

builder.Services.Configure<JsonOptions>(o => o.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase);

// ---- Authentication (cookie) + authorization (secure by default) ----
builder.Services
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
builder.Services.AddAuthorization(o =>
{
    o.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});
builder.Services.AddCascadingAuthenticationState();

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