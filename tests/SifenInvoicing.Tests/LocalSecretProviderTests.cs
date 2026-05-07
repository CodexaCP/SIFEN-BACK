using Microsoft.Extensions.Configuration;
using SifenInvoicing.Infrastructure.Security;

namespace SifenInvoicing.Tests;

public sealed class LocalSecretProviderTests
{
    [Fact]
    public async Task CheckStringSecretAsync_ShouldResolveConfigReference()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Secrets:TenantA:Csc"] = "CSC-TEST"
            })
            .Build();
        var provider = new LocalConfigurationTenantSecretProvider(configuration);

        var result = await provider.CheckStringSecretAsync("config:Secrets:TenantA:Csc", "csc.secret");

        Assert.True(result.IsReady);
    }

    [Fact]
    public async Task CheckStringSecretAsync_ShouldReturnUnsupportedReference()
    {
        var provider = new LocalConfigurationTenantSecretProvider(new ConfigurationBuilder().Build());

        var result = await provider.CheckStringSecretAsync("vault:tenant-a/csc", "csc.secret");

        Assert.False(result.IsReady);
        Assert.Equal(Application.Security.SecretStatus.UnsupportedReference, result.Status);
    }
}
