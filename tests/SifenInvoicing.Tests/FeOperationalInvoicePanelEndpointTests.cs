using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SifenInvoicing.Api.Endpoints;
using SifenInvoicing.Application.Auditing;
using SifenInvoicing.Application.Diagnostics;
using SifenInvoicing.Application.Invoices;
using SifenInvoicing.Application.Operations;
using SifenInvoicing.Application.Security;
using SifenInvoicing.Application.Sifen;
using SifenInvoicing.Application.Tenancy;
using SifenInvoicing.Application.XmlSigning;
using SifenInvoicing.Domain.Documents;
using SifenInvoicing.Domain.Tenants;
using SifenInvoicing.Infrastructure.Diagnostics;
using SifenInvoicing.Infrastructure.Invoices;
using SifenInvoicing.Infrastructure.Persistence;
using SifenInvoicing.Infrastructure.Tenancy;

namespace SifenInvoicing.Tests;

public sealed class FeOperationalInvoicePanelEndpointTests
{
    private const string CdcTenantA = "01800123456001001000012311123456789202604251";
    private const string CdcTenantB = "01800123456001001000012311123456789202604252";

    [Fact]
    public async Task GetTenantInvoicesAsync_ShouldReturnOnlyInvoicesForTenant()
    {
        var fixture = await CreateFixtureAsync();
        SeedInvoice(fixture.DbContext, fixture.TenantA.Id, "0000001", "Cliente A", FeInvoiceInternalStatus.DRAFT);
        SeedInvoice(fixture.DbContext, fixture.TenantB.Id, "0000002", "Cliente B", FeInvoiceInternalStatus.DRAFT);
        await fixture.DbContext.SaveChangesAsync();
        SetTenant(fixture.TenantAccessor, fixture.TenantA.Id);

        var result = await InvoiceEndpoints.GetTenantInvoicesAsync(
            fixture.TenantA.Id,
            null,
            null,
            null,
            null,
            1,
            20,
            fixture.TenantAccessor,
            fixture.DbContext,
            CancellationToken.None);

        var ok = Assert.IsAssignableFrom<IValueHttpResult>(result);
        var page = Assert.IsType<FeTenantInvoicePage>(ok.Value);
        var item = Assert.Single(page.Items);
        Assert.Equal(fixture.TenantA.Id, item.TenantId);
        Assert.Equal("Cliente A", item.CustomerName);
        Assert.Equal(1, page.TotalCount);
        Assert.Equal(1, page.TotalPages);
    }

    [Fact]
    public async Task GetTenantInvoicesAsync_ShouldFilterByInternalStatus()
    {
        var fixture = await CreateFixtureAsync();
        SeedInvoice(fixture.DbContext, fixture.TenantA.Id, "0000001", "Cliente A", FeInvoiceInternalStatus.DRAFT);
        SeedInvoice(fixture.DbContext, fixture.TenantA.Id, "0000002", "Cliente B", FeInvoiceInternalStatus.VALIDATED_TEST);
        await fixture.DbContext.SaveChangesAsync();
        SetTenant(fixture.TenantAccessor, fixture.TenantA.Id);

        var result = await InvoiceEndpoints.GetTenantInvoicesAsync(
            fixture.TenantA.Id,
            "VALIDATED_TEST",
            null,
            null,
            null,
            1,
            20,
            fixture.TenantAccessor,
            fixture.DbContext,
            CancellationToken.None);

        var ok = Assert.IsAssignableFrom<IValueHttpResult>(result);
        var page = Assert.IsType<FeTenantInvoicePage>(ok.Value);
        var item = Assert.Single(page.Items);
        Assert.Equal("0000002", item.InvoiceNumber);
        Assert.Equal("VALIDATED_TEST", item.InternalStatus);
    }

