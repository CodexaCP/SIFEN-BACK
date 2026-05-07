using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SifenInvoicing.Application.Diagnostics;
using SifenInvoicing.Application.Invoices;
using SifenInvoicing.Application.Operations;
using SifenInvoicing.Application.Security;
using SifenInvoicing.Application.Tenancy;
using SifenInvoicing.Application.XmlSigning;
using SifenInvoicing.Domain.Documents;
using SifenInvoicing.Domain.Tenants;
using SifenInvoicing.Infrastructure.Diagnostics;
using SifenInvoicing.Infrastructure.Invoices;
using SifenInvoicing.Infrastructure.Operations;
using SifenInvoicing.Infrastructure.Persistence;
using SifenInvoicing.Infrastructure.Tenancy;

namespace SifenInvoicing.Tests;

public sealed class OperationalReadinessReporterTests
{
    [Fact]
    public async Task GetTenantFeDiagnosticAsync_ShouldReturnInternalReady_WhenTenantIsCompleteButTransportIsDiagnostic()
    {
        var tenant = Tenant.CreateSharedDatabaseTenant("ready-fe", "READY FE");
        var tenantAccessor = CreateTenantAccessor(tenant.Id);
        await using var dbContext = CreateDbContext(tenantAccessor);
        SeedBaseTenantData(dbContext, tenant);
        dbContext.TenantCertificateMetadata.Add(TenantCertificateMetadata.Create(
            tenant.Id,
            SifenEnvironmentType.Test,
            CertificatePurpose.XmlSignature,
            "xml-signing",
            "CN=ACME",
            "ABC123",
            "123",
            "config:certificate",
            "config:password",
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(30)));
        dbContext.Documents.Add(CreateOperationalInvoice(tenant.Id));
        await dbContext.SaveChangesAsync();

        var reporter = CreateReporter(dbContext, tenantAccessor);

        var diagnostic = await reporter.GetTenantFeDiagnosticAsync(tenant.Id, SifenEnvironmentType.Test);

        Assert.True(diagnostic.ReadyForInternalValidation);
        Assert.False(diagnostic.ReadyForSifenTestAttempt);
        Assert.Equal("Diagnostic", diagnostic.TransportMode);
        Assert.Contains(diagnostic.Checks, item => item.Code == "XML_BUILDER_TYPEDOC01" && item.Status == "Passed");
        Assert.Contains(diagnostic.Checks, item => item.Code == "LOCAL_SIGNATURE_EXECUTION" && item.Status == "Passed");
        Assert.Contains(diagnostic.Checks, item => item.Code == "TRANSPORT_CLIENT_CERTIFICATE_CONFIGURED" && item.Status == "Warning");
    }

