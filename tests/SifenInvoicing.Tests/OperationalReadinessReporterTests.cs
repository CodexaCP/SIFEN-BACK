using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SifenInvoicing.Application.Diagnostics;
using SifenInvoicing.Application.Invoices;
using SifenInvoicing.Application.Operations;
using SifenInvoicing.Application.Security;
using SifenInvoicing.Application.Tenancy;
using SifenInvoicing.Application.XmlDe;
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

    // ---- Fase 4.3.1: readiness alineado con el flujo DE01 (solo lectura) ----

    private static async Task<(Tenant Tenant, ITenantContextAccessor Accessor, SifenDbContext Db)> CompleteTenantAsync(
        string slug, bool withActivity = true, bool withNumbering = true)
    {
        var tenant = Tenant.CreateSharedDatabaseTenant(slug, slug.ToUpperInvariant());
        var accessor = CreateTenantAccessor(tenant.Id);
        var db = CreateDbContext(accessor);
        SeedBaseTenantData(db, tenant, withActivity, withNumbering);
        db.TenantCertificateMetadata.Add(TenantCertificateMetadata.Create(
            tenant.Id, SifenEnvironmentType.Test, CertificatePurpose.XmlSignature, "xml-signing", "CN=ACME", "ABC123", "123",
            "config:certificate", "config:password", DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30)));
        await db.SaveChangesAsync();
        return (tenant, accessor, db);
    }

    [Fact]
    public async Task Readiness_ValidDe01Configuration_IsReported_AsReady_WithoutLegacyXsdPathOrSettingsNumbering()
    {
        var (tenant, accessor, db) = await CompleteTenantAsync("ready-de01");
        await using var _ = db;
        // Sin Invoice01RootPath ni ruta XSD por tenant, y con TenantSifenSettings sin relevancia para la numeracion.
        var diagnostic = await CreateReporter(db, accessor).GetTenantFeDiagnosticAsync(tenant.Id, SifenEnvironmentType.Test);

        Assert.True(diagnostic.ReadyForInternalValidation, string.Join("; ", diagnostic.Missing));
        foreach (var code in new[] { "XML_BUILDER_TYPEDOC01", "XML_XSD_VALIDATION", "XSD_ROOT_PATH_AVAILABLE", "DOCUMENT_NUMBER_CONFIGURED", "ESTABLISHMENT_CONFIGURED", "EXPEDITION_POINT_CONFIGURED" })
        {
            Assert.Contains(diagnostic.Checks, item => item.Code == code && item.Status == "Passed");
        }
    }

    [Fact]
    public async Task Readiness_EmitterWithoutEconomicActivity_IsNotReady()
    {
        var (tenant, accessor, db) = await CompleteTenantAsync("no-activity", withActivity: false);
        await using var _ = db;

        var diagnostic = await CreateReporter(db, accessor).GetTenantFeDiagnosticAsync(tenant.Id, SifenEnvironmentType.Test);

        Assert.False(diagnostic.ReadyForInternalValidation);
        var check = Assert.Single(diagnostic.Checks, item => item.Code == "XML_BUILDER_TYPEDOC01");
        Assert.Equal("Failed", check.Status);
        Assert.Contains("gActEco", check.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Readiness_WithoutFiscalStampAndSequence_IsNotReady()
    {
        var (tenant, accessor, db) = await CompleteTenantAsync("no-numbering", withNumbering: false);
        await using var _ = db;

        var diagnostic = await CreateReporter(db, accessor).GetTenantFeDiagnosticAsync(tenant.Id, SifenEnvironmentType.Test);

        Assert.False(diagnostic.ReadyForInternalValidation);
        Assert.Contains(diagnostic.Checks, item => item.Code == "DOCUMENT_NUMBER_CONFIGURED" && item.Status == "Failed");
    }

    [Fact]
    public async Task Readiness_WithoutDeployedXsdPackage_IsNotReady()
    {
        var (tenant, accessor, db) = await CompleteTenantAsync("no-xsd");
        await using var _ = db;
        var missing = new SifenInvoicing.Infrastructure.XmlValidation.SifenDeXsdValidator(
            new SifenInvoicing.Infrastructure.XmlValidation.XmlSchemaValidator(),
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                [SifenInvoicing.Infrastructure.XmlValidation.SifenDeXsdValidator.ConfigurationKey] = Path.Combine(Path.GetTempPath(), "no-such-xsd-" + Guid.NewGuid().ToString("N"))
            }).Build());

        var diagnostic = await CreateReporter(db, accessor, deXsdValidator: missing).GetTenantFeDiagnosticAsync(tenant.Id, SifenEnvironmentType.Test);

        Assert.False(diagnostic.ReadyForInternalValidation);
        Assert.Contains(diagnostic.Checks, item => item.Code == "XML_XSD_VALIDATION" && item.Status == "Failed");
    }

    [Fact]
    public async Task Readiness_DoesNotReserveNumbersNorCreateAnyFiscalDocument()
    {
        var (tenant, accessor, db) = await CompleteTenantAsync("no-side-effects");
        await using var _ = db;
        var reporter = CreateReporter(db, accessor);
        var sequenceBefore = await db.NumberingSequences.AsNoTracking().IgnoreQueryFilters().SingleAsync();

        for (var i = 0; i < 3; i++)
        {
            await reporter.GetTenantFeDiagnosticAsync(tenant.Id, SifenEnvironmentType.Test);
        }

        db.ChangeTracker.Clear();
        var sequenceAfter = await db.NumberingSequences.AsNoTracking().IgnoreQueryFilters().SingleAsync();
        Assert.Equal(sequenceBefore.NextNumber, sequenceAfter.NextNumber);
        Assert.Equal(sequenceBefore.Version, sequenceAfter.Version);
        Assert.Empty(await db.Documents.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await db.DocumentLines.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await db.IdempotencyRecords.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await db.DocumentLogs.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public void Reporter_DoesNotDependOnTheLegacyXmlGeneratorOrValidator()
    {
        var parameterTypes = typeof(ConfigurationOperationalReadinessReporter).GetConstructors()
            .Single().GetParameters().Select(p => p.ParameterType).ToArray();

        Assert.DoesNotContain(typeof(IFacturaXmlGenerator), parameterTypes);
        Assert.DoesNotContain(typeof(IFacturaXmlPreSubmissionValidator), parameterTypes);
        Assert.Contains(typeof(ISifenDeXsdValidator), parameterTypes);
    }

    private static ConfigurationOperationalReadinessReporter CreateReporter(
        SifenDbContext dbContext,
        ITenantContextAccessor tenantAccessor,
        string transportMode = "Diagnostic",
        bool transportCertificateConfigured = false,
        ISifenDeXsdValidator? deXsdValidator = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["Sifen:ActiveEnvironment"] = "Test",
            ["Sifen:Transport:Mode"] = transportMode,
            ["Sifen:Environments:Test:BaseUrl"] = "https://sifen-test.set.gov.py",
            ["Sifen:Environments:Test:Wsdl:Receive"] = "/de/ws/sync/recibe.wsdl?wsdl"
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
            deXsdValidator ?? DeTestKit.Xsd(),
            new TestFiscalClock(),
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

    private static void SeedBaseTenantData(SifenDbContext dbContext, Tenant tenant, bool withActivity = true, bool withNumbering = true)
    {
        dbContext.Tenants.Add(tenant);
        var taxpayer = TestFiscalSetup.Taxpayer(tenant.Id);
        dbContext.TaxpayerProfiles.Add(taxpayer);
        if (withActivity)
        {
            dbContext.TaxpayerEconomicActivities.Add(TestFiscalSetup.Activity(taxpayer));
        }

        if (withNumbering)
        {
            TestFiscalSetup.Seed(dbContext, tenant.Id, withTaxpayer: false);
        }

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
