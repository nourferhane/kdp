using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Zunavio.KdpFactory.Infrastructure.Configuration;
using Zunavio.KdpFactory.Infrastructure.Persistence;

namespace Zunavio.KdpFactory.Infrastructure.Seeding;

/// <summary>
/// Runs once at startup: applies migrations (opt-in), seeds the agent catalog
/// and bootstraps the demo project. All steps are idempotent and wrapped in a
/// dedicated scope so seeding never shares state with an in-flight job.
/// </summary>
public sealed class StartupSeederHostedService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    IHostEnvironment environment,
    ILogger<StartupSeederHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<KdpDbContext>();

            bool migrate = configuration.GetValue(KdpSettings.MigrateOnStartupEnvKey, false);
            if (migrate)
            {
                logger.LogInformation("Applying EF Core migrations to {Database}...", db.Database.GetDbConnection().Database);
                await db.Database.MigrateAsync(cancellationToken);
            }

            var seeder = scope.ServiceProvider.GetRequiredService<AgentDefinitionSeeder>();
            await seeder.SeedAsync(cancellationToken);

            bool seedDemo = configuration.GetValue(KdpSettings.SeedDemoProjectEnvKey, environment.IsDevelopment());
            if (seedDemo)
            {
                var seeded = await scope.ServiceProvider.GetRequiredService<DemoProjectSeeder>().EnsureZnv001Async(cancellationToken);
                if (seeded)
                {
                    logger.LogInformation("Demo project ZNV-001 bootstrapped at the MANUSCRIPT gate.");
                }
            }

            logger.LogInformation("Startup seeding complete.");
        }
        catch (Exception ex)
        {
            // Production must not silently continue if the database cannot be
            // (re)initialized: abort so the orchestrator reports a failed deploy.
            if (environment.IsProduction())
            {
                logger.LogCritical(ex,
                    "Startup database migration/seeding failed in production. Refusing to start the application.");
                throw;
            }

            logger.LogError(ex, "Startup seeding failed. The application will continue; manual recovery may be needed.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}