using Zunavio.KdpFactory.Infrastructure.Configuration;

namespace Zunavio.KdpFactory.IntegrationTests;

public class DatabaseConfigurationTests
{
    [Theory]
    [InlineData("postgres://user:pass@localhost:5432/kdpfactory")]
    [InlineData("postgresql://user:pass@localhost/kdpfactory")]
    [InlineData("postgres://kdp:kdp@db:5432/kdpfactory?sslmode=disable")]
    public void FromDatabaseUrl_parses_standard_urls(string url)
    {
        var csb = new Npgsql.NpgsqlConnectionStringBuilder(DatabaseOptions.FromDatabaseUrl(url));

        Assert.Equal("kdpfactory", csb.Database);
        Assert.NotEmpty(csb.Username);
        Assert.False(string.IsNullOrEmpty(csb.Password) && url.Contains('@'));
    }

    [Theory]
    [InlineData("http://user:pass@localhost:5432/db")]
    [InlineData("")]
    [InlineData("not a url")]
    public void FromDatabaseUrl_rejects_invalid(string url)
    {
        Assert.Throws<InvalidOperationException>(() => DatabaseOptions.FromDatabaseUrl(url));
    }
}