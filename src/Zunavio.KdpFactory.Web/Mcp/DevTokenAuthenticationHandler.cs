using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Zunavio.KdpFactory.Web.Mcp;

/// <summary>
/// Development-only placeholder for the "Bearer" scheme used when AUTH0_* are not
/// configured. It never authenticates, so /mcp stays protected (401) while the
/// dashboard and REST API remain usable locally. Production refuses to start
/// without AUTH0_* and never registers this handler.
/// </summary>
public sealed class DevTokenAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync() =>
        Task.FromResult(AuthenticateResult.NoResult());
}