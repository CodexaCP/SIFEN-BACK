using Microsoft.Extensions.Configuration;
using SifenInvoicing.Infrastructure.Persistence;

namespace SifenInvoicing.Tests;

public sealed class SifenConnectionStringResolverTests
{
    private const string LocalDbConnection = "Server=(localdb)\\MSSQLLocalDB;Database=SifenInvoicing;Trusted_Connection=True;TrustServerCertificate=True";
    private const string EnvironmentConnection = "Server=sql.test;Database=SifenTest;User Id=test;Password=not-a-secret;TrustServerCertificate=True";

    [Fact]
    public void Resolve_ShouldPreferSifenConnectionString()
    {
        var configuration = Build(new Dictionary<string, string?>
        {
            ["SIFEN_CONNECTION_STRING"] = EnvironmentConnection,
            ["ConnectionStrings:DefaultConnection"] = LocalDbConnection
        });

        Assert.Equal(EnvironmentConnection, SifenConnectionStringResolver.Resolve(configuration));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_ShouldFallbackToDefaultConnection_WhenSifenConnectionStringIsMissingOrEmpty(string? value)
    {
        var configuration = Build(new Dictionary<string, string?>
        {
            ["SIFEN_CONNECTION_STRING"] = value,
            ["ConnectionStrings:DefaultConnection"] = LocalDbConnection
        });

        Assert.Equal(LocalDbConnection, SifenConnectionStringResolver.Resolve(configuration));
    }

    [Fact]
    public void Resolve_ShouldReturnNull_WhenNothingIsConfigured()
    {
        var configuration = Build(new Dictionary<string, string?>());

        Assert.Null(SifenConnectionStringResolver.Resolve(configuration));
    }

    private static IConfiguration Build(Dictionary<string, string?> values)
        => new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
