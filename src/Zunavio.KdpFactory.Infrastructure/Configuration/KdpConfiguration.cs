using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Zunavio.KdpFactory.Infrastructure.Configuration;

/// <summary>Keys + parsing helpers for configuration (env vars, appsettings, secrets).</summary>
public static class KdpSettings
{
    public const string DbUrlEnvKey = "DATABASE_URL";
    public const string DefaultConnectionKey = "ConnectionStrings:DefaultConnection";

    public const string OpenAiApiKeyEnvKey = "OPENAI_API_KEY";
    public const string OpenAiModelEnvKey = "OPENAI_MODEL";
    public const string OpenAiBaseEnvKey = "OPENAI_BASE_URL";

    public const string GoogleServiceAccountJsonEnvKey = "GOOGLE_SERVICE_ACCOUNT_JSON";
    public const string GoogleApplicationCredentialsEnvKey = "GOOGLE_APPLICATION_CREDENTIALS";
    public const string GoogleRootFolderIdKey = "GOOGLE_ROOT_FOLDER_ID";
    public const string GooglePromptsFolderIdKey = "GOOGLE_PROMPTS_FOLDER_ID";
    public const string GoogleControlCenterSpreadsheetIdKey = "GOOGLE_CONTROL_CENTER_SPREADSHEET_ID";
    public const string GoogleProjectFolderIdKey = "GOOGLE_PROJECT_FOLDER_ID";
    public const string GoogleArchitectureDocIdKey = "GOOGLE_ARCHITECTURE_DOC_ID";

    public const string AdminUsernameEnvKey = "ADMIN_USERNAME";
    public const string AdminPasswordEnvKey = "ADMIN_PASSWORD";

    public const string MigrateOnStartupEnvKey = "MIGRATE_ON_STARTUP";
    public const string RunBackgroundWorkerEnvKey = "RUN_BACKGROUND_WORKER";
    public const string SeedDemoProjectEnvKey = "SEED_DEMO_PROJECT";
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

public sealed class GoogleOptions
{
    public string? ServiceAccountJson { get; set; }
    public string? ApplicationCredentialsPath { get; set; }
    public string? RootFolderId { get; set; }
    public string? PromptsFolderId { get; set; }
    public string? ControlCenterSpreadsheetId { get; set; }
    public string? ProjectFolderId { get; set; }
    public string? ArchitectureDocId { get; set; }

    public bool IsConfigured =>
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
    /// (Heroku/Render style) or an ordinary connection string, in that order.
    /// </summary>
    public static string BuildConnectionString(IConfiguration configuration)
    {
        var databaseUrl = configuration[KdpSettings.DbUrlEnvKey];
        if (!string.IsNullOrWhiteSpace(databaseUrl))
        {
            return FromDatabaseUrl(databaseUrl);
        }

        var plain = configuration[KdpSettings.DefaultConnectionKey];
        if (!string.IsNullOrWhiteSpace(plain))
        {
            return plain;
        }

        throw new InvalidOperationException(
            $"No database configured. Set '{KdpSettings.DbUrlEnvKey}' or '{KdpSettings.DefaultConnectionKey}'.");
    }

    public static string FromDatabaseUrl(string databaseUrl)
    {
        if (!Uri.TryCreate(databaseUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != "postgres" && uri.Scheme != "postgresql"))
        {
            throw new InvalidOperationException($"'{KdpSettings.DbUrlEnvKey}' must be a postgres:// URL.");
        }

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort ? 5432 : uri.Port,
            Database = uri.AbsolutePath.TrimStart('/'),
            Username = uri.UserInfo.Split(':', 2)[0],
            Password = uri.UserInfo.Contains(':') ? uri.UserInfo.Split(':', 2)[1] : string.Empty,
            SslMode = SslMode.Prefer,
            TrustServerCertificate = true,
            IncludeErrorDetail = true,
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
                case "sslmode" when Enum.TryParse<SslMode>(value, true, out var mode): builder.SslMode = mode; break;
                case "application_name": builder.ApplicationName = value; break;
                case "pooling" when bool.TryParse(value, out var pooling): builder.Pooling = pooling; break;
                case "timeout" when int.TryParse(value, out var timeout): builder.Timeout = timeout; break;
                case "commandtimeout" when int.TryParse(value, out var ct): builder.CommandTimeout = ct; break;
            }
        }

        builder.ApplicationName ??= "kdp-factory";
        return builder.ConnectionString;
    }
}