    [Fact]
    public async Task GetInvoiceDetailAsync_ShouldReturnRecentEventsAndLogs()
    {
        var fixture = await CreateFixtureAsync();
        var invoice = SeedInvoice(fixture.DbContext, fixture.TenantA.Id, "0000001", "Cliente A", FeInvoiceInternalStatus.VALIDATED_TEST);
        fixture.DbContext.FeInvoiceEvents.Add(FeInvoiceEvent.Create(
            fixture.TenantA.Id,
            invoice.Id,
            invoice.CorrelationId!,
            "DRAFT",
            "VALIDATED_TEST",
            "test.prepare.validated",
            "Factura validada en TEST.",
            null,
            DateTimeOffset.UtcNow));
        fixture.DbContext.FeTenantLogs.Add(FeTenantLog.Create(
            fixture.TenantA.Id,
            invoice.Id,
            invoice.CorrelationId,
            FeTenantLogLevel.INFO,
            "fe.test.flow",
            "Log tecnico de prueba.",
            null,
            DateTimeOffset.UtcNow));
        await fixture.DbContext.SaveChangesAsync();
        SetTenant(fixture.TenantAccessor, fixture.TenantA.Id);

        var result = await InvoiceEndpoints.GetInvoiceDetailAsync(
            invoice.Id,
            fixture.TenantAccessor,
            fixture.InvoiceService,
            CancellationToken.None);

        var ok = Assert.IsAssignableFrom<IValueHttpResult>(result);
        var detail = Assert.IsType<InvoiceDetail>(ok.Value);
        Assert.NotEmpty(detail.Events);
        Assert.NotEmpty(detail.TenantLogs);
    }

    [Fact]
    public async Task GetTenantInvoicesAsync_ShouldApplyPageSize()
    {
        var fixture = await CreateFixtureAsync();
        SeedInvoice(fixture.DbContext, fixture.TenantA.Id, "0000001", "Cliente A", FeInvoiceInternalStatus.DRAFT);
        SeedInvoice(fixture.DbContext, fixture.TenantA.Id, "0000002", "Cliente B", FeInvoiceInternalStatus.DRAFT);
        SeedInvoice(fixture.DbContext, fixture.TenantA.Id, "0000003", "Cliente C", FeInvoiceInternalStatus.DRAFT);
        await fixture.DbContext.SaveChangesAsync();
        SetTenant(fixture.TenantAccessor, fixture.TenantA.Id);

        var result = await InvoiceEndpoints.GetTenantInvoicesAsync(
            fixture.TenantA.Id,
            null,
            null,
            null,
            null,
            1,
            2,
            fixture.TenantAccessor,
            fixture.DbContext,
            CancellationToken.None);

        var ok = Assert.IsAssignableFrom<IValueHttpResult>(result);
        var page = Assert.IsType<FeTenantInvoicePage>(ok.Value);
        Assert.Equal(3, page.TotalCount);
        Assert.Equal(2, page.PageSize);
        Assert.Equal(2, page.TotalPages);
        Assert.Equal(2, page.Items.Count);
    }

    [Fact]
    public async Task GetTenantInvoicesAsync_ShouldApplyFilterAndPaginationTogether()
    {
        var fixture = await CreateFixtureAsync();
        SeedInvoice(fixture.DbContext, fixture.TenantA.Id, "0000001", "Cliente Uno", FeInvoiceInternalStatus.DRAFT);
        SeedInvoice(fixture.DbContext, fixture.TenantA.Id, "0000002", "Cliente Uno", FeInvoiceInternalStatus.DRAFT);
        SeedInvoice(fixture.DbContext, fixture.TenantA.Id, "0000003", "Cliente Uno", FeInvoiceInternalStatus.VALIDATED_TEST);
        await fixture.DbContext.SaveChangesAsync();
        SetTenant(fixture.TenantAccessor, fixture.TenantA.Id);

        var result = await InvoiceEndpoints.GetTenantInvoicesAsync(
            fixture.TenantA.Id,
            "DRAFT",
            "Cliente Uno",
            null,
            null,
            2,
            1,
            fixture.TenantAccessor,
            fixture.DbContext,
            CancellationToken.None);

        var ok = Assert.IsAssignableFrom<IValueHttpResult>(result);
        var page = Assert.IsType<FeTenantInvoicePage>(ok.Value);
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(2, page.Page);
        Assert.Single(page.Items);
        Assert.Equal("DRAFT", page.Items.Single().InternalStatus);
    }

