using System.Text.Json;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Docs.v1;
using Google.Apis.Drive.v3;
using Google.Apis.Http;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Sheets.v4;
using Google.Apis.Util;
using Microsoft.Extensions.Options;
using Zunavio.KdpFactory.Infrastructure.Configuration;

namespace Zunavio.KdpFactory.Infrastructure.Google;

public sealed class GoogleNotConfiguredException : InvalidOperationException
{
    public GoogleNotConfiguredException(string message) : base(message)
    {
    }
}

/// <summary>
/// Resolves the machine credential once and builds Google API services shared
/// across Drive, Docs and Sheets with a common http client initializer.
/// </summary>
public interface IGoogleCredentialProvider
{
    bool IsConfigured { get; }
    GoogleCredential Credential { get; }
    IConfigurableHttpClientInitializer Initializer { get; }
    DriveService Drive { get; }
    DocsService Docs { get; }
    SheetsService Sheets { get; }
}

public sealed class GoogleCredentialProvider : IGoogleCredentialProvider
{
    private const string ApplicationName = "Zunavio Kdp Factory";
    private static readonly string[] Scopes =
    [
        DriveService.Scope.Drive,              // full drive access (docs, folders, media)
        SheetsService.Scope.Spreadsheets,      // control-center sheet
    ];

    private readonly GoogleOptions _options;
    private readonly Lazy<GoogleCredential> _credential;
    private readonly Lazy<IConfigurableHttpClientInitializer> _initializer;

    public GoogleCredentialProvider(IOptions<GoogleOptions> options)
    {
        _options = options.Value;
        _credential = new Lazy<GoogleCredential>(CreateCredential);
        _initializer = new Lazy<IConfigurableHttpClientInitializer>(CreateInitializer);
    }

    public bool IsConfigured => _options.IsConfigured;

    public GoogleCredential Credential
    {
        get
        {
            if (!_options.IsConfigured)
            {
                throw new GoogleNotConfiguredException(
                    "Google integration is not configured. Configure OAuth or service-account credentials and the drive/sheet IDs.");
            }

            if (_options.HasOAuth)
            {
                throw new GoogleNotConfiguredException(
                    "GoogleCredential is not exposed when OAuth user credentials are active; use Initializer/Drive/Docs/Sheets.");
            }

            return _credential.Value;
        }
    }

    public IConfigurableHttpClientInitializer Initializer => _initializer.Value;

    public DriveService Drive => new(new DriveService.Initializer
    {
        HttpClientInitializer = Initializer,
        ApplicationName = ApplicationName,
    });

    public DocsService Docs => new(new DocsService.Initializer
    {
        HttpClientInitializer = Initializer,
        ApplicationName = ApplicationName,
    });

    public SheetsService Sheets => new(new SheetsService.Initializer
    {
        HttpClientInitializer = Initializer,
        ApplicationName = ApplicationName,
    });

    private IConfigurableHttpClientInitializer CreateInitializer()
    {
        if (_options.HasOAuth)
        {
            var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
            {
                ClientSecrets = new ClientSecrets
                {
                    ClientId = _options.OAuthClientId,
                    ClientSecret = _options.OAuthClientSecret
                },
                Scopes = Scopes
            });

            return new UserCredential(
                flow,
                "zunavio-factory",
                new TokenResponse { RefreshToken = _options.OAuthRefreshToken });
        }

        return Credential.CreateScoped(Scopes);
    }

    private GoogleCredential CreateCredential()
    {
        if (!string.IsNullOrWhiteSpace(_options.ServiceAccountJson))
        {
            return GoogleCredential.FromJson(_options.ServiceAccountJson)
                .CreateScoped(Scopes);
        }

        if (!string.IsNullOrWhiteSpace(_options.ApplicationCredentialsPath))
        {
            if (!File.Exists(_options.ApplicationCredentialsPath))
            {
                throw new GoogleNotConfiguredException(
                    $"GOOGLE_APPLICATION_CREDENTIALS points to a missing file '{_options.ApplicationCredentialsPath}'.");
            }

            return GoogleCredential.FromFile(_options.ApplicationCredentialsPath)
                .CreateScoped(Scopes);
        }

        throw new GoogleNotConfiguredException("No Google credential is available.");
    }
}