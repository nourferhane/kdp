using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Zunavio.KdpFactory.Infrastructure.Configuration;

namespace Zunavio.KdpFactory.Infrastructure.Persistence;

/// <summary>
/// Used by `dotnet ef migrations` and `dotnet ef database update` so no ASP.NET
/// host is required at design time. Reads DATABASE_URL (or the default
/// connection string) from the environment.
/// </summary>
public sealed class KdpDbContextFactory : IDesignTimeDbContextFactory<KdpDbContext>
{
    public KdpDbContext CreateDbContext(string[] args)
    {
        var databaseUrl = Environment.GetEnvironmentVariable(KdpSettings.DbUrlEnvKey);
        string? connectionString;
        if (!string.IsNullOrWhiteSpace(databaseUrl))
        {
            connectionString = DatabaseOptions.FromDatabaseUrl(databaseUrl);
        }
        else
        {
            connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
                ?? "Host=localhost;Port=5432;Database=kdpfactory;Username=postgres;Password=postgres";
        }

        var options = new DbContextOptionsBuilder<KdpDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new KdpDbContext(options);
    }
}