    [Fact]
    public async Task GetInvoiceDetailAsync_ShouldNotExposeInvoiceFromOtherTenant()
    {
        var fixture = await CreateFixtureAsync();
        SeedInvoice(fixture.DbContext, fixture.TenantA.Id, "0000001", "Cliente A", FeInvoiceInternalStatus.DRAFT);
        var otherTenantInvoice = SeedInvoice(fixture.DbContext, fixture.TenantB.Id, "0000002", "Cliente B", FeInvoiceInternalStatus.DRAFT);
        await fixture.DbContext.SaveChangesAsync();
        SetTenant(fixture.TenantAccessor, fixture.TenantA.Id);

        var result = await InvoiceEndpoints.GetInvoiceDetailAsync(
            otherTenantInvoice.Id,
            fixture.TenantAccessor,
            fixture.InvoiceService,
            CancellationToken.None);

        Assert.IsType<NotFound>(result);
    }

    [Fact]
    public async Task GetInvoiceDetailAsync_ShouldSuppressSensitiveTechnicalDetails()
    {
        var fixture = await CreateFixtureAsync();
        var invoice = SeedInvoice(fixture.DbContext, fixture.TenantA.Id, "0000001", "Cliente A", FeInvoiceInternalStatus.TEST_ERROR);
        fixture.DbContext.FeInvoiceEvents.Add(FeInvoiceEvent.Create(
            fixture.TenantA.Id,
            invoice.Id,
            invoice.CorrelationId!,
            "GENERATED",
            "TEST_ERROR",
            "test.prepare.error",
            "Factura con error de prueba.",
            "certificate password missing",
            DateTimeOffset.UtcNow));
        fixture.DbContext.FeTenantLogs.Add(FeTenantLog.Create(
            fixture.TenantA.Id,
            invoice.Id,
            invoice.CorrelationId,
            FeTenantLogLevel.ERROR,
            "fe.test.flow",
            "Detalle técnico sensible.",
            "csc secret exposed",
            DateTimeOffset.UtcNow));
        await fixture.DbContext.SaveChangesAsync();
        SetTenant(fixture.TenantAccessor, fixture.TenantA.Id);

        var result = await InvoiceEndpoints.GetInvoiceDetailAsync(
            invoice.Id,
            fixture.TenantAccessor,
            fixture.InvoiceService,
            CancellationToken.None);

        var ok = Assert.IsAssignableFrom<IValueHttpResult>(result);
        var detail = Assert.IsType<InvoiceDetail>(ok.Value);
        Assert.All(detail.Events, item => Assert.DoesNotContain("password", item.TechnicalDetail ?? string.Empty, StringComparison.OrdinalIgnoreCase));
        Assert.All(detail.TenantLogs, item => Assert.DoesNotContain("secret", item.TechnicalDetail ?? string.Empty, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetTenantInvoicesAsync_ShouldExposeCdcAndSifenStates()
    {
        var fixture = await CreateFixtureAsync();
        var invoice = SeedInvoice(fixture.DbContext, fixture.TenantA.Id, "0000001", "Cliente A", FeInvoiceInternalStatus.DRAFT, CdcTenantA);
        invoice.SetSifenStates(SifenTransmissionState.Delivered, SifenFiscalState.ApprovedWithObservations);
        SeedInvoice(fixture.DbContext, fixture.TenantB.Id, "0000002", "Cliente B", FeInvoiceInternalStatus.DRAFT, CdcTenantB);
        await fixture.DbContext.SaveChangesAsync();
        SetTenant(fixture.TenantAccessor, fixture.TenantA.Id);

        var result = await InvoiceEndpoints.GetTenantInvoicesAsync(
            fixture.TenantA.Id,
            null,
            null,
            null,
            null,
            1,
            20,
            fixture.TenantAccessor,
            fixture.DbContext,
            CancellationToken.None);

        var ok = Assert.IsAssignableFrom<IValueHttpResult>(result);
        var page = Assert.IsType<FeTenantInvoicePage>(ok.Value);
        var item = Assert.Single(page.Items);
        Assert.Equal(CdcTenantA, item.Cdc);
        Assert.Equal("corr-0000001", item.CorrelationId);
        Assert.Equal(SifenTransmissionState.Delivered, item.TransmissionState);
        Assert.Equal(SifenFiscalState.ApprovedWithObservations, item.FiscalState);

        var json = JsonSerializer.Serialize(page, ApiJsonOptions());
        Assert.Contains($"\"cdc\":\"{CdcTenantA}\"", json);
        Assert.Contains("\"transmissionState\":\"Delivered\"", json);
        Assert.Contains("\"fiscalState\":\"ApprovedWithObservations\"", json);
        Assert.DoesNotContain(CdcTenantB, json);
    }

    [Fact]
    public async Task GetTenantInvoicesAsync_ShouldForbidRouteOfOtherTenant()
    {
        var fixture = await CreateFixtureAsync();
        SeedInvoice(fixture.DbContext, fixture.TenantB.Id, "0000002", "Cliente B", FeInvoiceInternalStatus.DRAFT, CdcTenantB);
        await fixture.DbContext.SaveChangesAsync();
        SetTenant(fixture.TenantAccessor, fixture.TenantA.Id);

        var result = await InvoiceEndpoints.GetTenantInvoicesAsync(
            fixture.TenantB.Id,
            null,
            null,
            null,
            null,
            1,
            20,
            fixture.TenantAccessor,
            fixture.DbContext,
            CancellationToken.None);

        Assert.IsType<ForbidHttpResult>(result);
    }

    [Fact]
    public async Task GetInvoiceDetailAsync_ShouldExposeCdcAndSifenStatesKeepingExistingFields()
    {
        var fixture = await CreateFixtureAsync();
        var invoice = SeedInvoice(fixture.DbContext, fixture.TenantA.Id, "0000001", "Cliente A", FeInvoiceInternalStatus.VALIDATED_TEST, CdcTenantA);
        invoice.MarkAccepted("PROT-TEST-1", "TEST-CODE", "Mensaje de prueba", null, DateTimeOffset.UtcNow);
        invoice.SetSifenStates(SifenTransmissionState.Delivered, SifenFiscalState.Approved);
        await fixture.DbContext.SaveChangesAsync();
        SetTenant(fixture.TenantAccessor, fixture.TenantA.Id);

        var result = await InvoiceEndpoints.GetInvoiceDetailAsync(
            invoice.Id,
            fixture.TenantAccessor,
            fixture.InvoiceService,
            CancellationToken.None);

        var ok = Assert.IsAssignableFrom<IValueHttpResult>(result);
        var detail = Assert.IsType<InvoiceDetail>(ok.Value);
        Assert.Equal(CdcTenantA, detail.Cdc);
        Assert.Equal(SifenTransmissionState.Delivered, detail.TransmissionState);
        Assert.Equal(SifenFiscalState.Approved, detail.FiscalState);
        Assert.Equal(SifenDocumentStatus.Accepted, detail.Status);
        Assert.Equal("TEST-CODE", detail.StatusCode);
        Assert.Equal("Mensaje de prueba", detail.StatusMessage);
        Assert.Equal("PROT-TEST-1", detail.SifenTrackingId);
        Assert.Equal("corr-0000001", detail.CorrelationId);
        Assert.Equal("VALIDATED_TEST", detail.InternalStatus);

        var json = JsonSerializer.Serialize(detail, ApiJsonOptions());
        Assert.Contains("\"transmissionState\":\"Delivered\"", json);
        Assert.Contains("\"fiscalState\":\"Approved\"", json);
    }

    [Fact]
    public async Task GetSimpleInvoiceStatusAsync_ShouldNotExposeInvoiceFromOtherTenant()
    {
        var fixture = await CreateFixtureAsync();
        var otherTenantInvoice = SeedInvoice(fixture.DbContext, fixture.TenantB.Id, "0000002", "Cliente B", FeInvoiceInternalStatus.DRAFT, CdcTenantB);
        otherTenantInvoice.SetSifenStates(SifenTransmissionState.Delivered, SifenFiscalState.Approved);
        await fixture.DbContext.SaveChangesAsync();
        SetTenant(fixture.TenantAccessor, fixture.TenantA.Id);

        var result = await InvoiceEndpoints.GetSimpleInvoiceStatusAsync(
            CdcTenantB,
            fixture.InvoiceService,
            CancellationToken.None);

        Assert.IsType<NotFound>(result);
    }

    [Fact]
    public async Task GetSimpleInvoiceStatusAsync_ShouldExposeSifenStatesForOwnTenant()
    {
        var fixture = await CreateFixtureAsync();
        var invoice = SeedInvoice(fixture.DbContext, fixture.TenantB.Id, "0000002", "Cliente B", FeInvoiceInternalStatus.DRAFT, CdcTenantB);
        invoice.SetSifenStates(SifenTransmissionState.NotDelivered, SifenFiscalState.None);
        await fixture.DbContext.SaveChangesAsync();
        SetTenant(fixture.TenantAccessor, fixture.TenantB.Id);

        var result = await InvoiceEndpoints.GetSimpleInvoiceStatusAsync(
            CdcTenantB,
            fixture.InvoiceService,
            CancellationToken.None);

        var ok = Assert.IsAssignableFrom<IValueHttpResult>(result);
        var json = JsonSerializer.Serialize(ok.Value!, ApiJsonOptions());
        Assert.Contains($"\"cdc\":\"{CdcTenantB}\"", json);
        Assert.Contains("\"transmissionState\":\"NotDelivered\"", json);
        Assert.Contains("\"fiscalState\":\"None\"", json);
        Assert.Contains("\"sifenTrackingId\":null", json);
    }

    // Igual que la configuracion de la API (Program.cs): defaults web + JsonStringEnumConverter.
    private static JsonSerializerOptions ApiJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private static async Task<EndpointFixture> CreateFixtureAsync()
    {
        var tenantAccessor = new AsyncLocalTenantContextAccessor();
        var options = new DbContextOptionsBuilder<SifenDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var dbContext = new SifenDbContext(options, new SystemClock(), tenantAccessor);
        var tenantA = Tenant.CreateSharedDatabaseTenant("tenant-a", "Tenant A");
        var tenantB = Tenant.CreateSharedDatabaseTenant("tenant-b", "Tenant B");
        dbContext.Tenants.AddRange(tenantA, tenantB);
        await dbContext.SaveChangesAsync();

        var invoiceService = new EfInvoiceService(
            dbContext,
            tenantAccessor,
            DeTestKit.Builder(),
            DeTestKit.Xsd(),
            new InvoiceKudePdfRenderer(),
            new ReadyTenantCertificateValidator(),
            new FakeXmlDocumentSigner(),
            new StubOperationalReadinessReporter(),
            new FakeSubmissionGateway(),
            new FakeResponseParser(),
            CreateConfiguration(),
            new NullAuditTrail(),
            new SystemClock(),
            new SifenInvoicing.Infrastructure.Numbering.EfNumberingService(dbContext),
            new TestFiscalClock(), new FakeQrAttacher());

        return new EndpointFixture(tenantA, tenantB, tenantAccessor, dbContext, invoiceService);
    }

    private static SifenDocument SeedInvoice(
        SifenDbContext dbContext,
        Guid tenantId,
        string number,
        string customerName,
        FeInvoiceInternalStatus internalStatus,
        string cdc = CdcTenantA)
    {
        var invoice = SifenDocument.CreateInvoice(
            tenantId,
            SifenEnvironmentType.Test,
            cdc,
            "Factura electrónica",
            number,
            "001",
            "001",
            "PYG",
            "Contado",
            customerName,
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
            "<xml />",
            "TEST-01800123456001001000012311123456789202604251",
            "QR TEST - no válido para SET",
            false,
            DateTimeOffset.UtcNow);
        invoice.EnsureCorrelationId($"corr-{number}");
        invoice.SetInternalStatus(internalStatus);
        dbContext.Documents.Add(invoice);
        return invoice;
    }

    private static void SetTenant(ITenantContextAccessor tenantAccessor, Guid tenantId)
    {
        tenantAccessor.SetCurrent(new TenantContext
        {
            TenantId = tenantId.ToString(),
            ResolvedTenantId = tenantId,
            IsResolved = true
        });
    }

    private static IConfiguration CreateConfiguration()
        => new ConfigurationBuilder().AddInMemoryCollection(DeTestKit.DeDefaults).Build();


    private sealed class ReadyTenantCertificateValidator : ITenantCertificateValidator
    {
        public Task<CertificateValidationResult> ValidateAsync(TenantCertificateMetadata? metadata, CancellationToken cancellationToken = default)
            => Task.FromResult(new CertificateValidationResult(true, "OK", [new SecretCheckResult("certificate", SecretStatus.Present, "OK")]));
    }

    private sealed class FakeXmlDocumentSigner : IXmlDocumentSigner
    {
        public Task<SignedXmlDocumentResult> SignAsync(SignXmlDocumentCommand command, CancellationToken cancellationToken = default)
            => Task.FromResult(new SignedXmlDocumentResult(StructuralSignatureStub.Apply(command.Xml), command.DocumentId, "c14n", "rsa-sha256", "sha256", "enveloped"));
    }

    private sealed class FakeSubmissionGateway : ISifenSubmissionGateway
    {
        public Task<SifenSubmissionResult> SendToSifenAsync(SendToSifenCommand command, CancellationToken cancellationToken = default)
            => Task.FromResult(new SifenSubmissionResult(true, "test-endpoint", "track", "<xml />"));
    }

    private sealed class FakeResponseParser : ISifenResponseParser
    {
        public ParsedSifenResponse ParseResponse(string? rawResponse)
            => new(SifenResponseOutcome.Approved, SifenDocumentStatus.Accepted, true, true, null, "track", "0300", "OK", "OK");
    }

    private sealed class StubOperationalReadinessReporter : IOperationalReadinessReporter
    {
        public Task<IReadOnlyCollection<OperationalDependencyStatus>> GetSnapshotAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyCollection<OperationalDependencyStatus>>([]);

        public Task<TenantFeOperationalDiagnostic> GetTenantFeDiagnosticAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new TenantFeOperationalDiagnostic(Guid.Empty, "Test", "Diagnostic", true, false, "stub", [], [], [], null, null));

        public Task<TenantFeOperationalDiagnostic> GetTenantFeDiagnosticAsync(Guid tenantId, CancellationToken cancellationToken = default)
            => Task.FromResult(new TenantFeOperationalDiagnostic(tenantId, "Test", "Diagnostic", true, false, "stub", [], [], [], null, null));

        public Task<TenantFeOperationalDiagnostic> GetTenantFeDiagnosticAsync(Guid tenantId, SifenEnvironmentType environment, CancellationToken cancellationToken = default)
            => Task.FromResult(new TenantFeOperationalDiagnostic(tenantId, environment.ToString(), "Diagnostic", true, false, "stub", [], [], [], null, null));

        public Task<TenantFeOperationalDiagnostic> GetTenantFeDiagnosticAsync(SifenEnvironmentType environment, CancellationToken cancellationToken = default)
            => Task.FromResult(new TenantFeOperationalDiagnostic(Guid.Empty, environment.ToString(), "Diagnostic", true, false, "stub", [], [], [], null, null));
    }

    private sealed class NullAuditTrail : IAuditTrail
    {
        public Task RecordAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed record EndpointFixture(
        Tenant TenantA,
        Tenant TenantB,
        ITenantContextAccessor TenantAccessor,
        SifenDbContext DbContext,
        IInvoiceService InvoiceService);
}
