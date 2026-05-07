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

public sealed class InvoiceKudePdfTests
{
    [Fact]
    public async Task GenerateKudePdfAsync_ShouldGeneratePdfWithQr()
    {
        var fixture = await CreateFixtureAsync();
        SetTenant(fixture.TenantAccessor, fixture.TenantId);
        var invoiceId = await CreateInvoiceAsync(fixture.Service);
        SetTenant(fixture.TenantAccessor, fixture.TenantId);
        var invoice = await fixture.Service.GetByIdAsync(invoiceId);

        var pdf = await fixture.Service.GenerateKudePdfAsync(invoiceId);

        Assert.NotNull(pdf);
        Assert.True(pdf!.HasQr);
        Assert.NotNull(invoice);
        Assert.Equal(invoice!.Cdc, pdf.QrPayload);
        Assert.True(pdf.Content.Length > 0);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf.Content, 0, 4));
    }

    [Fact]
    public async Task DownloadKudeAsync_ShouldReturnPdfFile()
    {
        var fixture = await CreateFixtureAsync();
        SetTenant(fixture.TenantAccessor, fixture.TenantId);
        var invoiceId = await CreateInvoiceAsync(fixture.Service);
        SetTenant(fixture.TenantAccessor, fixture.TenantId);

        var result = await InvoiceEndpoints.DownloadKudeAsync(invoiceId, fixture.Service, CancellationToken.None);

        var fileResult = Assert.IsType<FileContentHttpResult>(result);
        Assert.Equal("application/pdf", fileResult.ContentType);
        Assert.NotNull(fileResult.FileDownloadName);
        Assert.True(fileResult.FileContents.Length > 0);
    }

    [Fact]
    public async Task DownloadXmlAsync_ShouldReturnXmlFile()
    {
        var fixture = await CreateFixtureAsync();
        SetTenant(fixture.TenantAccessor, fixture.TenantId);
        var invoiceId = await CreateInvoiceAsync(fixture.Service);
        SetTenant(fixture.TenantAccessor, fixture.TenantId);

        var result = await InvoiceEndpoints.DownloadXmlAsync(invoiceId, fixture.Service, CancellationToken.None);

        var fileResult = Assert.IsType<FileContentHttpResult>(result);
        Assert.Equal("application/xml", fileResult.ContentType);
        Assert.True(fileResult.FileContents.Length > 0);
    }

    [Fact]
    public async Task DownloadKudeAsync_ShouldReturnNotFound_WhenInvoiceDoesNotExist()
    {
        var fixture = await CreateFixtureAsync();
        SetTenant(fixture.TenantAccessor, fixture.TenantId);

        var result = await InvoiceEndpoints.DownloadKudeAsync(Guid.NewGuid(), fixture.Service, CancellationToken.None);

        Assert.IsType<NotFound>(result);
    }

    [Fact]
    public async Task GenerateKudePreviewHtmlAsync_ShouldReturnCodexaStandardHtml()
    {
        var fixture = await CreateFixtureAsync();
        SetTenant(fixture.TenantAccessor, fixture.TenantId);

        var result = await InvoiceEndpoints.GenerateKudePreviewHtmlAsync(
            new InvoiceEndpoints.KudePreviewRequest(
                "codexa-standard",
                "https://cdn.codexa.local/logo.png",
                "#2D9CDB",
                "#EAF6FD",
                "Consulte este comprobante en la SET",
                true,
                true),
            fixture.TenantAccessor,
            fixture.DbContext,
            new InvoiceKudePdfRenderer(),
            CancellationToken.None);

        var content = Assert.IsType<ContentHttpResult>(result);
        Assert.Contains("FACTURA ELECTRONICA", content.ResponseContent, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Vista previa con datos de prueba. No es comprobante válido.", content.ResponseContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateKudePreviewPdfAsync_ShouldReturnPdfFile()
    {
        var fixture = await CreateFixtureAsync();
        SetTenant(fixture.TenantAccessor, fixture.TenantId);

        var result = await InvoiceEndpoints.GenerateKudePreviewPdfAsync(
            new InvoiceEndpoints.KudePreviewRequest(
                "codexa-standard",
                null,
                "#2D9CDB",
                "#EAF6FD",
                "Consulte este comprobante en la SET",
                true,
                true),
            fixture.TenantAccessor,
            fixture.DbContext,
            new InvoiceKudePdfRenderer(),
            CancellationToken.None);

        var fileResult = Assert.IsType<FileContentHttpResult>(result);
        Assert.Equal("application/pdf", fileResult.ContentType);
        Assert.True(fileResult.FileContents.Length > 0);
    }

    private static async Task<Guid> CreateInvoiceAsync(IInvoiceService service)
    {
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

        return result.Id;
    }

    private static IConfiguration CreateConfiguration()
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection([])
            .Build();
    }

    private static async Task<TestFixture> CreateFixtureAsync()
    {
        var tenant = Tenant.CreateSharedDatabaseTenant("kude", "KUDE");
        var tenantAccessor = new AsyncLocalTenantContextAccessor();
        SetTenant(tenantAccessor, tenant.Id);

        var options = new DbContextOptionsBuilder<SifenDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var dbContext = new SifenDbContext(options, new SystemClock(), tenantAccessor);
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
        await dbContext.SaveChangesAsync();

        return new TestFixture(
            tenant.Id,
            tenantAccessor,
            dbContext,
            new EfInvoiceService(
            dbContext,
            tenantAccessor,
            new FacturaXmlGenerator(),
            new PassThroughFacturaXmlPreSubmissionValidator(),
            new InvoiceKudePdfRenderer(),
             new ReadyTenantCertificateValidator(),
             new FakeXmlDocumentSigner(),
             new StubOperationalReadinessReporter(),
             new FakeSubmissionGateway(),
             new FakeResponseParser(),
             CreateConfiguration(),
             new NullAuditTrail(),
             new SystemClock()));
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

    private sealed class PassThroughFacturaXmlPreSubmissionValidator : IFacturaXmlPreSubmissionValidator
    {
        public Task ValidateTipoDoc01Async(string xml, string cdc, Guid tenantId, SifenEnvironmentType environment, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class ReadyTenantCertificateValidator : ITenantCertificateValidator
    {
        public Task<CertificateValidationResult> ValidateAsync(TenantCertificateMetadata? metadata, CancellationToken cancellationToken = default)
            => Task.FromResult(new CertificateValidationResult(true, "OK", [new SecretCheckResult("certificate", SecretStatus.Present, "OK")]));
    }

    private sealed class FakeXmlDocumentSigner : IXmlDocumentSigner
    {
        public Task<SignedXmlDocumentResult> SignAsync(SignXmlDocumentCommand command, CancellationToken cancellationToken = default)
            => Task.FromResult(new SignedXmlDocumentResult(command.Xml, command.DocumentId, "c14n", "rsa-sha256", "sha256", "enveloped"));
    }

    private sealed class FakeSubmissionGateway : ISifenSubmissionGateway
    {
        public Task<SifenSubmissionResult> SendToSifenAsync(SendToSifenCommand command, CancellationToken cancellationToken = default)
            => Task.FromResult(new SifenSubmissionResult(true, "https://sifen-test.set.gov.py/de/ws/sync/recibe.wsdl", "123456789012345",
                $$"""
                <soapenv:Envelope xmlns:soapenv="http://schemas.xmlsoap.org/soap/envelope/" xmlns:sif="http://ekuatia.set.gov.py/sifen/xsd">
                  <soapenv:Body>
                    <sif:rRetEnviDe>
                      <sif:rProtDe>
                        <sif:Id>{{command.Cdc}}</sif:Id>
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

    private sealed class FakeResponseParser : ISifenResponseParser
    {
        public ParsedSifenResponse ParseResponse(string? rawResponse)
            => new(
                SifenResponseOutcome.Approved,
                SifenDocumentStatus.Accepted,
                true,
                true,
                "01800123456001001000012311123456789202604251",
                "123456789012345",
                "0300",
                "Factura electronica aprobada por SIFEN.",
                "Aprobado");
    }

    private sealed class StubOperationalReadinessReporter : IOperationalReadinessReporter
    {
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

        private static TenantFeOperationalDiagnostic Build(Guid tenantId, SifenEnvironmentType environment)
        {
            return new TenantFeOperationalDiagnostic(
                tenantId,
                environment.ToString(),
                "Diagnostic",
                true,
                false,
                "stub",
                [],
                [],
                [],
                null,
                null);
        }
    }

    private sealed class NullAuditTrail : IAuditTrail
    {
        public Task RecordAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed record TestFixture(
        Guid TenantId,
        ITenantContextAccessor TenantAccessor,
        SifenDbContext DbContext,
        IInvoiceService Service);
}
