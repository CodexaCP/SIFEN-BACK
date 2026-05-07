using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace SifenInvoicing.Api.Health;

public sealed class SifenConfigurationHealthCheck : IHealthCheck
{
    private readonly IConfiguration _configuration;

    public SifenConfigurationHealthCheck(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var testBaseUrl = _configuration["Sifen:Environments:Test:BaseUrl"];
        var productionBaseUrl = _configuration["Sifen:Environments:Production:BaseUrl"];

        var data = new Dictionary<string, object>
        {
            ["testBaseUrlConfigured"] = !string.IsNullOrWhiteSpace(testBaseUrl),
            ["productionBaseUrlConfigured"] = !string.IsNullOrWhiteSpace(productionBaseUrl),
            ["activeEnvironment"] = _configuration["Sifen:ActiveEnvironment"] ?? "NotConfigured"
        };

        if (string.IsNullOrWhiteSpace(testBaseUrl) || string.IsNullOrWhiteSpace(productionBaseUrl))
        {
            return Task.FromResult(HealthCheckResult.Degraded(
                "SIFEN endpoint configuration is incomplete.",
                data: data));
        }

        return Task.FromResult(HealthCheckResult.Healthy(
            "SIFEN endpoint configuration is present. External connectivity is not checked yet.",
            data));
    }
}
