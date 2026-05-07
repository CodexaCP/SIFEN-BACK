using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using SifenInvoicing.Application.Sifen;
using SifenInvoicing.Domain.Tenants;
using SifenInvoicing.Infrastructure.Diagnostics;
using SifenInvoicing.Infrastructure.Persistence;
using SifenInvoicing.Infrastructure.Sifen;
using SifenInvoicing.Infrastructure.Tenancy;

namespace SifenInvoicing.Tests;

public sealed class ConfigurationSifenSubmissionGatewayTests
{
    [Fact]
    public async Task SendToSifenAsync_ShouldReturnDiagnosticResult_WhenEndpointConfigurationIsMissing()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Sifen:Transport:Mode"] = "Diagnostic"
            })
            .Build();
        await using var dbContext = CreateDbContext();
        var gateway = new ConfigurationSifenSubmissionGateway(configuration, new SystemClock(), new FakeSoapTransport(), dbContext);

        var result = await gateway.SendToSifenAsync(CreateCommand());

        Assert.False(result.IsAcceptedByGateway);
        Assert.True(result.IsDiagnostic);
        Assert.Equal("MISSING_ENDPOINT_CONFIGURATION", result.TransportCode);
    }

    [Fact]
    public async Task SendToSifenAsync_ShouldReturnTimeoutResult_WhenTransportTimesOut()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Sifen:Transport:Mode"] = "Live",
                ["Sifen:Transport:ClientCertificatePath"] = "configured.pfx",
                ["Sifen:Transport:ClientCertificatePasswordEnvironmentVariable"] = "SIFEN_TRANSPORT_CERTIFICATE_PASSWORD",
                ["Sifen:Environments:Test:BaseUrl"] = "https://sifen-test.set.gov.py",
                ["Sifen:Environments:Test:Wsdl:Receive"] = "/de/ws/sync/recibe.wsdl?wsdl",
                ["Sifen:Timeouts:SoapRequestSeconds"] = "1"
            })
            .Build();
        await using var dbContext = CreateDbContext();
        var gateway = new ConfigurationSifenSubmissionGateway(configuration, new SystemClock(), new TimeoutSoapTransport(), dbContext);

        var result = await gateway.SendToSifenAsync(CreateCommand());

        Assert.False(result.IsAcceptedByGateway);
        Assert.False(result.IsDiagnostic);
        Assert.Equal("SOAP_TIMEOUT", result.TransportCode);
        Assert.Contains("SOAP_TIMEOUT|", result.RawResponse, StringComparison.Ordinal);
    }

    private static SendToSifenCommand CreateCommand()
    {
        const string cdc = "01800123456001001000012311123456789202604251";
        const string signedXml = """
            <rDE xmlns="http://ekuatia.set.gov.py/sifen/xsd" xmlns:ds="http://www.w3.org/2000/09/xmldsig#">
              <dVerFor>150</dVerFor>
              <DE Id="01800123456001001000012311123456789202604251">
                <gDatGralOpe />
              </DE>
              <ds:Signature>
                <ds:SignedInfo>
                  <ds:Reference URI="#01800123456001001000012311123456789202604251" />
                </ds:SignedInfo>
              </ds:Signature>
            </rDE>
            """;

        return new SendToSifenCommand(Guid.NewGuid(), SifenEnvironmentType.Test, cdc, signedXml);
    }

    private sealed class FakeSoapTransport : ISifenSoapTransport
    {
        public Task<SifenSoapTransportResult> SendAsync(Uri endpoint, string requestXml, string? soapAction, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new SifenSoapTransportResult(true, 200, "<ok />"));
        }
    }

    private sealed class TimeoutSoapTransport : ISifenSoapTransport
    {
        public Task<SifenSoapTransportResult> SendAsync(Uri endpoint, string requestXml, string? soapAction, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            throw new TaskCanceledException("timeout");
        }
    }

    private static SifenDbContext CreateDbContext()
    {
        var accessor = new AsyncLocalTenantContextAccessor();
        var options = new DbContextOptionsBuilder<SifenDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new SifenDbContext(options, new SystemClock(), accessor);
    }
}
