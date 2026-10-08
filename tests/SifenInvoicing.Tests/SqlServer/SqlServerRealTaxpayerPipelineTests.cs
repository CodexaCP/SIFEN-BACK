using System.Net;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using SifenInvoicing.Application.Cdc;
using SifenInvoicing.Application.Diagnostics;
using SifenInvoicing.Application.Invoices;
using SifenInvoicing.Application.Sifen;
using SifenInvoicing.Domain.Documents;
using SifenInvoicing.Domain.Tenants;
using SifenInvoicing.Infrastructure.Diagnostics;
using SifenInvoicing.Infrastructure.Invoices;
using SifenInvoicing.Infrastructure.Numbering;
using SifenInvoicing.Infrastructure.Sifen;
using SifenInvoicing.Infrastructure.Tenancy;
using SifenInvoicing.Tests.XmlDe;

namespace SifenInvoicing.Tests.SqlServer;

/// <summary>
/// Pipeline interno completo con los datos fiscales de TEST del contribuyente (RUC 80155881-6, timbrado 18079382,
/// inicio 2025-06-04, 001-001, DE 01, PYG, IVA 10 %, 2 items) sobre SQL Server REAL, en modo Diagnostic: se detiene
/// ANTES del gateway SIFEN (EfInvoiceService termina en INTERNAL_VALIDATION). El gateway de esta prueba lanza si se invoca,
/// asi que SIFEN REAL NO SE EJECUTA por construccion.
///
/// Datos que son de la prueba y NO del contribuyente real (reemplazar si se quiere validar contra el RUC real):
/// direccion/telefono/email del emisor, receptor, codigos de producto/unidad y precios, y el certificado de firma
/// (efimero, en memoria). El CSC es el IdCSC 0001 generico de la Guia de Pruebas. Las actividades 56103 y 56109 se
/// toman de los datos entregados (el prefijo "C4_" no se incluye en el codigo: PENDIENTE DE CONFIRMAR formato cActEco).
/// Ejecutar: dotnet test --no-restore --filter Category=SqlServerIntegration
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", SqlServerTestEnvironment.TraitCategory)]
public sealed class SqlServerRealTaxpayerPipelineTests(SqlServerFixture fixture)
{
    private const string Ruc = "80155881";
    private const string Dv = "6";
    private const string Stamp = "18079382";

    private sealed class ThrowingGateway : ISifenSubmissionGateway
    {
        public int Calls { get; private set; }

        public Task<SifenSubmissionResult> SendToSifenAsync(SendToSifenCommand command, CancellationToken cancellationToken = default)
        {
            Calls++;
            throw new InvalidOperationException("El gateway SIFEN NO debe invocarse en esta prueba.");
        }
    }

    private async Task<Guid> SeedRealTaxpayerTenantAsync()
    {
        var tenantId = await fixture.SeedTenantAsync("realdata", numbering: false, taxpayer: false);
        await using var db = fixture.NewContext(tenantId);

        var taxpayer = TestFiscalSetup.Taxpayer(tenantId, Ruc, Dv, "THE BEST OF BRASIL E.A.S.");
        db.TaxpayerProfiles.Add(taxpayer);
        db.TaxpayerEconomicActivities.Add(TaxpayerEconomicActivity.Create(
            tenantId, taxpayer.Id, "56103", "HELADERÍAS QUE NO ELABORAN EL PRODUCTO", 0));
        db.TaxpayerEconomicActivities.Add(TaxpayerEconomicActivity.Create(
            tenantId, taxpayer.Id, "56109", "OTROS SERVICIOS DE SUMINISTRO DE ALIMENTO PARA CONSUMO INMEDIATO N.C.P.", 1));

        var stamp = FiscalStamp.Create(tenantId, SifenEnvironmentType.Test, Stamp, new DateOnly(2025, 6, 4));
        db.FiscalStamps.Add(stamp);
        db.NumberingSequences.Add(NumberingSequence.Create(tenantId, SifenEnvironmentType.Test, stamp.Id, "01", "001", "001", null, 1));
        await db.SaveChangesAsync();
        return tenantId;
    }

