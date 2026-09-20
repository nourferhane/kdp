using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using Zunavio.KdpFactory.Application.Abstractions;
using Zunavio.KdpFactory.Infrastructure.Agents;
using Zunavio.KdpFactory.Infrastructure.BackgroundJobs;
using Zunavio.KdpFactory.Infrastructure.Configuration;
using Zunavio.KdpFactory.Infrastructure.Google;
using Zunavio.KdpFactory.Infrastructure.OpenAi;
using Zunavio.KdpFactory.Infrastructure.Persistence;
using Zunavio.KdpFactory.Infrastructure.Persistence.Repositories;
using Zunavio.KdpFactory.Infrastructure.Seeding;

namespace Zunavio.KdpFactory.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddKdpInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // ---- Options (from env vars via appsettings / secrets) ----
        services.Configure<OpenAiOptions>(o =>
        {
            o.ApiKey = configuration[KdpSettings.OpenAiApiKeyEnvKey] ?? string.Empty;
            o.Model = configuration[KdpSettings.OpenAiModelEnvKey] ?? "gpt-4o-mini";
            o.BaseUrl = configuration[KdpSettings.OpenAiBaseEnvKey];
            o.Enabled = !string.IsNullOrWhiteSpace(o.ApiKey);
        });

        services.Configure<GoogleOptions>(o =>
        {
            o.ServiceAccountJson = configuration[KdpSettings.GoogleServiceAccountJsonEnvKey];
            o.ApplicationCredentialsPath = configuration[KdpSettings.GoogleApplicationCredentialsEnvKey];
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
        var connectionString = DatabaseOptions.BuildConnectionString(configuration);
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

        // ---- OpenAI ----
        services.AddSingleton<ILanguageModelClient, OpenAiLanguageModelClient>();

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