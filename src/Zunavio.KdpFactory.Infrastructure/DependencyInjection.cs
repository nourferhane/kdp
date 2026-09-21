using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Npgsql;
using Zunavio.KdpFactory.Application.Abstractions;
using Zunavio.KdpFactory.Infrastructure.Agents;
using Zunavio.KdpFactory.Infrastructure.Ai;
using Zunavio.KdpFactory.Infrastructure.BackgroundJobs;
using Zunavio.KdpFactory.Infrastructure.Configuration;
using Zunavio.KdpFactory.Infrastructure.Gemini;
using Zunavio.KdpFactory.Infrastructure.Google;
using Zunavio.KdpFactory.Infrastructure.OpenAi;
using Zunavio.KdpFactory.Infrastructure.Persistence;
using Zunavio.KdpFactory.Infrastructure.Persistence.Repositories;
using Zunavio.KdpFactory.Infrastructure.Seeding;

namespace Zunavio.KdpFactory.Infrastructure;

public static class DependencyInjection
{
public static IServiceCollection AddKdpInfrastructure(this IServiceCollection services, IConfiguration configuration, IHostEnvironment? environment = null)
{
        // ---- Options (from env vars via appsettings / secrets) ----
        services.Configure<AiProviderOptions>(o =>
        {
            o.Provider = configuration[KdpSettings.AiProviderEnvKey] ?? "OpenAI";
        });

        services.Configure<OpenAiOptions>(o =>
        {
            o.ApiKey = configuration[KdpSettings.OpenAiApiKeyEnvKey] ?? string.Empty;
            o.Model = configuration[KdpSettings.OpenAiModelEnvKey] ?? "gpt-4o-mini";
            o.BaseUrl = configuration[KdpSettings.OpenAiBaseEnvKey];
            o.Enabled = !string.IsNullOrWhiteSpace(o.ApiKey);
        });

        services.Configure<GeminiOptions>(o =>
        {
            o.ApiKey = configuration[KdpSettings.GeminiApiKeyEnvKey] ?? string.Empty;
            o.Model = configuration[KdpSettings.GeminiModelEnvKey] ?? "gemini-2.5-flash";
            o.BaseUrl = configuration[KdpSettings.GeminiBaseEnvKey] ?? "https://generativelanguage.googleapis.com";
            o.Enabled = !string.IsNullOrWhiteSpace(o.ApiKey);
        });

        services.Configure<GoogleOptions>(o =>
        {
            o.ServiceAccountJson = configuration[KdpSettings.GoogleServiceAccountJsonEnvKey];
            o.ApplicationCredentialsPath = configuration[KdpSettings.GoogleApplicationCredentialsEnvKey];
            o.OAuthClientId = configuration[KdpSettings.GoogleOAuthClientIdEnvKey];
            o.OAuthClientSecret = configuration[KdpSettings.GoogleOAuthClientSecretEnvKey];
            o.OAuthRefreshToken = configuration[KdpSettings.GoogleOAuthRefreshTokenEnvKey];
            o.RootFolderId = configuration[KdpSettings.GoogleRootFolderIdKey];
            o.PromptsFolderId = configuration[KdpSettings.GooglePromptsFolderIdKey];
            o.ControlCenterSpreadsheetId = configuration[KdpSettings.GoogleControlCenterSpreadsheetIdKey];
            o.ProjectFolderId = configuration[KdpSettings.GoogleProjectFolderIdKey];
            o.ArchitectureDocId = configuration[KdpSettings.GoogleArchitectureDocIdKey];
        });

        services.Configure<AdminOptions>(o =>
        {
            o.Username = configuration[KdpSettings.AdminUsernameEnvKey];
            o.Password = configuration[KdpSettings.AdminPasswordEnvKey];
        });

        services.Configure<WorkerOptions>(o =>
        {
            o.Enabled = configuration.GetValue(KdpSettings.RunBackgroundWorkerEnvKey, true);
            o.PollIntervalSeconds = configuration.GetValue("JOB_POLL_INTERVAL_SECONDS", 5);
            o.LeaseSeconds = configuration.GetValue("JOB_LEASE_SECONDS", 600);
            o.MaxAttempts = configuration.GetValue("JOB_MAX_ATTEMPTS", 5);
        });

        // ---- EF Core + PostgreSQL ----
        var isProduction = environment is null || environment.IsProduction();
        var connectionString = DatabaseOptions.BuildConnectionString(configuration, isProduction);
        services.AddDbContext<KdpDbContext>(builder =>
            builder.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
            }));
        services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));
        services.AddDbContextFactory<KdpDbContext>(lifetime: ServiceLifetime.Scoped);

        // ---- Persistence ----
        services.AddScoped<IUnitOfWork, KdpUnitOfWork>();
        services.AddScoped<IFactorySettingsProvider, FactorySettingsProvider>();

        // ---- Language model providers ----
        services.AddSingleton<OpenAiLanguageModelClient>();
        services.AddHttpClient<IVisualGenerationService, OpenAiVisualGenerationService>(client =>
        {
            client.Timeout = TimeSpan.FromMinutes(5);
        });
        services.AddSingleton<GeminiLanguageModelClient>();
        services.AddSingleton<ILanguageModelClient, ProviderLanguageModelClient>();

        // ---- Google ----
        services.AddSingleton<IGoogleCredentialProvider, GoogleCredentialProvider>();
        services.AddSingleton<IGoogleConfigurationSettings, GoogleConfigurationSettings>();
        services.AddScoped<IArtifactStorage, GoogleDriveArtifactStorage>();
        services.AddScoped<IAgentPromptLoader, GoogleAgentPromptLoader>();
        services.AddScoped<IGoogleControlCenterSyncService, GoogleControlCenterSyncService>();

        // ---- Agents (AI execution) ----
        services.AddScoped<IAgentExecutor, GenericAgentExecutor>();

        // ---- Seeding ----
        services.AddScoped<AgentDefinitionSeeder>();
        services.AddScoped<IGoogleDriveAgentDefinitionSeeder>(sp => sp.GetRequiredService<AgentDefinitionSeeder>());
        services.AddScoped<DemoProjectSeeder>();

        // ---- Background worker ----
        services.AddScoped<IJobDispatcher, JobDispatcher>();
        services.AddHostedService<BackgroundJobWorker>();
        services.AddHostedService<StartupSeederHostedService>();

        return services;
    }
}