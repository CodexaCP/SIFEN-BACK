using System.Xml.Linq;
using SifenInvoicing.Application.Cdc;
using SifenInvoicing.Application.Invoices;
using SifenInvoicing.Domain.Common;

namespace SifenInvoicing.Tests;

public sealed class FacturaXmlGeneratorTests
{
    [Fact]
    public void GenerateFacturaXML_ShouldBuildMinimalFacturaWithConsistentTotals()
    {
        var generator = new FacturaXmlGenerator();
        var input = new GenerateFacturaXmlInput(
            new GenerateCdcInput("01", "80012345", "6", "1", "1", "123", "1", "1", "123456789", "20260425"),
            new DateTimeOffset(2026, 4, 25, 10, 30, 0, TimeSpan.FromHours(-4)),
            1,
            "ACME Paraguay SA",
            "Asuncion 123",
            "Cliente Demo",
            InvoiceReceiverDocumentType.Ruc,
            "80099999",
            InvoiceCurrency.PYG,
            InvoiceSaleCondition.Cash,
            [
                new GenerateFacturaXmlItemInput("Servicio mensual", 2, 100000m, InvoiceVatType.Vat10),
                new GenerateFacturaXmlItemInput("Producto exento", 1, 50000m, InvoiceVatType.Exempt)
            ]);

        var result = generator.GenerateFacturaXML(input);
        var xml = XDocument.Parse(result.Xml);
        XNamespace ns = "http://ekuatia.set.gov.py/sifen/xsd";

        Assert.Equal(result.Cdc, xml.Root!.Element(ns + "DE")!.Attribute("Id")!.Value);
        Assert.Equal("01", xml.Root.Element(ns + "DE")!.Element(ns + "gDtipDE")!.Element(ns + "dCodTipoDoc")!.Value);
        Assert.Equal("Factura Electrónica", xml.Root.Element(ns + "DE")!.Element(ns + "gDtipDE")!.Element(ns + "dDesTipDoc")!.Value);
        Assert.Equal("PYG", xml.Root.Element(ns + "DE")!.Element(ns + "gCamFE")!.Element(ns + "dCodMon")!.Value);
        Assert.Equal("1", xml.Root.Element(ns + "DE")!.Element(ns + "gCamFE")!.Element(ns + "iCondOpe")!.Value);
        Assert.Equal(250000m, result.TotalGeneral);
        Assert.Equal(200000m, result.TotalGravado10);
        Assert.Equal(0m, result.TotalGravado5);
        Assert.Equal(50000m, result.TotalExento);
        Assert.Equal(18181.82m, result.TotalIva);
    }

    [Fact]
    public void GenerateFacturaXML_ShouldSupportCiReceiverAndUsdCreditSale()
    {
        var generator = new FacturaXmlGenerator();
        var input = new GenerateFacturaXmlInput(
            new GenerateCdcInput("01", "80012345", "6", "1", "1", "124", "1", "1", "123456789", "20260425"),
            new DateTimeOffset(2026, 4, 25, 11, 0, 0, TimeSpan.FromHours(-4)),
            2,
            "ACME Paraguay SA",
            "Asuncion 123",
            "Consumidor Final",
            InvoiceReceiverDocumentType.Ci,
            "1234567",
            InvoiceCurrency.USD,
            InvoiceSaleCondition.Credit,
            [
                new GenerateFacturaXmlItemInput("Servicio USD", 1, 100m, InvoiceVatType.Vat5)
            ]);

        var result = generator.GenerateFacturaXML(input);
        var xml = XDocument.Parse(result.Xml);
        XNamespace ns = "http://ekuatia.set.gov.py/sifen/xsd";

        Assert.Equal("USD", xml.Root!.Element(ns + "DE")!.Element(ns + "gCamFE")!.Element(ns + "dCodMon")!.Value);
        Assert.Equal("2", xml.Root.Element(ns + "DE")!.Element(ns + "gCamFE")!.Element(ns + "iCondOpe")!.Value);
        Assert.Equal("1234567", xml.Root.Element(ns + "DE")!.Element(ns + "gDatGralOpe")!.Element(ns + "gReceptor")!.Element(ns + "dNumIDRec")!.Value);
        Assert.Equal(100m, result.TotalGravado5);
        Assert.Equal(4.76m, result.TotalIva);
    }

    [Fact]
    public void GenerateFacturaXML_ShouldThrow_WhenItemsAreMissing()
    {
        var generator = new FacturaXmlGenerator();
        var input = new GenerateFacturaXmlInput(
            new GenerateCdcInput("01", "80012345", "6", "1", "1", "125", "1", "1", "123456789", "20260425"),
            DateTimeOffset.Now,
            1,
            "ACME Paraguay SA",
            "Asuncion 123",
            "Cliente Demo",
            InvoiceReceiverDocumentType.Ruc,
            "80099999",
            InvoiceCurrency.PYG,
            InvoiceSaleCondition.Cash,
            []);

        Assert.Throws<DomainException>(() => generator.GenerateFacturaXML(input));
    }

    [Fact]
    public void GenerateFacturaXML_ShouldThrow_WhenReceiverIsMissing()
    {
        var generator = new FacturaXmlGenerator();
        var input = new GenerateFacturaXmlInput(
            new GenerateCdcInput("01", "80012345", "6", "1", "1", "126", "1", "1", "123456789", "20260425"),
            DateTimeOffset.Now,
            1,
            "ACME Paraguay SA",
            "Asuncion 123",
            "",
            InvoiceReceiverDocumentType.Ruc,
            "80099999",
            InvoiceCurrency.PYG,
            InvoiceSaleCondition.Cash,
            [
                new GenerateFacturaXmlItemInput("Servicio mensual", 1, 100000m, InvoiceVatType.Vat10)
            ]);

        var exception = Assert.Throws<DomainException>(() => generator.GenerateFacturaXML(input));

        Assert.Equal("ReceptorNombre is required.", exception!.Message);
    }
}
