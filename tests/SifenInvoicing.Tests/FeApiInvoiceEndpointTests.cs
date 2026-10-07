using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Http;
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
using SifenInvoicing.Domain.Common;
using SifenInvoicing.Domain.Documents;
using SifenInvoicing.Domain.Tenants;
using SifenInvoicing.Infrastructure.Diagnostics;
using SifenInvoicing.Infrastructure.Invoices;
using SifenInvoicing.Infrastructure.Persistence;
using SifenInvoicing.Infrastructure.Tenancy;

namespace SifenInvoicing.Tests;

public sealed class FeApiInvoiceEndpointTests
{
    [Fact]
    public async Task CreateSimpleInvoiceAsync_ShouldCreateInvoice()
    {
        var fixture = await CreateFixtureAsync();
        SetTenant(fixture.TenantAccessor, fixture.TenantId);

        var result = await InvoiceEndpoints.CreateSimpleInvoiceAsync(
            new InvoiceEndpoints.CreateSimpleInvoiceRequest
            {
                CurrencyCode = "PYG",
                SaleCondition = "Contado",
                ReceiverName = "Cliente Demo",
                ReceiverDocument = "80099999",
                ReceiverAddress = "Barrio Demo",
                ReceiverEmail = "cliente@test.py",
                ReceiverPhone = "0981000000",
                Notes = "Observacion TEST",
                Items =
                [
                    new InvoiceEndpoints.CreateInvoiceItemRequest
                    {
                        Description = "Servicio mensual",
                        Quantity = 1,
                        UnitPrice = 100000m,
                        VatRate = 10
                    }
                ]
            },
            Guid.NewGuid().ToString(),
            fixture.Service,
            CancellationToken.None);

        var stored = await fixture.DbContext.Documents.AsNoTracking().SingleAsync();
        var storedLine = await fixture.DbContext.DocumentLines.AsNoTracking().SingleAsync();
        var storedEvent = await fixture.DbContext.FeInvoiceEvents.AsNoTracking().SingleAsync();
        var storedTenantLog = await fixture.DbContext.FeTenantLogs.AsNoTracking().SingleAsync();

        Assert.NotNull(result);
        Assert.Equal(SifenDocumentStatus.InternalValidation, stored.Status);
        Assert.Equal("Cliente Demo", stored.ReceiverName);
        Assert.Equal("Factura electrónica", stored.DocumentType);
        Assert.Equal("Barrio Demo", stored.ReceiverAddress);
        Assert.Equal("cliente@test.py", stored.ReceiverEmail);
        Assert.Equal("0981000000", stored.ReceiverPhone);
        Assert.Equal("Observacion TEST", stored.Notes);
        Assert.Equal(100000m, stored.SubtotalAmount);
        Assert.Equal(9091m, stored.Vat10Amount); // PYG 0 decimales (politica provisoria [TEST])
        Assert.Equal(9091m, stored.TotalVatAmount);
        Assert.Equal(FeInvoiceInternalStatus.DRAFT, stored.InternalStatus);
        Assert.Equal(1, storedLine.LineNumber);
        Assert.Equal(10, storedLine.VatRate);
        Assert.Equal("InvoiceCreatedTest", storedEvent.EventType);
        Assert.Equal("fe.invoice.create", storedTenantLog.Source);
    }

