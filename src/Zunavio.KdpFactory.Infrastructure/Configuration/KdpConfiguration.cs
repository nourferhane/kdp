using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Zunavio.KdpFactory.Infrastructure.Configuration;

/// <summary>Keys + parsing helpers for configuration (env vars, appsettings, secrets).</summary>
public static class KdpSettings
{
    public const string DbUrlEnvKey = "DATABASE_URL";
    public const string DefaultConnectionKey = "ConnectionStrings:DefaultConnection";

    public const string AiProviderEnvKey = "AI_PROVIDER";

    public const string OpenAiApiKeyEnvKey = "OPENAI_API_KEY";
    public const string OpenAiModelEnvKey = "OPENAI_MODEL";
    public const string OpenAiBaseEnvKey = "OPENAI_BASE_URL";

    public const string GeminiApiKeyEnvKey = "GEMINI_API_KEY";
    public const string GeminiModelEnvKey = "GEMINI_MODEL";
    public const string GeminiBaseEnvKey = "GEMINI_BASE_URL";

    public const string GoogleServiceAccountJsonEnvKey = "GOOGLE_SERVICE_ACCOUNT_JSON";
    public const string GoogleApplicationCredentialsEnvKey = "GOOGLE_APPLICATION_CREDENTIALS";
    public const string GoogleOAuthClientIdEnvKey = "GOOGLE_OAUTH_CLIENT_ID";
    public const string GoogleOAuthClientSecretEnvKey = "GOOGLE_OAUTH_CLIENT_SECRET";
    public const string GoogleOAuthRefreshTokenEnvKey = "GOOGLE_OAUTH_REFRESH_TOKEN";
    public const string GoogleRootFolderIdKey = "GOOGLE_ROOT_FOLDER_ID";
    public const string GooglePromptsFolderIdKey = "GOOGLE_PROMPTS_FOLDER_ID";
    public const string GoogleControlCenterSpreadsheetIdKey = "GOOGLE_CONTROL_CENTER_SPREADSHEET_ID";
    public const string GoogleProjectFolderIdKey = "GOOGLE_PROJECT_FOLDER_ID";
    public const string GoogleArchitectureDocIdKey = "GOOGLE_ARCHITECTURE_DOC_ID";

    public const string AdminUsernameEnvKey = "ADMIN_USERNAME";
    public const string AdminPasswordEnvKey = "ADMIN_PASSWORD";

    public const string Auth0DomainEnvKey = "AUTH0_DOMAIN";
    public const string Auth0AudienceEnvKey = "AUTH0_AUDIENCE";

    public const string MigrateOnStartupEnvKey = "MIGRATE_ON_STARTUP";
    public const string RunBackgroundWorkerEnvKey = "RUN_BACKGROUND_WORKER";
    public const string SeedDemoProjectEnvKey = "SEED_DEMO_PROJECT";
}

public sealed class AiProviderOptions
{
    public string Provider { get; set; } = "OpenAI";
}

public sealed class OpenAiOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "gpt-4o-mini";
    public string? BaseUrl { get; set; }
    public double? PricePerMillionInput { get; set; }
    public double? PricePerMillionOutput { get; set; }
    public int TimeoutSeconds { get; set; } = 300;
    public int MaxRetries { get; set; } = 3;
    public bool Enabled { get; set; }
}

public sealed class GeminiOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "gemini-2.5-flash";
    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com";
    public double? PricePerMillionInput { get; set; }
    public double? PricePerMillionOutput { get; set; }
    public int TimeoutSeconds { get; set; } = 300;
    public int MaxRetries { get; set; } = 3;
    public bool Enabled { get; set; }
}

public sealed class GoogleOptions
{
    public string? ServiceAccountJson { get; set; }
    public string? ApplicationCredentialsPath { get; set; }
    public string? OAuthClientId { get; set; }
    public string? OAuthClientSecret { get; set; }
    public string? OAuthRefreshToken { get; set; }
    public string? RootFolderId { get; set; }
    public string? PromptsFolderId { get; set; }
    public string? ControlCenterSpreadsheetId { get; set; }
    public string? ProjectFolderId { get; set; }
    public string? ArchitectureDocId { get; set; }

    public bool HasOAuth =>
        !string.IsNullOrWhiteSpace(OAuthClientId) &&
        !string.IsNullOrWhiteSpace(OAuthClientSecret) &&
        !string.IsNullOrWhiteSpace(OAuthRefreshToken);

