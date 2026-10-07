using System.Xml.Linq;
using SifenInvoicing.Application.Cdc;
using SifenInvoicing.Application.Invoices;
using SifenInvoicing.Domain.Common;

namespace SifenInvoicing.Tests.XmlDe;

/// <summary>
/// Fase 4.0 - CARACTERIZACION del FacturaXmlGenerator actual. Documenta que NO es conforme al DE v150.
/// Estas pruebas deben retirarse/invertirse cuando se implemente Fase 4.2; no son codigo de produccion.
/// </summary>
public sealed class CurrentGeneratorGapTests
{
    private static XDocument Generate()
    {
        var input = new GenerateFacturaXmlInput(
            new GenerateCdcInput("01", "80012345", "6", "1", "1", "123", "1", "1", "123456789", "20260425"),
            new DateTimeOffset(2026, 4, 25, 10, 30, 0, TimeSpan.FromHours(-4)),
            1, "ACME Paraguay SA", "Asuncion 123", "Cliente Demo",
            InvoiceReceiverDocumentType.Ruc, "80099999", InvoiceCurrency.PYG, InvoiceSaleCondition.Cash,
            TestFiscal.Model(("Servicio", 2m, 100000m, InvoiceVatType.Vat10)),
            TestFiscal.Descriptions(("Servicio", 2m, 100000m, InvoiceVatType.Vat10)));
        return XDocument.Parse(new FacturaXmlGenerator().GenerateFacturaXML(input).Xml);
    }

    [Fact]
    public void CurrentGenerator_IsNotConformantToV150Reference_Gap()
    {
        var errors = DeConformance.Check(Generate());
        Assert.NotEmpty(errors);
        Assert.Contains(errors, e => e.Contains("dSisFact"));   // retirado por NT-10
    }

    [Fact]
    public void CurrentGenerator_EmitsNamesAbsentFromManualV150_Gap()
    {
        var names = Generate().Descendants().Select(e => e.Name.LocalName).ToHashSet();
        Assert.Contains("dCodTipoDoc", names);
        Assert.DoesNotContain("dBasExe", names);               // NT-13: obligatorio por item
        Assert.DoesNotContain("gTimb", names);
    }

    [Fact]
    public void CurrentGenerator_NoSignatureNoGCamFuFD_AsExpectedBeforeSigning()
    {
        var root = Generate().Root!;
        Assert.DoesNotContain(root.Elements(), e => e.Name.LocalName is "Signature" or "gCamFuFD");
    }
}
