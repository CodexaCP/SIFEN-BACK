using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
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
        var gateway = new ConfigurationSifenSubmissionGateway(configuration, new SystemClock(), new FakeSoapTransport(), dbContext, new NoCertificateProvider());

        var result = await gateway.SendToSifenAsync(CreateCommand());

        Assert.False(result.IsAcceptedByGateway);
        Assert.True(result.IsDiagnostic);
        Assert.Equal("MISSING_ENDPOINT_CONFIGURATION", result.TransportCode);
    }

    [Fact]
    public async Task SendToSifenAsync_ShouldReturnTimeoutResult_WhenTransportTimesOut()
    {
        await using var dbContext = CreateDbContext();
        var gateway = new ConfigurationSifenSubmissionGateway(
            LiveConfiguration(), new SystemClock(), new TimeoutSoapTransport(), dbContext, new EphemeralCertificateProvider());

        var result = await gateway.SendToSifenAsync(CreateCommand());

        Assert.False(result.IsAcceptedByGateway);
        Assert.False(result.IsDiagnostic);
        Assert.Equal("SOAP_TIMEOUT", result.TransportCode);
        Assert.Contains("SOAP_TIMEOUT|", result.RawResponse, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendToSifenAsync_ShouldNotCallTransport_InDiagnosticMode()
    {
        var configuration = LiveConfiguration(("Sifen:Transport:Mode", "Diagnostic"));
        var transport = new CapturingSoapTransport();
        await using var dbContext = CreateDbContext();
        var gateway = new ConfigurationSifenSubmissionGateway(
            configuration, new SystemClock(), transport, dbContext, new EphemeralCertificateProvider());

        var result = await gateway.SendToSifenAsync(CreateCommand());

        Assert.True(result.IsDiagnostic);
        Assert.Equal("DIAGNOSTIC_MODE_ENABLED", result.TransportCode);
        Assert.Null(transport.Request);
    }

    [Fact]
    public async Task SendToSifenAsync_ShouldNotCallTransport_WhenTenantHasNoCertificate()
    {
        var transport = new CapturingSoapTransport();
        await using var dbContext = CreateDbContext();
        var gateway = new ConfigurationSifenSubmissionGateway(
            LiveConfiguration(), new SystemClock(), transport, dbContext, new NoCertificateProvider());

        var result = await gateway.SendToSifenAsync(CreateCommand());

        Assert.Equal("MISSING_CLIENT_CERTIFICATE_CONFIGURATION", result.TransportCode);
        Assert.Null(transport.Request);
    }

    [Fact]
    public async Task SendToSifenAsync_ShouldBuildSoap12Request_WithExactSignedDocumentAndTenantCertificate()
    {
        var transport = new CapturingSoapTransport();
        var provider = new EphemeralCertificateProvider();
        var command = CreateCommand();
        await using var dbContext = CreateDbContext();
        var gateway = new ConfigurationSifenSubmissionGateway(LiveConfiguration(), new SystemClock(), transport, dbContext, provider);

        var result = await gateway.SendToSifenAsync(command);

        Assert.True(result.IsAcceptedByGateway);
        var request = Assert.IsType<SifenSoapRequest>(transport.Request);
        Assert.Equal("https://sifen-test.set.gov.py/de/ws/sync/recibe.wsdl", request.Endpoint.ToString());
        Assert.Null(request.SoapAction); // sin SOAPAction confirmada no se inventa ninguna
        Assert.NotNull(request.ClientCertificate);
        Assert.Equal(command.TenantId, provider.LastTenantId);
        Assert.StartsWith("<soap:Envelope xmlns:soap=\"http://www.w3.org/2003/05/soap-envelope\"", request.EnvelopeXml, StringComparison.Ordinal);
        Assert.Contains(command.SignedXml.Trim(), request.EnvelopeXml, StringComparison.Ordinal); // DE firmado intacto
    }

    [Fact]
    public async Task SendToSifenAsync_ShouldPassSoapAction_OnlyWhenConfigured()
    {
        var transport = new CapturingSoapTransport();
        await using var dbContext = CreateDbContext();
        var gateway = new ConfigurationSifenSubmissionGateway(
            LiveConfiguration(("Sifen:Environments:Test:SoapAction:Receive", "urn:configured-action")),
            new SystemClock(), transport, dbContext, new EphemeralCertificateProvider());

        await gateway.SendToSifenAsync(CreateCommand());

        Assert.Equal("urn:configured-action", transport.Request!.SoapAction);
    }

    private static IConfiguration LiveConfiguration(params (string Key, string Value)[] overrides)
    {
        var values = new Dictionary<string, string?>
        {
            ["Sifen:Transport:Mode"] = "Live",
            ["Sifen:Environments:Test:BaseUrl"] = "https://sifen-test.set.gov.py",
            ["Sifen:Environments:Test:Wsdl:Receive"] = "/de/ws/sync/recibe.wsdl?wsdl",
            ["Sifen:Timeouts:SoapRequestSeconds"] = "1"
        };
        foreach (var (key, value) in overrides)
        {
            values[key] = value;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
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
        public Task<SifenSoapTransportResult> SendAsync(SifenSoapRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new SifenSoapTransportResult(true, 200, "<ok />"));
        }
    }

    private sealed class CapturingSoapTransport : ISifenSoapTransport
    {
        public SifenSoapRequest? Request { get; private set; }

        public Task<SifenSoapTransportResult> SendAsync(SifenSoapRequest request, CancellationToken cancellationToken = default)
        {
            Request = request;
            return Task.FromResult(new SifenSoapTransportResult(true, 200, "<ok />"));
        }
    }

    private sealed class TimeoutSoapTransport : ISifenSoapTransport
    {
        public Task<SifenSoapTransportResult> SendAsync(SifenSoapRequest request, CancellationToken cancellationToken = default)
        {
            throw new TaskCanceledException("timeout");
        }
    }

    private sealed class NoCertificateProvider : ISifenClientCertificateProvider
    {
        public Task<X509Certificate2?> GetAsync(Guid tenantId, SifenEnvironmentType environment, CancellationToken cancellationToken = default) =>
            Task.FromResult<X509Certificate2?>(null);
    }

    /// <summary>Solo para verificar el cableado del gateway (el transporte es falso); no simula SIFEN ni valida el certificado real.</summary>
    private sealed class EphemeralCertificateProvider : ISifenClientCertificateProvider
    {
        public Guid? LastTenantId { get; private set; }

        public Task<X509Certificate2?> GetAsync(Guid tenantId, SifenEnvironmentType environment, CancellationToken cancellationToken = default)
        {
            LastTenantId = tenantId;
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest("CN=GATEWAY-WIRING-TEST", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            return Task.FromResult<X509Certificate2?>(
                request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1)));
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