    [SqlServerFact]
    public async Task RealTaxpayerData_ShouldRunWholeInternalPipeline_AndStopBeforeSifen()
    {
        var tenantId = await SeedRealTaxpayerTenantAsync();
        var gateway = new ThrowingGateway();
        await using var db = fixture.NewContext(tenantId);
        var service = new EfInvoiceService(
            db, new AsyncLocalTenantContextAccessor(), DeTestKit.Builder(), DeTestKit.Xsd(), new InvoiceKudePdfRenderer(),
            new ReadyCertificateValidator(), SigningTestKit.SharedSigner(db), new StubReadiness(), gateway,
            new DefaultSifenResponseParser(), SqlServerFakes.Configuration(), new NullAudit(), new SystemClock(),
            new EfNumberingService(db), new TestFiscalClock(), SigningTestKit.SharedQrAttacher(db));

        var result = await service.CreateAsync(new CreateInvoiceCommand(
            "real-data-001", null, "Cliente Demo", InvoiceReceiverDocumentType.Ci, "1234567", null, null, null,
            InvoiceCurrency.PYG, InvoiceSaleCondition.Cash,
            [
                new CreateInvoiceItemCommand("Item de prueba 1", 1, 100000m, 10, "SRV-001", 77),
                new CreateInvoiceItemCommand("Item de prueba 2", 2, 50000m, 10, "SRV-002", 77)
            ]));

        // 1) No se llamo a SIFEN y el estado es solo interno.
        Assert.Equal(0, gateway.Calls);
        Assert.Equal(SifenDocumentStatus.InternalValidation, result.Status);
        Assert.Equal(200000m, result.TotalAmount);

        // 2) Estado persistido, leido con un contexto nuevo.
        await using var verify = fixture.NewContext(tenantId);
        var document = await verify.Documents.SingleAsync();
        Assert.Equal("001", document.EstablishmentCode);
        Assert.Equal("001", document.ExpeditionPointCode);
        Assert.Equal("0000001", document.ExternalDocumentNumber);
        Assert.Equal(Stamp, document.StampingNumber);
        Assert.Equal(SifenTransmissionState.NotSent, document.TransmissionState);
        Assert.Equal(SifenFiscalState.None, document.FiscalState);
        Assert.Equal(2, await verify.DocumentLines.CountAsync());
        Assert.Equal(2L, (await verify.NumberingSequences.SingleAsync()).NextNumber);

        // 3) CDC: 01 + RUC + DV + 001 + 001 + 0000001, con DV modulo 11 valido.
        var cdc = document.Cdc;
        Assert.Equal(44, cdc.Length);
        Assert.Equal("01" + Ruc + Dv + "001" + "001" + "0000001", cdc[..24]);
        Assert.Equal(CdcGenerator.CalculateModulo11(cdc[..43]).ToString(), cdc[43].ToString());

        // 4) XML firmado (XMLDSig) + QR + gCamFuFD, y XSD final valido.
        var xml = Assert.IsType<string>(document.SignedXmlPayload);
        await DeTestKit.Xsd().EnsureFinalValidAsync(xml);
        var root = XDocument.Parse(xml).Root!;
        XNamespace ds = "http://www.w3.org/2000/09/xmldsig#";
        XNamespace sifen = "http://ekuatia.set.gov.py/sifen/xsd";
        var digest = root.Descendants(ds + "DigestValue").Single().Value;
        Assert.False(string.IsNullOrWhiteSpace(digest));
        var qr = WebUtility.HtmlDecode(root.Descendants(sifen + "dCarQR").Single().Value);
        Assert.Contains("Id=" + cdc, qr, StringComparison.Ordinal);
        Assert.Contains("IdCSC=0001", qr, StringComparison.Ordinal);
        Assert.Contains("cHashQR=", qr, StringComparison.Ordinal);
        Assert.DoesNotContain(SigningTestKit.GuideGenericCsc, qr, StringComparison.Ordinal); // el CSC nunca va en el QR
        Assert.DoesNotContain(SigningTestKit.GuideGenericCsc, xml, StringComparison.Ordinal);

        // 5) Evidencia de que no hubo transporte: ningun log de solicitud/respuesta SIFEN.
        var events = await verify.DocumentLogs.Select(log => log.EventType).ToListAsync();
        Assert.DoesNotContain("sifen.request", events);
        Assert.DoesNotContain("sifen.submission", events);
    }
}
