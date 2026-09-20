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

    [Fact]
    public void FromDatabaseUrl_decodes_url_encoded_credentials()
    {
        // Supabase-style passwords commonly contain '@' ':' '%' etc. URL-encoded.
        var csb = new Npgsql.NpgsqlConnectionStringBuilder(
            DatabaseOptions.FromDatabaseUrl("postgres://my%40user:p%40ss%3Aw%2Frd%25@db.example.com:5432/kdpfactory"));

        Assert.Equal("my@user", csb.Username);
        Assert.Equal("p@ss:w/rd%", csb.Password);
        Assert.Equal("db.example.com", csb.Host);
    }

    [Fact]
    public void FromDatabaseUrl_defaults_to_ssl_require_in_production()
    {
        var csb = new Npgsql.NpgsqlConnectionStringBuilder(
            DatabaseOptions.FromDatabaseUrl("postgres://u:p@db.example.com:5432/postgres", isProduction: true));

        Assert.Equal(Npgsql.SslMode.Require, csb.SslMode);
        Assert.False(csb.IncludeErrorDetail);
    }

    [Fact]
    public void FromDatabaseUrl_is_permissive_in_non_production()
    {
        var csb = new Npgsql.NpgsqlConnectionStringBuilder(
            DatabaseOptions.FromDatabaseUrl("postgres://u:p@db.example.com:5432/postgres", isProduction: false));

        Assert.Equal(Npgsql.SslMode.Prefer, csb.SslMode);
        Assert.True(csb.IncludeErrorDetail);
    }

    [Fact]
    public void FromDatabaseUrl_accepts_explicit_sslmode_override()
    {
        var csb = new Npgsql.NpgsqlConnectionStringBuilder(
            DatabaseOptions.FromDatabaseUrl("postgres://u:p@db.example.com:5432/postgres?sslmode=verify-full", isProduction: true));

        Assert.Equal(Npgsql.SslMode.VerifyFull, csb.SslMode);
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