    public bool IsConfigured =>
        HasOAuth ||
        !string.IsNullOrWhiteSpace(ServiceAccountJson) ||
        !string.IsNullOrWhiteSpace(ApplicationCredentialsPath);

    public bool IsFullyConfigured =>
        IsConfigured &&
        !string.IsNullOrWhiteSpace(ControlCenterSpreadsheetId);
}

public sealed class AdminOptions
{
    public string? Username { get; set; }
    public string? Password { get; set; }
    public bool Enabled => !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrWhiteSpace(Password);
}

public sealed class WorkerOptions
{
    public bool Enabled { get; set; } = true;
    public int PollIntervalSeconds { get; set; } = 5;
    public int LeaseSeconds { get; set; } = 600;
    public int MaxAttempts { get; set; } = 5;
}

public static class DatabaseOptions
{
    /// <summary>
    /// Builds an Npgsql connection string from an optional postgres:// DATABASE_URL
    /// (Heroku/Render/Koyeb/Supabase style) or an ordinary connection string, in that order.
    /// </summary>
    public static string BuildConnectionString(IConfiguration configuration, bool isProduction = false)
    {
        var databaseUrl = configuration[KdpSettings.DbUrlEnvKey];
        if (!string.IsNullOrWhiteSpace(databaseUrl))
        {
            return FromDatabaseUrl(databaseUrl, isProduction);
        }

        var plain = configuration[KdpSettings.DefaultConnectionKey];
        if (!string.IsNullOrWhiteSpace(plain))
        {
            return plain;
        }

        throw new InvalidOperationException(
            $"No database configured. Set '{KdpSettings.DbUrlEnvKey}' or '{KdpSettings.DefaultConnectionKey}'.");
    }

    /// <summary>
    /// Parses a postgres:// or postgresql:// URL into an Npgsql connection string.
    /// Username/password are URL-decoded (Supabase credentials may encode '@', ':', '%').
    /// In production SSL defaults to Require (encryption on, certificate not silently
    /// disabled) and IncludeErrorDetail is off; both can be tuned with ?sslmode= and
    /// ?includedetail= query parameters.
    /// </summary>
    public static string FromDatabaseUrl(string databaseUrl, bool isProduction = false)
    {
        if (!Uri.TryCreate(databaseUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != "postgres" && uri.Scheme != "postgresql"))
        {
            throw new InvalidOperationException($"'{KdpSettings.DbUrlEnvKey}' must be a postgres:// URL.");
        }

        var userInfo = uri.UserInfo.Split(':', 2);
        var sslMode = isProduction ? SslMode.Require : SslMode.Prefer;
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort ? 5432 : uri.Port,
            Database = uri.AbsolutePath.TrimStart('/'),
            Username = Uri.UnescapeDataString(userInfo[0]),
            Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : string.Empty,
            SslMode = sslMode,
            IncludeErrorDetail = !isProduction,
        };

        var queryPairs = uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var pair in queryPairs)
        {
            var parts = pair.Split('=', 2);
            var key = Uri.UnescapeDataString(parts[0]);
            var value = parts.Length == 2 ? Uri.UnescapeDataString(parts[1]) : string.Empty;
            if (string.IsNullOrWhiteSpace(key)) continue;
            switch (key.ToLowerInvariant())
            {
                case "sslmode" when TryParseEnumIgnoringSeparators(value, out SslMode mode): builder.SslMode = mode; break;
                case "includedetail" when bool.TryParse(value, out var detail): builder.IncludeErrorDetail = detail; break;
                case "application_name": builder.ApplicationName = value; break;
                case "pooling" when bool.TryParse(value, out var pooling): builder.Pooling = pooling; break;
                case "timeout" when int.TryParse(value, out var timeout): builder.Timeout = timeout; break;
                case "commandtimeout" when int.TryParse(value, out var ct): builder.CommandTimeout = ct; break;
            }
        }

        builder.ApplicationName ??= "kdp-factory";
        return builder.ConnectionString;
    }

    /// <summary>
    /// Enum.TryParse that also accepts kebab/snake style values (e.g. "verify-full",
    /// "prefer_nossl") regardless of a separator-less enum name.
    /// </summary>
    private static bool TryParseEnumIgnoringSeparators<TEnum>(string value, out TEnum result) where TEnum : struct
    {
        result = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal);
        return Enum.TryParse(normalized, true, out result);
    }
}