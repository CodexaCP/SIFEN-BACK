using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SifenInvoicing.Application.Auditing;
using SifenInvoicing.Application.Diagnostics;
using SifenInvoicing.Application.Invoices;
using SifenInvoicing.Application.XmlDe;
using SifenInvoicing.Application.Operations;
using SifenInvoicing.Application.Security;
using SifenInvoicing.Application.Sifen;
using SifenInvoicing.Application.Tenancy;
using SifenInvoicing.Application.XmlSigning;
using SifenInvoicing.Domain.Common;
using SifenInvoicing.Domain.Documents;
using SifenInvoicing.Domain.Tenants;
using SifenInvoicing.Infrastructure.Diagnostics;
using SifenInvoicing.Infrastructure.Invoices;
using SifenInvoicing.Infrastructure.Persistence;
using SifenInvoicing.Infrastructure.Tenancy;

namespace SifenInvoicing.Tests;

public sealed partial class InvoiceServiceTests
{
    [Fact]
    public async Task CreateAsync_ShouldPersistInternalValidation_WhenTransportModeIsDiagnostic()
    {
        var tenant = Tenant.CreateSharedDatabaseTenant("acme", "ACME");
        var tenantId = tenant.Id;
        var tenantAccessor = new AsyncLocalTenantContextAccessor();
        tenantAccessor.SetCurrent(new TenantContext
        {
            TenantId = tenantId.ToString(),
            ResolvedTenantId = tenantId,
            IsResolved = true
        });

        await using var dbContext = CreateDbContext(tenantAccessor);
        dbContext.Tenants.Add(tenant);
        TestFiscalSetup.Seed(dbContext, tenantId);
        dbContext.TenantCertificateMetadata.Add(TenantCertificateMetadata.Create(
            tenantId,
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

        var submissionGateway = new CountingSubmissionGateway();
        var service = new EfInvoiceService(
            dbContext,
            tenantAccessor,
            DeTestKit.Builder(),
            DeTestKit.Xsd(),
            new InvoiceKudePdfRenderer(),
            new ReadyTenantCertificateValidator(),
            new FakeXmlDocumentSigner(),
            new StubOperationalReadinessReporter(),
            submissionGateway,
            new FakeResponseParser(),
            CreateConfiguration(),
            new NullAuditTrail(),
            new SystemClock(),
            new SifenInvoicing.Infrastructure.Numbering.EfNumberingService(dbContext),
            new TestFiscalClock());

        var result = await service.CreateAsync(new CreateInvoiceCommand(
            Guid.NewGuid().ToString(),
            null,
            "Cliente Demo",
            InvoiceReceiverDocumentType.Ci,
            "1234567",
            null,
            null,
            null,
            InvoiceCurrency.PYG,
            InvoiceSaleCondition.Cash,
            [
                new CreateInvoiceItemCommand("Servicio mensual", 1, 100000m, 10, "SRV-001", 77)
            ]));

        var stored = await service.GetByIdAsync(result.Id);

        Assert.NotNull(stored);
        Assert.Equal(tenantId, stored!.TenantId);
        Assert.Equal(SifenDocumentStatus.InternalValidation, stored.Status);
        Assert.Equal("INTERNAL_VALIDATION", stored.StatusCode);
        Assert.NotNull(stored.SignedXmlPayload);
        Assert.Equal(0, submissionGateway.SendCount);
        Assert.Contains(stored.Logs, log => log.EventType == "internal.validation.completed");
    }

    [Fact]
    public async Task CreateAsync_ShouldFail_WhenTenantIsInactive()
    {
        var tenant = Tenant.CreateSharedDatabaseTenant("inactive-tenant", "INACTIVE TENANT");
        tenant.Suspend();
        var tenantId = tenant.Id;
        var tenantAccessor = new AsyncLocalTenantContextAccessor();
        tenantAccessor.SetCurrent(new TenantContext
        {
            TenantId = tenantId.ToString(),
            ResolvedTenantId = tenantId,
            IsResolved = true
        });

        await using var dbContext = CreateDbContext(tenantAccessor);
        SeedRetryDependencies(dbContext, tenant);
        await dbContext.SaveChangesAsync();

        var service = new EfInvoiceService(
            dbContext,
            tenantAccessor,
            DeTestKit.Builder(),
            DeTestKit.Xsd(),
            new InvoiceKudePdfRenderer(),
            new ReadyTenantCertificateValidator(),
            new FakeXmlDocumentSigner(),
            new StubOperationalReadinessReporter(transportMode: "Live"),
            new FakeSubmissionGateway(),
            new FakeResponseParser(),
            CreateConfiguration(),
            new NullAuditTrail(),
            new SystemClock(),
            new SifenInvoicing.Infrastructure.Numbering.EfNumberingService(dbContext),
            new TestFiscalClock());

        var exception = await Assert.ThrowsAsync<UserFacingException>(() => CreateInvoiceAsync(service));

        Assert.Equal("TENANT_INACTIVE", exception.ErrorCode);
    }

    [Fact]
    public async Task CreateAsync_ShouldFail_WhenMonthlyInvoiceLimitIsReached()
    {
        var tenant = Tenant.CreateSharedDatabaseTenant("limited-tenant", "LIMITED TENANT", maxInvoicesPerMonth: 1);
        var tenantId = tenant.Id;
        var tenantAccessor = new AsyncLocalTenantContextAccessor();
        tenantAccessor.SetCurrent(new TenantContext
        {
            TenantId = tenantId.ToString(),
            ResolvedTenantId = tenantId,
            IsResolved = true
        });

        await using var dbContext = CreateDbContext(tenantAccessor);
        SeedRetryDependencies(dbContext, tenant);
        await dbContext.SaveChangesAsync();

        var service = new EfInvoiceService(
            dbContext,
            tenantAccessor,
            DeTestKit.Builder(),
            DeTestKit.Xsd(),
            new InvoiceKudePdfRenderer(),
            new ReadyTenantCertificateValidator(),
            new FakeXmlDocumentSigner(),
            new StubOperationalReadinessReporter(transportMode: "Live"),
            new FakeSubmissionGateway(),
            new FakeResponseParser(),
            CreateConfiguration(),
            new NullAuditTrail(),
            new SystemClock(),
            new SifenInvoicing.Infrastructure.Numbering.EfNumberingService(dbContext),
            new TestFiscalClock());

        await CreateInvoiceAsync(service);
        var exception = await Assert.ThrowsAsync<UserFacingException>(() => CreateInvoiceAsync(service));

        Assert.Equal("PLAN_MONTHLY_LIMIT_REACHED", exception.ErrorCode);
    }

    [Fact]
    public async Task SearchAsync_ShouldFilterByStatusAndNumber()
    {
        var tenant = Tenant.CreateSharedDatabaseTenant("search", "SEARCH");
        var tenantId = tenant.Id;
        var tenantAccessor = new AsyncLocalTenantContextAccessor();
        tenantAccessor.SetCurrent(new TenantContext
        {
            TenantId = tenantId.ToString(),
            ResolvedTenantId = tenantId,
            IsResolved = true
        });

        await using var dbContext = CreateDbContext(tenantAccessor);
        dbContext.Tenants.Add(tenant);
        TestFiscalSetup.Seed(dbContext, tenantId, firstNumber: 7);
        dbContext.TenantCertificateMetadata.Add(TenantCertificateMetadata.Create(
            tenantId,
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

        var service = new EfInvoiceService(
            dbContext,
            tenantAccessor,
            DeTestKit.Builder(),
            DeTestKit.Xsd(),
            new InvoiceKudePdfRenderer(),
            new ReadyTenantCertificateValidator(),
            new FakeXmlDocumentSigner(),
            new StubOperationalReadinessReporter(transportMode: "Live"),
            new FakeSubmissionGateway(),
            new FakeResponseParser(),
            CreateConfiguration(),
            new NullAuditTrail(),
            new SystemClock(),
            new SifenInvoicing.Infrastructure.Numbering.EfNumberingService(dbContext),
            new TestFiscalClock());

        await service.CreateAsync(new CreateInvoiceCommand(
            Guid.NewGuid().ToString(),
            null,
            "Cliente Demo",
            InvoiceReceiverDocumentType.Ci,
            "1234567",
            null,
            null,
            null,
            InvoiceCurrency.PYG,
            InvoiceSaleCondition.Cash,
            [new CreateInvoiceItemCommand("Servicio mensual", 1, 100000m, 10, "SRV-001", 77)]));

        var found = await service.SearchAsync(new InvoiceSearchQuery(
            SifenDocumentStatus.Accepted,
            null,
            null,
            null,
            "7"));

        var match = Assert.Single(found);
        Assert.Equal("0000007", match.ExternalDocumentNumber);
        Assert.Equal(SifenDocumentStatus.Accepted, match.Status);
    }

    [Fact]
    public async Task CreateAsync_ShouldPersistFailedStatus_WhenGatewayReturnsTechnicalFailure()
    {
        var tenant = Tenant.CreateSharedDatabaseTenant("acme-3", "ACME 3");
        var tenantId = tenant.Id;
        var tenantAccessor = new AsyncLocalTenantContextAccessor();
        tenantAccessor.SetCurrent(new TenantContext
        {
            TenantId = tenantId.ToString(),
            ResolvedTenantId = tenantId,
            IsResolved = true
        });

        await using var dbContext = CreateDbContext(tenantAccessor);
        dbContext.Tenants.Add(tenant);
        TestFiscalSetup.Seed(dbContext, tenantId);
        dbContext.TenantCertificateMetadata.Add(TenantCertificateMetadata.Create(
            tenantId,
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

        var service = new EfInvoiceService(
            dbContext,
            tenantAccessor,
            DeTestKit.Builder(),
            DeTestKit.Xsd(),
            new InvoiceKudePdfRenderer(),
            new ReadyTenantCertificateValidator(),
            new FakeXmlDocumentSigner(),
            new StubOperationalReadinessReporter(transportMode: "Live"),
            new FailingSubmissionGateway(),
            new DefaultParser(),
            CreateConfiguration(),
            new NullAuditTrail(),
            new SystemClock(),
            new SifenInvoicing.Infrastructure.Numbering.EfNumberingService(dbContext),
            new TestFiscalClock());

        var result = await service.CreateAsync(new CreateInvoiceCommand(
            Guid.NewGuid().ToString(),
            null,
            "Cliente Demo",
            InvoiceReceiverDocumentType.Ci,
            "1234567",
            null,
            null,
            null,
            InvoiceCurrency.PYG,
            InvoiceSaleCondition.Cash,
            [
                new CreateInvoiceItemCommand("Servicio mensual", 1, 100000m, 10, "SRV-001", 77)
            ]));

        var stored = await service.GetByIdAsync(result.Id);

        Assert.NotNull(stored);
        Assert.Equal(SifenDocumentStatus.Failed, stored!.Status);
        Assert.Equal("SOAP_TIMEOUT", stored.StatusCode);
        Assert.Equal("SIFEN no respondio dentro del tiempo esperado.", stored.StatusMessage);
        Assert.Equal("https://sifen-test.set.gov.py/de/ws/sync/recibe.wsdl", stored.LastSubmissionEndpoint);
        Assert.Contains(stored.Logs, log => log.EventType == "sifen.request");
        Assert.Contains(stored.Logs, log => log.EventType == "sifen.submission");
    }

    [Fact]
    public async Task RetryAsync_ShouldNotAllowApprovedDocument()
    {
        var tenant = Tenant.CreateSharedDatabaseTenant("approved-retry", "APPROVED RETRY");
        var tenantId = tenant.Id;
        var tenantAccessor = new AsyncLocalTenantContextAccessor();
        tenantAccessor.SetCurrent(new TenantContext
        {
            TenantId = tenantId.ToString(),
            ResolvedTenantId = tenantId,
            ClientId = "ops-user",
            IsResolved = true
        });

        await using var dbContext = CreateDbContext(tenantAccessor);
        SeedRetryDependencies(dbContext, tenant);
        await dbContext.SaveChangesAsync();

        var service = new EfInvoiceService(
            dbContext,
            tenantAccessor,
            DeTestKit.Builder(),
            DeTestKit.Xsd(),
            new InvoiceKudePdfRenderer(),
            new ReadyTenantCertificateValidator(),
            new FakeXmlDocumentSigner(),
            new StubOperationalReadinessReporter(transportMode: "Live"),
            new FakeSubmissionGateway(),
            new DefaultParser(),
            CreateConfiguration(),
            new NullAuditTrail(),
            new SystemClock(),
            new SifenInvoicing.Infrastructure.Numbering.EfNumberingService(dbContext),
            new TestFiscalClock());

        var created = await CreateInvoiceAsync(service);

        var exception = await Assert.ThrowsAsync<DomainException>(() => service.RetryAsync(created.Id));

        Assert.Contains("Approved invoices cannot be retried.", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RetryAsync_ShouldAllowTechnicalFailure()
    {
        var tenant = Tenant.CreateSharedDatabaseTenant("tech-retry", "TECH RETRY");
        var tenantId = tenant.Id;
        var tenantAccessor = new AsyncLocalTenantContextAccessor();
        tenantAccessor.SetCurrent(new TenantContext
        {
            TenantId = tenantId.ToString(),
            ResolvedTenantId = tenantId,
            ClientId = "ops-user",
            IsResolved = true
        });

        await using var dbContext = CreateDbContext(tenantAccessor);
        SeedRetryDependencies(dbContext, tenant);
        await dbContext.SaveChangesAsync();

        var failedService = new EfInvoiceService(
            dbContext,
            tenantAccessor,
            DeTestKit.Builder(),
            DeTestKit.Xsd(),
            new InvoiceKudePdfRenderer(),
            new ReadyTenantCertificateValidator(),
            new FakeXmlDocumentSigner(),
            new StubOperationalReadinessReporter(transportMode: "Live"),
            new FailingSubmissionGateway(),
            new DefaultParser(),
            CreateConfiguration(),
            new NullAuditTrail(),
            new SystemClock(),
            new SifenInvoicing.Infrastructure.Numbering.EfNumberingService(dbContext),
            new TestFiscalClock());

        var created = await CreateInvoiceAsync(failedService);

        var retryService = new EfInvoiceService(
            dbContext,
            tenantAccessor,
            DeTestKit.Builder(),
            DeTestKit.Xsd(),
            new InvoiceKudePdfRenderer(),
            new ReadyTenantCertificateValidator(),
            new FakeXmlDocumentSigner(),
            new StubOperationalReadinessReporter(transportMode: "Live"),
            new FakeSubmissionGateway(),
            new DefaultParser(),
            CreateConfiguration(),
            new NullAuditTrail(),
            new SystemClock(),
            new SifenInvoicing.Infrastructure.Numbering.EfNumberingService(dbContext),
            new TestFiscalClock());

        var retried = await retryService.RetryAsync(created.Id);
        var stored = await retryService.GetByIdAsync(created.Id);

        Assert.Equal(1, retried.AttemptNumber);
        Assert.Equal(SifenDocumentStatus.Accepted, retried.Status);
        Assert.NotNull(stored);
        Assert.Equal(SifenDocumentStatus.Accepted, stored!.Status);
        Assert.Contains(stored.Logs, log => log.EventType == "sifen.retry.attempted");
        Assert.Contains(stored.Logs, log => log.EventType == "sifen.retry.result");
    }

    [Fact]
    public async Task RetryAsync_ShouldNotAllowRejectedDocument()
    {
        var tenant = Tenant.CreateSharedDatabaseTenant("rejected-retry", "REJECTED RETRY");
        var tenantId = tenant.Id;
        var tenantAccessor = new AsyncLocalTenantContextAccessor();
        tenantAccessor.SetCurrent(new TenantContext
        {
            TenantId = tenantId.ToString(),
            ResolvedTenantId = tenantId,
            ClientId = "ops-user",
            IsResolved = true
        });

        await using var dbContext = CreateDbContext(tenantAccessor);
        SeedRetryDependencies(dbContext, tenant);
        await dbContext.SaveChangesAsync();

        var service = new EfInvoiceService(
            dbContext,
            tenantAccessor,
            DeTestKit.Builder(),
            DeTestKit.Xsd(),
            new InvoiceKudePdfRenderer(),
            new ReadyTenantCertificateValidator(),
            new FakeXmlDocumentSigner(),
            new StubOperationalReadinessReporter(transportMode: "Live"),
            new RejectedSubmissionGateway(),
            new DefaultParser(),
            CreateConfiguration(),
            new NullAuditTrail(),
            new SystemClock(),
            new SifenInvoicing.Infrastructure.Numbering.EfNumberingService(dbContext),
            new TestFiscalClock());

        var created = await CreateInvoiceAsync(service);

        var exception = await Assert.ThrowsAsync<DomainException>(() => service.RetryAsync(created.Id));

        Assert.Contains("Rejected invoices require explicit reprocessing.", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RetryAsync_ShouldRegisterAuditAttempt()
    {
        var tenant = Tenant.CreateSharedDatabaseTenant("audit-retry", "AUDIT RETRY");
        var tenantId = tenant.Id;
        var tenantAccessor = new AsyncLocalTenantContextAccessor();
        tenantAccessor.SetCurrent(new TenantContext
        {
            TenantId = tenantId.ToString(),
            ResolvedTenantId = tenantId,
            ClientId = "ops-audit",
            IsResolved = true
        });

        await using var dbContext = CreateDbContext(tenantAccessor);
        SeedRetryDependencies(dbContext, tenant);
        await dbContext.SaveChangesAsync();
        var auditTrail = new ListAuditTrail();

        var failedService = new EfInvoiceService(
            dbContext,
            tenantAccessor,
            DeTestKit.Builder(),
            DeTestKit.Xsd(),
            new InvoiceKudePdfRenderer(),
            new ReadyTenantCertificateValidator(),
            new FakeXmlDocumentSigner(),
            new StubOperationalReadinessReporter(transportMode: "Live"),
            new FailingSubmissionGateway(),
            new DefaultParser(),
            CreateConfiguration(),
            new NullAuditTrail(),
            new SystemClock(),
            new SifenInvoicing.Infrastructure.Numbering.EfNumberingService(dbContext),
            new TestFiscalClock());

        var created = await CreateInvoiceAsync(failedService);

        var retryService = new EfInvoiceService(
            dbContext,
            tenantAccessor,
            DeTestKit.Builder(),
            DeTestKit.Xsd(),
            new InvoiceKudePdfRenderer(),
            new ReadyTenantCertificateValidator(),
            new FakeXmlDocumentSigner(),
            new StubOperationalReadinessReporter(transportMode: "Live"),
            new FakeSubmissionGateway(),
            new DefaultParser(),
            CreateConfiguration(),
            auditTrail,
            new SystemClock(),
            new SifenInvoicing.Infrastructure.Numbering.EfNumberingService(dbContext),
            new TestFiscalClock());

        await retryService.RetryAsync(created.Id);

        var audit = Assert.Single(auditTrail.Events);
        Assert.Equal("invoice.retry", audit.EventName);
        Assert.Equal("ops-audit", audit.ActorId);
        Assert.Equal("1", audit.Metadata["attemptNumber"]);
        Assert.Equal(SifenDocumentStatus.Accepted.ToString(), audit.Outcome);
    }

    [Fact]
    public async Task CreateAsync_ShouldBlockLiveEmission_WhenTenantDiagnosticIsNotReady()
    {
        var tenant = Tenant.CreateSharedDatabaseTenant("live-blocked", "LIVE BLOCKED");
        var tenantId = tenant.Id;
        var tenantAccessor = new AsyncLocalTenantContextAccessor();
        tenantAccessor.SetCurrent(new TenantContext
        {
            TenantId = tenantId.ToString(),
            ResolvedTenantId = tenantId,
            IsResolved = true
        });

        await using var dbContext = CreateDbContext(tenantAccessor);
        SeedRetryDependencies(dbContext, tenant);
        await dbContext.SaveChangesAsync();

        var service = new EfInvoiceService(
            dbContext,
            tenantAccessor,
            DeTestKit.Builder(),
            DeTestKit.Xsd(),
            new InvoiceKudePdfRenderer(),
            new ReadyTenantCertificateValidator(),
            new FakeXmlDocumentSigner(),
            new StubOperationalReadinessReporter(transportMode: "Live", readyForSifenTestAttempt: false),
            new FakeSubmissionGateway(),
            new FakeResponseParser(),
            CreateConfiguration(),
            new NullAuditTrail(),
            new SystemClock(),
            new SifenInvoicing.Infrastructure.Numbering.EfNumberingService(dbContext),
            new TestFiscalClock());

        var exception = await Assert.ThrowsAsync<UserFacingException>(() => CreateInvoiceAsync(service));

        Assert.Equal("SIFEN_CONFIGURATION_INCOMPLETE", exception.ErrorCode);
    }

    [Fact]
    public async Task CreateAsync_ShouldPersistUnsignedDraft_WhenDevelopmentOverrideAllowsMissingDependencies()
    {
        using var _ = new DevelopmentEnvironmentScope();

        var tenant = Tenant.CreateSharedDatabaseTenant("dev-unsigned", "DEV UNSIGNED");
        var tenantId = tenant.Id;
        var tenantAccessor = new AsyncLocalTenantContextAccessor();
        tenantAccessor.SetCurrent(new TenantContext
        {
            TenantId = tenantId.ToString(),
            ResolvedTenantId = tenantId,
            IsResolved = true
        });

        await using var dbContext = CreateDbContext(tenantAccessor);
        dbContext.Tenants.Add(tenant);
        TestFiscalSetup.Seed(dbContext, tenantId);
        await dbContext.SaveChangesAsync();

        var submissionGateway = new CountingSubmissionGateway();
        var service = new EfInvoiceService(
            dbContext,
            tenantAccessor,
            DeTestKit.Builder(),
            DeTestKit.Xsd(),
            new InvoiceKudePdfRenderer(),
            new MissingTenantCertificateValidator(),
            new FakeXmlDocumentSigner(),
            new StubOperationalReadinessReporter(
                transportMode: "Diagnostic",
                readyForInternalValidation: false,
                missingChecks:
                [
                    new TenantFeOperationalCheck("CSC_SECRET_AVAILABLE", "Failed", "CSC secret is missing.", false),
                    new TenantFeOperationalCheck("XSD_ROOT_PATH_CONFIGURED", "Failed", "Invoice TipoDoc 01 XSD root path is missing.", false),
                    new TenantFeOperationalCheck("SIGNATURE_CERTIFICATE_VALID", "Failed", "Signature certificate metadata is missing.", false)
                ]),
            submissionGateway,
            new FakeResponseParser(),
            CreateConfiguration(allowUnsignedInternalValidation: true),
            new NullAuditTrail(),
            new SystemClock(),
            new SifenInvoicing.Infrastructure.Numbering.EfNumberingService(dbContext),
            new TestFiscalClock());

        var result = await CreateInvoiceAsync(service);
        var stored = await service.GetByIdAsync(result.Id);

        Assert.NotNull(stored);
        Assert.Equal(SifenDocumentStatus.DraftValidatedWithoutSignature, stored!.Status);
        Assert.Equal("DRAFT_VALIDATED_WITHOUT_SIGNATURE", stored.StatusCode);
        Assert.Equal("XML generated locally. Certificate/XSD/CSC are missing for full validation.", stored.StatusMessage);
        Assert.Null(stored.SignedXmlPayload);
        Assert.Equal(0, submissionGateway.SendCount);
        Assert.Contains(stored.Logs, log => log.EventType == "internal.validation.degraded");
    }

    // ---- Fase 3: numeracion, idempotencia, campos fiscales controlados por el backend ----

    private sealed record Fixture(EfInvoiceService Service, SifenDbContext Db, Guid TenantId, ITenantContextAccessor Accessor)
    {
        /// <summary>El tenant es ambiente (AsyncLocal): cada prueba lo activa en su propio flujo.</summary>
        public void Use() => Accessor.SetCurrent(new TenantContext { TenantId = TenantId.ToString(), ResolvedTenantId = TenantId, IsResolved = true });
    }

    private static async Task<Fixture> CreateFiscalFixtureAsync(
        Guid? tenantIdOverride = null,
        bool seedNumbering = true,
        bool fiscalProfile = true,
        long firstNumber = 1,
        ITenantContextAccessor? sharedAccessor = null,
        SifenDbContext? sharedDb = null,
        SifenDeXmlBuilder? builder = null,
        ISifenDeXsdValidator? xsd = null,
        bool withActivity = true,
        Action<TaxpayerProfile>? customizeTaxpayer = null)
    {
        var tenant = Tenant.CreateSharedDatabaseTenant($"fiscal-{Guid.NewGuid():N}"[..20], "FISCAL");
        var tenantId = tenantIdOverride ?? tenant.Id;
        var accessor = sharedAccessor ?? new AsyncLocalTenantContextAccessor();
        accessor.SetCurrent(new TenantContext { TenantId = tenantId.ToString(), ResolvedTenantId = tenantId, IsResolved = true });
        var dbContext = sharedDb ?? CreateDbContext(accessor);
        dbContext.Tenants.Add(tenant);

        if (fiscalProfile)
        {
            var taxpayer = TestFiscalSetup.Taxpayer(tenant.Id);
            customizeTaxpayer?.Invoke(taxpayer);
            dbContext.TaxpayerProfiles.Add(taxpayer);
            if (withActivity)
            {
                dbContext.TaxpayerEconomicActivities.Add(TestFiscalSetup.Activity(taxpayer));
            }
        }
        else
        {
            dbContext.TaxpayerProfiles.Add(TaxpayerProfile.Create(tenant.Id, "80012345", "6", "ACME Paraguay SA"));
        }

        if (seedNumbering)
        {
            TestFiscalSetup.Seed(dbContext, tenant.Id, firstNumber: firstNumber, withTaxpayer: false);
        }

        dbContext.TenantCertificateMetadata.Add(TenantCertificateMetadata.Create(
            tenant.Id, SifenEnvironmentType.Test, CertificatePurpose.XmlSignature, "xml-signing", "CN=ACME", "ABC123", "123",
            "config:certificate", "config:password", DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30)));
        await dbContext.SaveChangesAsync();

        var service = new EfInvoiceService(
            dbContext, accessor, builder ?? DeTestKit.Builder(), xsd ?? DeTestKit.Xsd(),
            new InvoiceKudePdfRenderer(), new ReadyTenantCertificateValidator(), new FakeXmlDocumentSigner(),
            new StubOperationalReadinessReporter(), new CountingSubmissionGateway(), new FakeResponseParser(),
            CreateConfiguration(), new NullAuditTrail(), new SystemClock(),
            new SifenInvoicing.Infrastructure.Numbering.EfNumberingService(dbContext), new TestFiscalClock());

        return new Fixture(service, dbContext, tenant.Id, accessor);
    }

    private static CreateInvoiceCommand Command(string key, decimal price = 100000m, string description = "Servicio mensual") =>
        new(key, null, "Cliente Demo", InvoiceReceiverDocumentType.Ci, "1234567", null, null, null,
            InvoiceCurrency.PYG, InvoiceSaleCondition.Cash, [new CreateInvoiceItemCommand(description, 1, price, 10, "SRV-001", 77)]);

    [Fact]
    public async Task CreateAsync_ShouldAssignNumberStampAndCdcFromTenantFiscalConfiguration()
    {
        var f = await CreateFiscalFixtureAsync(firstNumber: 41);
        f.Use();

        var result = await f.Service.CreateAsync(Command("k-1"));
        var doc = await f.Db.Documents.SingleAsync();

        Assert.Equal("0000041", doc.ExternalDocumentNumber);
        Assert.Equal("001", doc.EstablishmentCode);
        Assert.Equal("001", doc.ExpeditionPointCode);
        Assert.Equal(TestFiscalSetup.StampingNumber, doc.StampingNumber);
        Assert.Equal(SifenEnvironmentType.Test, doc.Environment);

        // CDC (Manual v150 sec. 10.1): tipoDoc RUC DV est punto numero tipoContrib fecha tipoEmision codSeg DV
        var cdc = result.Cdc;
        Assert.Equal(44, cdc.Length);
        Assert.Equal("01", cdc[..2]);
        Assert.Equal("80012345", cdc[2..10]);
        Assert.Equal("0", cdc[10..11]);
        Assert.Equal("001", cdc[11..14]);
        Assert.Equal("001", cdc[14..17]);
        Assert.Equal("0000041", cdc[17..24]);
        Assert.Equal("2", cdc[24..25]);                       // tipo contribuyente de la configuracion (persona juridica)
        Assert.Equal(new TestFiscalClock().Now.ToString("yyyyMMdd"), cdc[25..33]);
        Assert.Equal("1", cdc[33..34]);                       // emision normal
        Assert.NotEqual("0000041", cdc[34..41]);
        Assert.Equal(9, cdc[34..43].Length);
        Assert.NotEqual("123456789", cdc[34..43]);            // nunca la constante historica
        Assert.True(SifenInvoicing.Application.Cdc.CdcGenerator.ValidateCDC(cdc));
    }

    [Fact]
    public async Task CreateAsync_ShouldConsumeSequentialNumbers()
    {
        var f = await CreateFiscalFixtureAsync();
        f.Use();

        await f.Service.CreateAsync(Command("k-1"));
        await f.Service.CreateAsync(Command("k-2", 200000m));

        var numbers = (await f.Db.Documents.Select(d => d.ExternalDocumentNumber).ToListAsync()).OrderBy(n => n).ToList();
        Assert.Equal(["0000001", "0000002"], numbers);
        Assert.Equal(3, (await f.Db.NumberingSequences.SingleAsync()).NextNumber);
    }

    [Fact]
    public async Task CreateAsync_Idempotency_SameKeyAndContent_ShouldReturnSameInvoiceWithoutConsumingAnotherNumber()
    {
        var f = await CreateFiscalFixtureAsync();
        f.Use();

        var first = await f.Service.CreateAsync(Command("same-key"));
        var second = await f.Service.CreateAsync(Command("same-key"));

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(first.Cdc, second.Cdc);
        Assert.Single(await f.Db.Documents.ToListAsync());
        Assert.Single(await f.Db.IdempotencyRecords.ToListAsync());
        Assert.Equal(2, (await f.Db.NumberingSequences.SingleAsync()).NextNumber);
    }

    [Fact]
    public async Task CreateAsync_Idempotency_SameKeyDifferentContent_ShouldFailWithoutCreatingAnything()
    {
        var f = await CreateFiscalFixtureAsync();
        f.Use();
        await f.Service.CreateAsync(Command("reused"));

        var ex = await Assert.ThrowsAsync<UserFacingException>(() => f.Service.CreateAsync(Command("reused", 999m)));

        Assert.Equal("IDEMPOTENCY_KEY_REUSED", ex.ErrorCode);
        Assert.Single(await f.Db.Documents.ToListAsync());
        Assert.Equal(2, (await f.Db.NumberingSequences.SingleAsync()).NextNumber);
    }

    [Fact]
    public async Task CreateAsync_ShouldRequireIdempotencyKey()
    {
        var f = await CreateFiscalFixtureAsync();
        f.Use();

        await Assert.ThrowsAsync<DomainException>(() => f.Service.CreateAsync(Command("  ")));
        Assert.Empty(await f.Db.Documents.ToListAsync());
        Assert.Equal(1, (await f.Db.NumberingSequences.SingleAsync()).NextNumber);
    }

    [Fact]
    public async Task CreateAsync_Idempotency_ShouldBeScopedPerTenant()
    {
        var shared = new AsyncLocalTenantContextAccessor();
        var a = await CreateFiscalFixtureAsync(sharedAccessor: shared);
        var b = await CreateFiscalFixtureAsync(sharedAccessor: shared, sharedDb: a.Db);

        shared.SetCurrent(new TenantContext { TenantId = a.TenantId.ToString(), ResolvedTenantId = a.TenantId, IsResolved = true });
        var ra = await a.Service.CreateAsync(Command("shared-key"));
        shared.SetCurrent(new TenantContext { TenantId = b.TenantId.ToString(), ResolvedTenantId = b.TenantId, IsResolved = true });
        var rb = await b.Service.CreateAsync(Command("shared-key"));

        Assert.NotEqual(ra.Id, rb.Id);
        Assert.Equal(2, await a.Db.IdempotencyRecords.IgnoreQueryFilters().CountAsync());
        // Cada tenant arranca su propia numeracion en 0000001.
        var numbers = await a.Db.Documents.IgnoreQueryFilters().Select(d => d.ExternalDocumentNumber).ToListAsync();
        Assert.Equal(["0000001", "0000001"], numbers);
    }

    [Fact]
    public async Task CreateAsync_ShouldNotConsumeNumber_WhenNumberingIsNotConfigured()
    {
        var f = await CreateFiscalFixtureAsync(seedNumbering: false);
        f.Use();

        var ex = await Assert.ThrowsAsync<UserFacingException>(() => f.Service.CreateAsync(Command("k-1")));

        Assert.Equal("FISCAL_NUMBERING_NOT_CONFIGURED", ex.ErrorCode);
        Assert.Empty(await f.Db.Documents.ToListAsync());
        Assert.Empty(await f.Db.IdempotencyRecords.ToListAsync());
    }

    [Fact]
    public async Task CreateAsync_ShouldFailBeforeReservingNumber_WhenFiscalProfileIsIncomplete()
    {
        var f = await CreateFiscalFixtureAsync(fiscalProfile: false);
        f.Use();

        var ex = await Assert.ThrowsAsync<UserFacingException>(() => f.Service.CreateAsync(Command("k-1")));

        Assert.Equal("FISCAL_CONFIGURATION_INCOMPLETE", ex.ErrorCode);
        Assert.Equal(1, (await f.Db.NumberingSequences.SingleAsync()).NextNumber);
    }

    [Fact]
    public async Task CreateAsync_ShouldNotConsumeNumber_WhenFiscalCalculationFails()
    {
        var f = await CreateFiscalFixtureAsync();
        f.Use();
        var usd = Command("k-usd") with { Currency = InvoiceCurrency.USD };

        await Assert.ThrowsAsync<DomainException>(() => f.Service.CreateAsync(usd));

        Assert.Equal(1, (await f.Db.NumberingSequences.SingleAsync()).NextNumber);
    }

    [Fact]
    public async Task CreateAsync_ShouldPersistTotalsFromFiscalEngine()
    {
        var f = await CreateFiscalFixtureAsync();
        f.Use();
        var command = Command("k-1") with
        {
            Items = [new CreateInvoiceItemCommand("A", 1, 150000m, 10, "SRV-001", 77), new CreateInvoiceItemCommand("B", 1, 150000m, 10, "SRV-001", 77)]
        };

        var result = await f.Service.CreateAsync(command);
        var doc = await f.Db.Documents.SingleAsync();

        Assert.Equal(300000m, result.TotalAmount);
        Assert.Equal(27272m, doc.TotalVatAmount);
        Assert.Equal(27272m, doc.Vat10Amount);
        Assert.Equal(2, await f.Db.DocumentLines.CountAsync());
    }

    private static SifenDbContext CreateDbContext(ITenantContextAccessor tenantAccessor)
    {
        var options = new DbContextOptionsBuilder<SifenDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new SifenDbContext(options, new SystemClock(), tenantAccessor);
    }

    private static async Task<CreateInvoiceResult> CreateInvoiceAsync(IInvoiceService service)
    {
        return await service.CreateAsync(new CreateInvoiceCommand(
            Guid.NewGuid().ToString(),
            null,
            "Cliente Demo",
            InvoiceReceiverDocumentType.Ci,
            "1234567",
            null,
            null,
            null,
            InvoiceCurrency.PYG,
            InvoiceSaleCondition.Cash,
            [
                new CreateInvoiceItemCommand("Servicio mensual", 1, 100000m, 10, "SRV-001", 77)
            ]));
    }

    private static IConfiguration CreateConfiguration(bool allowUnsignedInternalValidation = false)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Sifen:Development:AllowUnsignedInternalValidation"] = allowUnsignedInternalValidation.ToString(),
                ["Sifen:De:DefaultTransactionType"] = "1",
                ["Sifen:De:DefaultPresenceIndicator"] = "1"
            })
            .Build();
    }

    private static void SeedRetryDependencies(SifenDbContext dbContext, Tenant tenant)
    {
        dbContext.Tenants.Add(tenant);
        TestFiscalSetup.Seed(dbContext, tenant.Id);
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
    }


    private sealed class ReadyTenantCertificateValidator : ITenantCertificateValidator
    {
        public Task<CertificateValidationResult> ValidateAsync(TenantCertificateMetadata? metadata, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new CertificateValidationResult(
                true,
                "Certificate loaded and validated.",
                [new SecretCheckResult("certificate.private_key", SecretStatus.Present, "OK")]));
        }
    }

    private sealed class MissingTenantCertificateValidator : ITenantCertificateValidator
    {
        public Task<CertificateValidationResult> ValidateAsync(TenantCertificateMetadata? metadata, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new CertificateValidationResult(
                false,
                "Certificate metadata is missing.",
                [new SecretCheckResult("certificate.metadata", SecretStatus.Missing, "Certificate metadata is missing.")]));
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

    private sealed class FakeSubmissionGateway : ISifenSubmissionGateway
    {
        public Task<SifenSubmissionResult> SendToSifenAsync(SendToSifenCommand command, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new SifenSubmissionResult(
                true,
                "https://sifen-test.set.gov.py/de/ws/sync/recibe.wsdl?wsdl",
                "123456789012345",
                $$"""
                <soapenv:Envelope xmlns:soapenv="http://schemas.xmlsoap.org/soap/envelope/" xmlns:sif="http://ekuatia.set.gov.py/sifen/xsd">
                  <soapenv:Body>
                    <sif:rRetEnviDe>
                      <sif:rProtDe>
                        <sif:Id>{{command.Cdc}}</sif:Id>
                        <sif:dFecProc>2026-04-25T12:00:00</sif:dFecProc>
                        <sif:dEstRes>Aprobado</sif:dEstRes>
                        <sif:dProtAut>123456789012345</sif:dProtAut>
                        <sif:gResProc>
                          <sif:dCodRes>0300</sif:dCodRes>
                          <sif:dMsgRes>Aprobado</sif:dMsgRes>
                        </sif:gResProc>
                      </sif:rProtDe>
                    </sif:rRetEnviDe>
                  </soapenv:Body>
                </soapenv:Envelope>
                """));
        }
    }

    private sealed class CountingSubmissionGateway : ISifenSubmissionGateway
    {
        public int SendCount { get; private set; }

        public Task<SifenSubmissionResult> SendToSifenAsync(SendToSifenCommand command, CancellationToken cancellationToken = default)
        {
            SendCount++;
            return Task.FromResult(new SifenSubmissionResult(true, "https://example.test", null, "<ok />"));
        }
    }


    private sealed class FakeResponseParser : ISifenResponseParser
    {
        public ParsedSifenResponse ParseResponse(string? rawResponse)
        {
            return new ParsedSifenResponse(
                SifenResponseOutcome.Approved,
                SifenDocumentStatus.Accepted,
                true,
                true,
                null,
                "123456789012345",
                "0300",
                "Factura electronica aprobada por SIFEN.",
                "Aprobado");
        }
    }

    private sealed class DefaultParser : ISifenResponseParser
    {
        public ParsedSifenResponse ParseResponse(string? rawResponse)
        {
            return new Infrastructure.Sifen.DefaultSifenResponseParser().ParseResponse(rawResponse);
        }
    }

    private sealed class FailingSubmissionGateway : ISifenSubmissionGateway
    {
        public Task<SifenSubmissionResult> SendToSifenAsync(SendToSifenCommand command, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new SifenSubmissionResult(
                false,
                "https://sifen-test.set.gov.py/de/ws/sync/recibe.wsdl",
                null,
                "SOAP_TIMEOUT|Request timed out.",
                "<soapenv:Envelope />",
                "SOAP_TIMEOUT",
                "SIFEN SOAP request timed out.",
                null,
                false));
        }
    }

    private sealed class RejectedSubmissionGateway : ISifenSubmissionGateway
    {
        public Task<SifenSubmissionResult> SendToSifenAsync(SendToSifenCommand command, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new SifenSubmissionResult(
                false,
                "https://sifen-test.set.gov.py/de/ws/sync/recibe.wsdl",
                null,
                $$"""
                <soapenv:Envelope xmlns:soapenv="http://schemas.xmlsoap.org/soap/envelope/" xmlns:sif="http://ekuatia.set.gov.py/sifen/xsd">
                  <soapenv:Body>
                    <sif:rRetEnviDe>
                      <sif:rProtDe>
                        <sif:Id>{{command.Cdc}}</sif:Id>
                        <sif:dEstRes>Rechazado</sif:dEstRes>
                        <sif:gResProc>
                          <sif:dCodRes>0500</sif:dCodRes>
                          <sif:dMsgRes>Documento rechazado</sif:dMsgRes>
                        </sif:gResProc>
                      </sif:rProtDe>
                    </sif:rRetEnviDe>
                  </soapenv:Body>
                </soapenv:Envelope>
                """));
        }
    }

    private sealed class StubOperationalReadinessReporter : IOperationalReadinessReporter
    {
        private readonly string _transportMode;
        private readonly bool _readyForInternalValidation;
        private readonly bool _readyForSifenTestAttempt;
        private readonly IReadOnlyCollection<TenantFeOperationalCheck> _missingChecks;

        public StubOperationalReadinessReporter(
            string transportMode = "Diagnostic",
            bool readyForInternalValidation = true,
            bool readyForSifenTestAttempt = true,
            IReadOnlyCollection<TenantFeOperationalCheck>? missingChecks = null)
        {
            _transportMode = transportMode;
            _readyForInternalValidation = readyForInternalValidation;
            _readyForSifenTestAttempt = readyForSifenTestAttempt;
            _missingChecks = missingChecks ?? [];
        }

        public Task<IReadOnlyCollection<OperationalDependencyStatus>> GetSnapshotAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyCollection<OperationalDependencyStatus>>([]);

        public Task<TenantFeOperationalDiagnostic> GetTenantFeDiagnosticAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Build(Guid.Empty, SifenEnvironmentType.Test));

        public Task<TenantFeOperationalDiagnostic> GetTenantFeDiagnosticAsync(Guid tenantId, CancellationToken cancellationToken = default)
            => Task.FromResult(Build(tenantId, SifenEnvironmentType.Test));

        public Task<TenantFeOperationalDiagnostic> GetTenantFeDiagnosticAsync(Guid tenantId, SifenEnvironmentType environment, CancellationToken cancellationToken = default)
            => Task.FromResult(Build(tenantId, environment));

        public Task<TenantFeOperationalDiagnostic> GetTenantFeDiagnosticAsync(SifenEnvironmentType environment, CancellationToken cancellationToken = default)
            => Task.FromResult(Build(Guid.Empty, environment));

        private TenantFeOperationalDiagnostic Build(Guid tenantId, SifenEnvironmentType environment)
        {
            return new TenantFeOperationalDiagnostic(
                tenantId,
                environment.ToString(),
                _transportMode,
                _readyForInternalValidation,
                _readyForSifenTestAttempt,
                "stub",
                _missingChecks.Where(check => !check.IsReady).Select(check => check.Message).ToArray(),
                [],
                _missingChecks,
                null,
                null);
        }
    }

    private sealed class DevelopmentEnvironmentScope : IDisposable
    {
        private readonly string? _previousAspNetCoreEnvironment;
        private readonly string? _previousDotNetEnvironment;

        public DevelopmentEnvironmentScope()
        {
            _previousAspNetCoreEnvironment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
            _previousDotNetEnvironment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
            Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "Development");
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", _previousAspNetCoreEnvironment);
            Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", _previousDotNetEnvironment);
        }
    }

    private sealed class NullAuditTrail : IAuditTrail
    {
        public Task RecordAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class ListAuditTrail : IAuditTrail
    {
        public List<AuditEvent> Events { get; } = [];

        public Task RecordAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
        {
            Events.Add(auditEvent);
            return Task.CompletedTask;
        }
    }
}