    [Fact]
    public async Task GetTenantFeDiagnosticAsync_ShouldReturnNotReady_WhenCertificateIsMissing()
    {
        var tenant = Tenant.CreateSharedDatabaseTenant("missing-cert-fe", "MISSING CERT FE");
        var tenantAccessor = CreateTenantAccessor(tenant.Id);
        await using var dbContext = CreateDbContext(tenantAccessor);
        SeedBaseTenantData(dbContext, tenant);
        dbContext.Documents.Add(CreateOperationalInvoice(tenant.Id));
        await dbContext.SaveChangesAsync();

        var reporter = CreateReporter(dbContext, tenantAccessor);

        var diagnostic = await reporter.GetTenantFeDiagnosticAsync(tenant.Id, SifenEnvironmentType.Test);

        Assert.False(diagnostic.ReadyForInternalValidation);
        Assert.Contains(diagnostic.Checks, item => item.Code == "SIGNATURE_CERTIFICATE_LOADED" && item.Status == "Failed");
        Assert.Contains(diagnostic.Missing, item => item.Contains("Certificate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetTenantFeDiagnosticAsync_ShouldReturnReadyForSifenAttempt_WhenTransportIsLiveAndConfigured()
    {
        var tenant = Tenant.CreateSharedDatabaseTenant("live-fe", "LIVE FE");
        var tenantAccessor = CreateTenantAccessor(tenant.Id);
        await using var dbContext = CreateDbContext(tenantAccessor);
        SeedBaseTenantData(dbContext, tenant);
        dbContext.TenantCertificateMetadata.Add(TenantCertificateMetadata.Create(
            tenant.Id,
            SifenEnvironmentType.Test,
            CertificatePurpose.XmlSignature,
            "xml-signing",
            "CN=ACME",
            "ABC123",
            "123",
            "config:certificate",
            "config:password",
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(30)));
        await dbContext.SaveChangesAsync();

        var reporter = CreateReporter(
            dbContext,
            tenantAccessor,
            transportMode: "Live",
            transportCertificateConfigured: true);

        var diagnostic = await reporter.GetTenantFeDiagnosticAsync(tenant.Id, SifenEnvironmentType.Test);

        Assert.True(diagnostic.ReadyForInternalValidation);
        Assert.True(diagnostic.ReadyForSifenTestAttempt);
        Assert.Equal("Live", diagnostic.TransportMode);
        Assert.Contains(diagnostic.Checks, item => item.Code == "SIFEN_ENDPOINT_CONFIGURED" && item.Status == "Passed");
        Assert.Contains(diagnostic.Checks, item => item.Code == "TRANSPORT_CLIENT_CERTIFICATE_CONFIGURED" && item.Status == "Passed");
    }

    private static ConfigurationOperationalReadinessReporter CreateReporter(
        SifenDbContext dbContext,
        ITenantContextAccessor tenantAccessor,
        string transportMode = "Diagnostic",
        bool transportCertificateConfigured = false)
    {
        var xsdPath = Path.GetTempFileName();
        var values = new Dictionary<string, string?>
        {
            ["Sifen:ActiveEnvironment"] = "Test",
            ["Sifen:Transport:Mode"] = transportMode,
            ["Sifen:Environments:Test:BaseUrl"] = "https://sifen-test.set.gov.py",
            ["Sifen:Environments:Test:Wsdl:Receive"] = "/de/ws/sync/recibe.wsdl?wsdl",
            ["Sifen:XmlSchemas:Invoice01RootPath"] = xsdPath
        };

        if (transportCertificateConfigured)
        {
            values["Sifen:Transport:ClientCertificatePath"] = "C:\\certs\\transport.pfx";
            values["Sifen:Transport:ClientCertificatePasswordEnvironmentVariable"] = "SIFEN_TRANSPORT_CERT";
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        return new ConfigurationOperationalReadinessReporter(
            configuration,
            new SystemClock(),
            tenantAccessor,
            dbContext,
            new ReadySecretProvider(),
            new ReadyCertificateValidator(),
            new FacturaXmlGenerator(),
            new PassThroughFacturaXmlPreSubmissionValidator(),
            new FakeXmlDocumentSigner());
    }

    private static ITenantContextAccessor CreateTenantAccessor(Guid tenantId)
    {
        var tenantAccessor = new AsyncLocalTenantContextAccessor();
        tenantAccessor.SetCurrent(new TenantContext
        {
            TenantId = tenantId.ToString(),
            ResolvedTenantId = tenantId,
            IsResolved = true
        });
        return tenantAccessor;
    }

    private static SifenDbContext CreateDbContext(ITenantContextAccessor tenantAccessor)
    {
        var options = new DbContextOptionsBuilder<SifenDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new SifenDbContext(options, new SystemClock(), tenantAccessor);
    }

    private static void SeedBaseTenantData(SifenDbContext dbContext, Tenant tenant)
    {
        dbContext.Tenants.Add(tenant);
        dbContext.TaxpayerProfiles.Add(TaxpayerProfile.Create(tenant.Id, "80012345", "6", "ACME Paraguay SA"));
        dbContext.TenantSifenSettings.Add(TenantSifenSettings.Create(
            tenant.Id,
            SifenEnvironmentType.Test,
            "0001",
            "config:csc",
            "001",
            "001",
            "0000001",
            "12345678",
            "config:certificate",
            "config:password",
            "xml-signing",
            null,
            null,
            null));
    }

    private static SifenDocument CreateOperationalInvoice(Guid tenantId)
    {
        var document = SifenDocument.CreateInvoice(
            tenantId,
            SifenEnvironmentType.Test,
            "01800123456001001000012311123456789202604251",
            "Factura electrónica",
            "0000123",
            "001",
            "001",
            "PYG",
            "Contado",
            "Cliente Demo",
            "80099999",
            "Barrio Demo",
            "cliente@test.py",
            "0981000000",
            "Observacion TEST",
            100000m,
            0m,
            9090.91m,
            0m,
            9090.91m,
            100000m,
            "<rDE xmlns=\"http://ekuatia.set.gov.py/sifen/xsd\"><DE /></rDE>",
            "TEST-01800123456001001000012311123456789202604251",
            "QR TEST - no válido para SET",
            false,
            DateTimeOffset.UtcNow.AddDays(-1));

        document.MarkSigned("<signed />", DateTimeOffset.UtcNow.AddDays(-1));
        document.MarkPendingSubmission("https://sifen-test.set.gov.py/de/ws/sync/recibe.wsdl", DateTimeOffset.UtcNow.AddHours(-3));
        document.MarkAccepted("123456789012345", "0300", "Aprobado", "<raw />", DateTimeOffset.UtcNow.AddHours(-3));
        return document;
    }

    private sealed class PassThroughFacturaXmlPreSubmissionValidator : IFacturaXmlPreSubmissionValidator
    {
        public Task ValidateTipoDoc01Async(string xml, string cdc, Guid tenantId, SifenEnvironmentType environment, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class FakeXmlDocumentSigner : IXmlDocumentSigner
    {
        public Task<SignedXmlDocumentResult> SignAsync(SignXmlDocumentCommand command, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new SignedXmlDocumentResult(
                $"<signed>{command.Xml}</signed>",
                command.DocumentId,
                "c14n",
                "rsa-sha256",
                "sha256",
                "enveloped"));
        }
    }

    private sealed class ReadySecretProvider : ITenantSecretProvider
    {
        public Task<SecretCheckResult> CheckStringSecretAsync(string secretReference, string checkName, CancellationToken cancellationToken = default)
            => Task.FromResult(new SecretCheckResult(checkName, SecretStatus.Present, "Secret ready."));

        public Task<SecretCheckResult> CheckBinarySecretAsync(string secretReference, string checkName, CancellationToken cancellationToken = default)
            => Task.FromResult(new SecretCheckResult(checkName, SecretStatus.Present, "Secret ready."));

        public Task<string> GetStringSecretAsync(string secretReference, CancellationToken cancellationToken = default)
            => Task.FromResult("secret");

        public Task<byte[]> GetBinarySecretAsync(string secretReference, CancellationToken cancellationToken = default)
            => Task.FromResult(Array.Empty<byte>());
    }

    private sealed class ReadyCertificateValidator : ITenantCertificateValidator
    {
        public Task<CertificateValidationResult> ValidateAsync(TenantCertificateMetadata? metadata, CancellationToken cancellationToken = default)
        {
            if (metadata is null)
            {
                return Task.FromResult(new CertificateValidationResult(
                    false,
                    "Certificate metadata is missing.",
                    [new SecretCheckResult("certificate.metadata", SecretStatus.Missing, "Certificate metadata is missing.")]));
            }

            return Task.FromResult(new CertificateValidationResult(
                true,
                "Certificate loaded and validated.",
                [
                    new SecretCheckResult("certificate.pfx", SecretStatus.Present, "Certificate PFX ready."),
                    new SecretCheckResult("certificate.password", SecretStatus.Present, "Certificate password ready.")
                ]));
        }
    }
}