    [Fact]
    public async Task CreateSimpleInvoiceAsync_ShouldFail_WhenCustomerIsMissing()
    {
        var fixture = await CreateFixtureAsync();
        SetTenant(fixture.TenantAccessor, fixture.TenantId);

        var exception = await Assert.ThrowsAsync<DomainException>(() =>
            InvoiceEndpoints.CreateSimpleInvoiceAsync(
                new InvoiceEndpoints.CreateSimpleInvoiceRequest
                {
                    CurrencyCode = "PYG",
                    ReceiverName = "",
                    ReceiverDocument = "",
                    Items =
                    [
                        new InvoiceEndpoints.CreateInvoiceItemRequest
                        {
                            Description = "Servicio mensual",
                            Quantity = 1,
                            UnitPrice = 100000m,
                            VatRate = 10
                        }
                    ]
                },
                Guid.NewGuid().ToString(),
                fixture.Service,
                CancellationToken.None));

        Assert.Contains("Customer is required.", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateSimpleInvoiceAsync_ShouldCalculateTotalsInBackend()
    {
        var fixture = await CreateFixtureAsync();
        SetTenant(fixture.TenantAccessor, fixture.TenantId);

        await InvoiceEndpoints.CreateSimpleInvoiceAsync(
            new InvoiceEndpoints.CreateSimpleInvoiceRequest
            {
                CurrencyCode = "PYG",
                ReceiverName = "Cliente IVA",
                ReceiverDocument = "80099999",
                Items =
                [
                    new InvoiceEndpoints.CreateInvoiceItemRequest
                    {
                        Description = "Gravado 10",
                        Quantity = 1,
                        UnitPrice = 110000m,
                        VatRate = 10
                    },
                    new InvoiceEndpoints.CreateInvoiceItemRequest
                    {
                        Description = "Gravado 5",
                        Quantity = 1,
                        UnitPrice = 105000m,
                        VatRate = 5
                    },
                    new InvoiceEndpoints.CreateInvoiceItemRequest
                    {
                        Description = "Exento",
                        Quantity = 1,
                        UnitPrice = 50000m,
                        VatRate = 0
                    }
                ]
            },
            Guid.NewGuid().ToString(),
            fixture.Service,
            CancellationToken.None);

        var stored = await fixture.DbContext.Documents.AsNoTracking().SingleAsync();
        var lines = await fixture.DbContext.DocumentLines.AsNoTracking().OrderBy(x => x.LineNumber).ToListAsync();

        Assert.Equal(265000m, stored.SubtotalAmount);
        Assert.Equal(10000m, stored.Vat10Amount);
        Assert.Equal(5000m, stored.Vat5Amount);
        Assert.Equal(50000m, stored.ExemptAmount);
        Assert.Equal(15000m, stored.TotalVatAmount);
        Assert.Equal(265000m, stored.TotalAmount);
        Assert.Equal(new[] { 10, 5, 0 }, lines.Select(x => x.VatRate).ToArray());
    }

    [Fact]
    public async Task CreateSimpleInvoiceAsync_ShouldFail_WhenVatRateIsInvalid()
    {
        var fixture = await CreateFixtureAsync();
        SetTenant(fixture.TenantAccessor, fixture.TenantId);

        var exception = await Assert.ThrowsAsync<DomainException>(() =>
            InvoiceEndpoints.CreateSimpleInvoiceAsync(
                new InvoiceEndpoints.CreateSimpleInvoiceRequest
                {
                    CurrencyCode = "PYG",
                    ReceiverName = "Cliente Demo",
                    ReceiverDocument = "80099999",
                    Items =
                    [
                        new InvoiceEndpoints.CreateInvoiceItemRequest
                        {
                            Description = "Servicio mensual",
                            Quantity = 1,
                            UnitPrice = 100000m,
                            VatRate = 7
                        }
                    ]
                },
                Guid.NewGuid().ToString(),
                fixture.Service,
                CancellationToken.None));

        Assert.Contains("vatRate", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

[Theory]
    [InlineData(SifenDocumentStatus.DraftGenerated, "pendiente")]
    [InlineData(SifenDocumentStatus.PendingSubmission, "pendiente")]
    [InlineData(SifenDocumentStatus.Submitted, "pendiente")]
    [InlineData(SifenDocumentStatus.InternalValidation, "validacion-interna")]
    [InlineData(SifenDocumentStatus.InternalValidationFailed, "validacion-interna-fallida")]
    [InlineData(SifenDocumentStatus.DraftValidatedWithoutSignature, "borrador-validado-sin-firma")]
    [InlineData(SifenDocumentStatus.Accepted, "aprobado")]
    [InlineData(SifenDocumentStatus.Rejected, "rechazado")]
    [InlineData(SifenDocumentStatus.Failed, "error")]
    public async Task GetSimpleInvoiceStatusAsync_ShouldMapClearStatus(
        SifenDocumentStatus internalStatus,
        string expectedStatus)
    {
        var service = new StubInvoiceService(BuildInvoiceDetail(internalStatus));

        var result = await InvoiceEndpoints.GetSimpleInvoiceStatusAsync(
            "01800123456001001000012311123456789202604251",
            service,
            CancellationToken.None);

        var ok = Assert.IsAssignableFrom<IValueHttpResult>(result);
        var status = ReadAnonymousProperty<string>(ok.Value!, "status");

        Assert.Equal(expectedStatus, status);
    }

    [Fact]
    public async Task DownloadSimpleInvoiceXmlAsync_ShouldReturnXmlFile()
    {
        var service = new StubInvoiceService(BuildInvoiceDetail(SifenDocumentStatus.Accepted));

        var result = await InvoiceEndpoints.DownloadSimpleInvoiceXmlAsync(
            Guid.NewGuid(),
            service,
            CancellationToken.None);

        var file = Assert.IsType<FileContentHttpResult>(result);
        Assert.Equal("application/xml", file.ContentType);
        Assert.True(file.FileContents.Length > 0);
    }

    [Fact]
    public async Task DownloadSimpleInvoiceKudeAsync_ShouldReturnPdfFile_WhenAvailable()
    {
        var service = new StubInvoiceService(
            BuildInvoiceDetail(SifenDocumentStatus.Accepted),
            new InvoiceKudePdfResult("kude.pdf", [1, 2, 3], "01800123456001001000012311123456789202604251", true));

        var result = await InvoiceEndpoints.DownloadSimpleInvoiceKudeAsync(
            Guid.NewGuid(),
            service,
            CancellationToken.None);

        var file = Assert.IsType<FileContentHttpResult>(result);
        Assert.Equal("application/pdf", file.ContentType);
        Assert.True(file.FileContents.Length > 0);
    }

    [Fact]
    public async Task DownloadSimpleInvoiceKudeAsync_ShouldReturnPlaceholder_WhenKudeIsMissing()
    {
        var service = new StubInvoiceService(BuildInvoiceDetail(SifenDocumentStatus.Accepted), null);

        var result = await InvoiceEndpoints.DownloadSimpleInvoiceKudeAsync(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            service,
            CancellationToken.None);

        var ok = Assert.IsAssignableFrom<IValueHttpResult>(result);
        var available = ReadAnonymousProperty<bool>(ok.Value!, "Available");
        var futurePath = ReadAnonymousProperty<string>(ok.Value!, "FuturePath");

        Assert.False(available);
        Assert.Equal("/api/fe/invoices/11111111-1111-1111-1111-111111111111/kude", futurePath);
    }

    private static async Task<TestFixture> CreateFixtureAsync()
    {
        var tenant = Tenant.CreateSharedDatabaseTenant("api-fe", "API FE");
        var tenantAccessor = new AsyncLocalTenantContextAccessor();
        tenantAccessor.SetCurrent(new TenantContext
        {
            TenantId = tenant.Id.ToString(),
            ResolvedTenantId = tenant.Id,
            IsResolved = true
        });

        var options = new DbContextOptionsBuilder<SifenDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var dbContext = new SifenDbContext(options, new SystemClock(), tenantAccessor);
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
        await dbContext.SaveChangesAsync();

        var service = new EfInvoiceService(
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
            new SystemClock(),
            new SifenInvoicing.Infrastructure.Numbering.EfNumberingService(dbContext),
            new TestFiscalClock());

        return new TestFixture(tenant.Id, tenantAccessor, dbContext, service);
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
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection([])
            .Build();
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
            => Task.FromResult(new SifenSubmissionResult(
                true,
                "https://sifen-test.set.gov.py/de/ws/sync/recibe.wsdl",
                "123456789012345",
                """
                <soapenv:Envelope xmlns:soapenv="http://schemas.xmlsoap.org/soap/envelope/" xmlns:sif="http://ekuatia.set.gov.py/sifen/xsd">
                  <soapenv:Body>
                    <sif:rRetEnviDe>
                      <sif:rProtDe>
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
                null,
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

    private static InvoiceDetail BuildInvoiceDetail(SifenDocumentStatus status)
    {
        return new InvoiceDetail(
            Guid.NewGuid(),
            Guid.NewGuid(),
            SifenEnvironmentType.Test,
            status,
            "01800123456001001000012311123456789202604251",
            "TEST-01800123456001001000012311123456789202604251",
            "QR TEST",
            false,
            "Factura electrónica",
            "0000001",
            "001",
            "001",
            "PYG",
            "Contado",
            "Observacion TEST",
            "Cliente Demo",
            "80099999",
            "Barrio Demo",
            "cliente@test.py",
            "0981000000",
            100000m,
            0m,
            9090.91m,
            0m,
            9090.91m,
            100000m,
            "<xml />",
            "<signed />",
            "https://sifen-test.set.gov.py/de/ws/sync/recibe.wsdl",
            "123456789012345",
            status == SifenDocumentStatus.Accepted ? "0300" : status == SifenDocumentStatus.Rejected ? "0500" : status == SifenDocumentStatus.Failed ? "SOAP_TIMEOUT" : null,
            status == SifenDocumentStatus.Accepted ? "Factura electronica aprobada por SIFEN." : status == SifenDocumentStatus.Rejected ? "Factura electronica rechazada por SIFEN." : status == SifenDocumentStatus.Failed ? "SIFEN no respondio dentro del tiempo esperado." : "Pendiente.",
            status == SifenDocumentStatus.Failed ? "SOAP_TIMEOUT" : status == SifenDocumentStatus.Rejected ? "SIFEN_REJECTED" : null,
            status == SifenDocumentStatus.Failed ? "SifenTransport" : status == SifenDocumentStatus.Rejected ? "SifenRejected" : null,
            status == SifenDocumentStatus.Failed ? "No pudimos completar el envio a SIFEN." : status == SifenDocumentStatus.Rejected ? "SIFEN rechazo esta factura." : null,
            status == SifenDocumentStatus.Failed ? "Puedes reintentar esta factura cuando el servicio vuelva a estar disponible." : status == SifenDocumentStatus.Rejected ? "Revisa los datos de la factura y vuelve a emitirla cuando la correccion este lista." : null,
            status == SifenDocumentStatus.Failed,
            status == SifenDocumentStatus.Failed ? "corr-123" : null,
            "VALIDATED_TEST",
            0,
            status == SifenDocumentStatus.Failed ? "SOAP_TIMEOUT" : null,
            status == SifenDocumentStatus.Failed ? "SIFEN no respondio dentro del tiempo esperado." : null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            status is SifenDocumentStatus.Accepted or SifenDocumentStatus.Rejected or SifenDocumentStatus.Failed ? DateTimeOffset.UtcNow : null,
            [new InvoiceItemSummary(1, "Servicio mensual", 1m, 100000m, 10, 9090.91m, 0m, 100000m, 100000m)],
            [],
            [],
            []);
    }

    private static T ReadAnonymousProperty<T>(object instance, string propertyName)
    {
        var property = instance.GetType().GetProperty(propertyName)
            ?? throw new InvalidOperationException($"Property '{propertyName}' was not found.");
        return (T)(property.GetValue(instance)
            ?? throw new InvalidOperationException($"Property '{propertyName}' is null."));
    }

    private sealed class StubInvoiceService : IInvoiceService
    {
        private readonly InvoiceDetail _invoiceDetail;
        private readonly InvoiceKudePdfResult? _kude;

        public StubInvoiceService(InvoiceDetail invoiceDetail, InvoiceKudePdfResult? kude = null)
        {
            _invoiceDetail = invoiceDetail;
            _kude = kude;
        }

        public Task<CreateInvoiceResult> CreateAsync(CreateInvoiceCommand command, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<InvoiceDetail?> GetByIdAsync(Guid invoiceId, CancellationToken cancellationToken = default)
            => Task.FromResult<InvoiceDetail?>(_invoiceDetail);

        public Task<InvoiceDetail?> GetByCdcAsync(string cdc, CancellationToken cancellationToken = default)
            => Task.FromResult<InvoiceDetail?>(_invoiceDetail);

        public Task<InvoiceKudePdfResult?> GenerateKudePdfAsync(Guid invoiceId, CancellationToken cancellationToken = default)
            => Task.FromResult(_kude);

        public Task<InvoiceRetryResult> RetryAsync(Guid invoiceId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyCollection<InvoiceListItem>> SearchAsync(InvoiceSearchQuery query, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed record TestFixture(
        Guid TenantId,
        ITenantContextAccessor TenantAccessor,
        SifenDbContext DbContext,
        IInvoiceService Service);
}
