using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SifenInvoicing.Application.Auditing;
using SifenInvoicing.Application.Diagnostics;
using SifenInvoicing.Application.Invoices;
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

public sealed class InvoiceServiceTests
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
        dbContext.TaxpayerProfiles.Add(TaxpayerProfile.Create(tenantId, "80012345", "6", "ACME Paraguay SA"));
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
            new FacturaXmlGenerator(),
            new PassThroughFacturaXmlPreSubmissionValidator(),
            new InvoiceKudePdfRenderer(),
            new ReadyTenantCertificateValidator(),
            new FakeXmlDocumentSigner(),
            new StubOperationalReadinessReporter(),
            submissionGateway,
            new FakeResponseParser(),
            CreateConfiguration(),
            new NullAuditTrail(),
            new SystemClock());

        var result = await service.CreateAsync(new CreateInvoiceCommand(
            SifenEnvironmentType.Test,
            "Factura electrónica",
            "001",
            "001",
            "0000001",
            "123456789",
            new DateOnly(2026, 4, 25),
            "Asuncion 123",
            null,
            "Cliente Demo",
            InvoiceReceiverDocumentType.Ruc,
            "80099999",
            null,
            null,
            null,
            InvoiceCurrency.PYG,
            InvoiceSaleCondition.Cash,
            1,
            "1",
            "1",
            [
                new CreateInvoiceItemCommand("Servicio mensual", 1, 100000m, 10)
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
    public async Task CreateAsync_ShouldPersistInternalValidationFailed_WhenLocalXsdValidationFailsInDiagnosticMode()
    {
        var tenant = Tenant.CreateSharedDatabaseTenant("acme-xsd", "ACME XSD");
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
        dbContext.TaxpayerProfiles.Add(TaxpayerProfile.Create(tenantId, "80012345", "6", "ACME Paraguay SA"));
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
            new FacturaXmlGenerator(),
            new FailingFacturaXmlPreSubmissionValidator("XSD root path is invalid."),
            new InvoiceKudePdfRenderer(),
            new ReadyTenantCertificateValidator(),
            new FakeXmlDocumentSigner(),
            new StubOperationalReadinessReporter(),
            submissionGateway,
            new FakeResponseParser(),
            CreateConfiguration(),
            new NullAuditTrail(),
            new SystemClock());

        var result = await service.CreateAsync(new CreateInvoiceCommand(
            SifenEnvironmentType.Test,
            "Factura electrónica",
            "001",
            "001",
            "0000001",
            "123456789",
            new DateOnly(2026, 4, 25),
            "Asuncion 123",
            null,
            "Cliente Demo",
            InvoiceReceiverDocumentType.Ruc,
            "80099999",
            null,
            null,
            null,
            InvoiceCurrency.PYG,
            InvoiceSaleCondition.Cash,
            1,
            "1",
            "1",
            [
                new CreateInvoiceItemCommand("Servicio mensual", 1, 100000m, 10)
            ]));

        var stored = await service.GetByIdAsync(result.Id);

        Assert.NotNull(stored);
        Assert.Equal(SifenDocumentStatus.InternalValidationFailed, stored!.Status);
        Assert.Equal("INTERNAL_VALIDATION_XSD_FAILED", stored.StatusCode);
        Assert.Contains("XSD root path is invalid.", stored.StatusMessage, StringComparison.Ordinal);
        Assert.Equal(0, submissionGateway.SendCount);
        Assert.Contains(stored.Logs, log => log.EventType == "internal.validation.failed");
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
            new FacturaXmlGenerator(),
            new PassThroughFacturaXmlPreSubmissionValidator(),
            new InvoiceKudePdfRenderer(),
            new ReadyTenantCertificateValidator(),
            new FakeXmlDocumentSigner(),
            new StubOperationalReadinessReporter(transportMode: "Live"),
            new FakeSubmissionGateway(),
            new FakeResponseParser(),
            CreateConfiguration(),
            new NullAuditTrail(),
            new SystemClock());

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
            new FacturaXmlGenerator(),
            new PassThroughFacturaXmlPreSubmissionValidator(),
            new InvoiceKudePdfRenderer(),
            new ReadyTenantCertificateValidator(),
            new FakeXmlDocumentSigner(),
            new StubOperationalReadinessReporter(transportMode: "Live"),
            new FakeSubmissionGateway(),
            new FakeResponseParser(),
            CreateConfiguration(),
            new NullAuditTrail(),
            new SystemClock());

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
        dbContext.TaxpayerProfiles.Add(TaxpayerProfile.Create(tenantId, "80012345", "6", "ACME Paraguay SA"));
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
            new FacturaXmlGenerator(),
            new PassThroughFacturaXmlPreSubmissionValidator(),
            new InvoiceKudePdfRenderer(),
            new ReadyTenantCertificateValidator(),
            new FakeXmlDocumentSigner(),
            new StubOperationalReadinessReporter(transportMode: "Live"),
            new FakeSubmissionGateway(),
            new FakeResponseParser(),
            CreateConfiguration(),
            new NullAuditTrail(),
            new SystemClock());

        await service.CreateAsync(new CreateInvoiceCommand(
            SifenEnvironmentType.Test,
            "Factura electrónica",
            "001",
            "001",
            "0000007",
            "123456789",
            new DateOnly(2026, 4, 25),
            "Asuncion 123",
            null,
            "Cliente Demo",
            InvoiceReceiverDocumentType.Ruc,
            "80099999",
            null,
            null,
            null,
            InvoiceCurrency.PYG,
            InvoiceSaleCondition.Cash,
            1,
            "1",
            "1",
            [new CreateInvoiceItemCommand("Servicio mensual", 1, 100000m, 10)]));

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
    public async Task CreateAsync_ShouldFailBeforeSending_WhenGeneratedTotalsAreInconsistent()
    {
        var tenant = Tenant.CreateSharedDatabaseTenant("acme-2", "ACME 2");
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
        dbContext.TaxpayerProfiles.Add(TaxpayerProfile.Create(tenantId, "80012345", "6", "ACME Paraguay SA"));
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

        var gateway = new CountingSubmissionGateway();
        var service = new EfInvoiceService(
            dbContext,
            tenantAccessor,
            new InconsistentFacturaXmlGenerator(),
            new PassThroughFacturaXmlPreSubmissionValidator(),
            new InvoiceKudePdfRenderer(),
            new ReadyTenantCertificateValidator(),
            new FakeXmlDocumentSigner(),
            new StubOperationalReadinessReporter(),
            gateway,
            new FakeResponseParser(),
            CreateConfiguration(),
            new NullAuditTrail(),
            new SystemClock());

        var exception = await Assert.ThrowsAsync<DomainException>(() => service.CreateAsync(new CreateInvoiceCommand(
            SifenEnvironmentType.Test,
            "Factura electrónica",
            "001",
            "001",
            "0000001",
            "123456789",
            new DateOnly(2026, 4, 25),
            "Asuncion 123",
            null,
            "Cliente Demo",
            InvoiceReceiverDocumentType.Ruc,
            "80099999",
            null,
            null,
            null,
            InvoiceCurrency.PYG,
            InvoiceSaleCondition.Cash,
            1,
            "1",
            "1",
            [
                new CreateInvoiceItemCommand("Servicio mensual", 1, 100000m, 10)
            ])));

        Assert.Contains("totals are inconsistent", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, gateway.SendCount);
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
        dbContext.TaxpayerProfiles.Add(TaxpayerProfile.Create(tenantId, "80012345", "6", "ACME Paraguay SA"));
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
            new FacturaXmlGenerator(),
            new PassThroughFacturaXmlPreSubmissionValidator(),
            new InvoiceKudePdfRenderer(),
            new ReadyTenantCertificateValidator(),
            new FakeXmlDocumentSigner(),
            new StubOperationalReadinessReporter(transportMode: "Live"),
            new FailingSubmissionGateway(),
            new DefaultParser(),
            CreateConfiguration(),
            new NullAuditTrail(),
            new SystemClock());

        var result = await service.CreateAsync(new CreateInvoiceCommand(
            SifenEnvironmentType.Test,
            "Factura electrónica",
            "001",
            "001",
            "0000001",
            "123456789",
            new DateOnly(2026, 4, 25),
            "Asuncion 123",
            null,
            "Cliente Demo",
            InvoiceReceiverDocumentType.Ruc,
            "80099999",
            null,
            null,
            null,
            InvoiceCurrency.PYG,
            InvoiceSaleCondition.Cash,
            1,
            "1",
            "1",
            [
                new CreateInvoiceItemCommand("Servicio mensual", 1, 100000m, 10)
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
            new FacturaXmlGenerator(),
            new PassThroughFacturaXmlPreSubmissionValidator(),
            new InvoiceKudePdfRenderer(),
            new ReadyTenantCertificateValidator(),
            new FakeXmlDocumentSigner(),
            new StubOperationalReadinessReporter(transportMode: "Live"),
            new FakeSubmissionGateway(),
            new DefaultParser(),
            CreateConfiguration(),
            new NullAuditTrail(),
            new SystemClock());

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
            new FacturaXmlGenerator(),
            new PassThroughFacturaXmlPreSubmissionValidator(),
            new InvoiceKudePdfRenderer(),
            new ReadyTenantCertificateValidator(),
            new FakeXmlDocumentSigner(),
            new StubOperationalReadinessReporter(transportMode: "Live"),
            new FailingSubmissionGateway(),
            new DefaultParser(),
            CreateConfiguration(),
            new NullAuditTrail(),
            new SystemClock());

        var created = await CreateInvoiceAsync(failedService);

        var retryService = new EfInvoiceService(
            dbContext,
            tenantAccessor,
            new FacturaXmlGenerator(),
            new PassThroughFacturaXmlPreSubmissionValidator(),
            new InvoiceKudePdfRenderer(),
            new ReadyTenantCertificateValidator(),
            new FakeXmlDocumentSigner(),
            new StubOperationalReadinessReporter(transportMode: "Live"),
            new FakeSubmissionGateway(),
            new DefaultParser(),
            CreateConfiguration(),
            new NullAuditTrail(),
            new SystemClock());

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
            new FacturaXmlGenerator(),
            new PassThroughFacturaXmlPreSubmissionValidator(),
            new InvoiceKudePdfRenderer(),
            new ReadyTenantCertificateValidator(),
            new FakeXmlDocumentSigner(),
            new StubOperationalReadinessReporter(transportMode: "Live"),
            new RejectedSubmissionGateway(),
            new DefaultParser(),
            CreateConfiguration(),
            new NullAuditTrail(),
            new SystemClock());

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
            new FacturaXmlGenerator(),
            new PassThroughFacturaXmlPreSubmissionValidator(),
            new InvoiceKudePdfRenderer(),
            new ReadyTenantCertificateValidator(),
            new FakeXmlDocumentSigner(),
            new StubOperationalReadinessReporter(transportMode: "Live"),
            new FailingSubmissionGateway(),
            new DefaultParser(),
            CreateConfiguration(),
            new NullAuditTrail(),
            new SystemClock());

        var created = await CreateInvoiceAsync(failedService);

        var retryService = new EfInvoiceService(
            dbContext,
            tenantAccessor,
            new FacturaXmlGenerator(),
            new PassThroughFacturaXmlPreSubmissionValidator(),
            new InvoiceKudePdfRenderer(),
            new ReadyTenantCertificateValidator(),
            new FakeXmlDocumentSigner(),
            new StubOperationalReadinessReporter(transportMode: "Live"),
            new FakeSubmissionGateway(),
            new DefaultParser(),
            CreateConfiguration(),
            auditTrail,
            new SystemClock());

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
            new FacturaXmlGenerator(),
            new PassThroughFacturaXmlPreSubmissionValidator(),
            new InvoiceKudePdfRenderer(),
            new ReadyTenantCertificateValidator(),
            new FakeXmlDocumentSigner(),
            new StubOperationalReadinessReporter(transportMode: "Live", readyForSifenTestAttempt: false),
            new FakeSubmissionGateway(),
            new FakeResponseParser(),
            CreateConfiguration(),
            new NullAuditTrail(),
            new SystemClock());

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
        dbContext.TaxpayerProfiles.Add(TaxpayerProfile.Create(tenantId, "80012345", "6", "ACME Paraguay SA"));
        await dbContext.SaveChangesAsync();

        var submissionGateway = new CountingSubmissionGateway();
        var service = new EfInvoiceService(
            dbContext,
            tenantAccessor,
            new FacturaXmlGenerator(),
            new FailingFacturaXmlPreSubmissionValidator("TODO: Official XSD path for FE TipoDoc 01 is not configured. XML validation must be completed before signing or sending."),
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
            new SystemClock());

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
            SifenEnvironmentType.Test,
            "Factura electrónica",
            "001",
            "001",
            "0000001",
            "123456789",
            new DateOnly(2026, 4, 25),
            "Asuncion 123",
            null,
            "Cliente Demo",
            InvoiceReceiverDocumentType.Ruc,
            "80099999",
            null,
            null,
            null,
            InvoiceCurrency.PYG,
            InvoiceSaleCondition.Cash,
            1,
            "1",
            "1",
            [
                new CreateInvoiceItemCommand("Servicio mensual", 1, 100000m, 10)
            ]));
    }

    private static IConfiguration CreateConfiguration(bool allowUnsignedInternalValidation = false)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Sifen:Development:AllowUnsignedInternalValidation"] = allowUnsignedInternalValidation.ToString()
            })
            .Build();
    }

    private static void SeedRetryDependencies(SifenDbContext dbContext, Tenant tenant)
    {
        dbContext.Tenants.Add(tenant);
        dbContext.TaxpayerProfiles.Add(TaxpayerProfile.Create(tenant.Id, "80012345", "6", "ACME Paraguay SA"));
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

    private sealed class PassThroughFacturaXmlPreSubmissionValidator : IFacturaXmlPreSubmissionValidator
    {
        public Task ValidateTipoDoc01Async(string xml, string cdc, Guid tenantId, SifenEnvironmentType environment, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(xml) || string.IsNullOrWhiteSpace(cdc))
            {
                throw new DomainException("xml and cdc are required.");
            }

            return Task.CompletedTask;
        }
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

    private sealed class InconsistentFacturaXmlGenerator : IFacturaXmlGenerator
    {
        public GeneratedFacturaXmlResult GenerateFacturaXML(GenerateFacturaXmlInput input)
        {
            var valid = new FacturaXmlGenerator().GenerateFacturaXML(input);
            var inconsistentXml = valid.Xml.Replace(
                "<dTotGralOpe>100000</dTotGralOpe>",
                "<dTotGralOpe>99999</dTotGralOpe>",
                StringComparison.Ordinal);

            return valid with { Xml = inconsistentXml };
        }
    }

    private sealed class FailingFacturaXmlPreSubmissionValidator(string message) : IFacturaXmlPreSubmissionValidator
    {
        public Task ValidateTipoDoc01Async(string xml, string cdc, Guid tenantId, SifenEnvironmentType environment, CancellationToken cancellationToken = default)
            => throw new DomainException(message);
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
