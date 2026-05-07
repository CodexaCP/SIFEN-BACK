using System.Globalization;
using System.Xml.Linq;
using SifenInvoicing.Application.Cdc;
using SifenInvoicing.Domain.Common;

namespace SifenInvoicing.Application.Invoices;

public sealed class FacturaXmlGenerator : IFacturaXmlGenerator
{
    private static readonly XNamespace Ns = "http://ekuatia.set.gov.py/sifen/xsd";
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public GeneratedFacturaXmlResult GenerateFacturaXML(GenerateFacturaXmlInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (input.Items is null || input.Items.Count == 0)
        {
            throw new DomainException("At least one invoice item is required.");
        }

        if (input.SistemaFacturacion < 1)
        {
            throw new DomainException("SistemaFacturacion must be greater than zero.");
        }

        ValidateRequired(input.EmisorNombre, nameof(input.EmisorNombre));
        ValidateRequired(input.EmisorDireccion, nameof(input.EmisorDireccion));
        ValidateRequired(input.ReceptorNombre, nameof(input.ReceptorNombre));
        ValidateRequired(input.ReceptorDocumento, nameof(input.ReceptorDocumento));
        ValidateRequired(input.Cdc.Establecimiento, nameof(input.Cdc.Establecimiento));
        ValidateRequired(input.Cdc.PuntoExpedicion, nameof(input.Cdc.PuntoExpedicion));
        ValidateRequired(input.Cdc.NumeroDe, nameof(input.Cdc.NumeroDe));
        ValidateRequired(input.Cdc.CodigoSeguridad, nameof(input.Cdc.CodigoSeguridad));

        if (input.FechaFirma == default)
        {
            throw new DomainException("FechaFirma is required.");
        }

        if (!Enum.IsDefined(input.ReceptorTipoDocumento))
        {
            throw new DomainException("Unsupported receiver document type.");
        }

        if (!Enum.IsDefined(input.Currency))
        {
            throw new DomainException("Unsupported currency.");
        }

        if (!Enum.IsDefined(input.SaleCondition))
        {
            throw new DomainException("Unsupported sale condition.");
        }

        var cdc = CdcGenerator.GenerateCDC(input.Cdc);
        var items = input.Items.Select((item, index) => BuildItem(item, index + 1)).ToList();

        var totalGravado10 = items.Where(item => item.VatType == InvoiceVatType.Vat10).Sum(item => item.Total);
        var totalGravado5 = items.Where(item => item.VatType == InvoiceVatType.Vat5).Sum(item => item.Total);
        var totalExento = items.Where(item => item.VatType == InvoiceVatType.Exempt).Sum(item => item.Total);
        var totalIva10 = Round(totalGravado10 / 11m);
        var totalIva5 = Round(totalGravado5 / 21m);
        var totalIva = totalIva10 + totalIva5;
        var totalGeneral = totalGravado10 + totalGravado5 + totalExento;

        var deElement = new XElement(Ns + "DE",
            new XAttribute("Id", cdc),
            new XElement(Ns + "dDVId", cdc[^1].ToString()),
            new XElement(Ns + "dFecFirma", FormatTimestamp(input.FechaFirma)),
            new XElement(Ns + "dSisFact", input.SistemaFacturacion.ToString(Invariant)),
            new XElement(Ns + "gDatGralOpe",
                new XElement(Ns + "gEmis",
                    new XElement(Ns + "dRucEm", input.Cdc.Ruc),
                    new XElement(Ns + "dDVEmi", input.Cdc.DvRuc),
                    new XElement(Ns + "xNomEmi", input.EmisorNombre.Trim()),
                    new XElement(Ns + "xDirEmi", input.EmisorDireccion.Trim())),
                new XElement(Ns + "gReceptor",
                    new XElement(Ns + "iTiRec", GetReceiverTypeCode(input.ReceptorTipoDocumento)),
                    BuildReceiverDocument(input.ReceptorTipoDocumento, input.ReceptorDocumento.Trim()),
                    new XElement(Ns + "xNomRec", input.ReceptorNombre.Trim()))),
            new XElement(Ns + "gDtipDE",
                new XElement(Ns + "dCodTipoDoc", "01"),
                new XElement(Ns + "dDesTipDoc", "Factura Electrónica")),
            new XElement(Ns + "gCamFE",
                new XElement(Ns + "iTipOpe", "1"),
                new XElement(Ns + "dDesTipOpe", "B2B"),
                new XElement(Ns + "dCodMon", input.Currency.ToString()),
                new XElement(Ns + "dDesMon", input.Currency.ToString()),
                new XElement(Ns + "iCondOpe", GetSaleConditionCode(input.SaleCondition)),
                new XElement(Ns + "dDCondOpe", GetSaleConditionDescription(input.SaleCondition))),
            new XElement(Ns + "gCamItem",
                items.Select(item =>
                    new XElement(Ns + "gCamItemDet",
                        new XElement(Ns + "dCodInt", item.Code),
                        new XElement(Ns + "xDesProSer", item.Description),
                        new XElement(Ns + "dCantProSer", FormatDecimal(item.Quantity)),
                        new XElement(Ns + "dPUniProSer", FormatDecimal(item.UnitPrice)),
                        new XElement(Ns + "dTotBruOpeItem", FormatDecimal(item.Total)),
                        new XElement(Ns + "iAfecIVA", GetVatAffectationCode(item.VatType)),
                        new XElement(Ns + "dTasaIVA", FormatDecimal(GetVatRate(item.VatType))),
                        new XElement(Ns + "dValTotItem", FormatDecimal(item.Total))))),
            new XElement(Ns + "gTotSub",
                new XElement(Ns + "dSubExe", FormatDecimal(totalExento)),
                new XElement(Ns + "dSub5", FormatDecimal(totalGravado5)),
                new XElement(Ns + "dSub10", FormatDecimal(totalGravado10)),
                new XElement(Ns + "dTotOpe", FormatDecimal(totalGeneral)),
                new XElement(Ns + "dTotDesc", "0"),
                new XElement(Ns + "dTotDescGlotem", "0"),
                new XElement(Ns + "dTotAntItem", "0"),
                new XElement(Ns + "dTotAnt", "0"),
                new XElement(Ns + "dPorcDescTotal", "0"),
                new XElement(Ns + "dDescTotal", "0"),
                new XElement(Ns + "dAnticipo", "0"),
                new XElement(Ns + "dRedon", "0"),
                new XElement(Ns + "dComi", "0"),
                new XElement(Ns + "dTotGralOpe", FormatDecimal(totalGeneral)),
                new XElement(Ns + "dIVA5", FormatDecimal(totalIva5)),
                new XElement(Ns + "dIVA10", FormatDecimal(totalIva10)),
                new XElement(Ns + "dTotIVA", FormatDecimal(totalIva)),
                new XElement(Ns + "dBaseGrav5", FormatDecimal(totalGravado5)),
                new XElement(Ns + "dBaseGrav10", FormatDecimal(totalGravado10))));

        var document = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement(Ns + "rDE",
                new XAttribute(XNamespace.Xmlns + "ds", "http://www.w3.org/2000/09/xmldsig#"),
                new XElement(Ns + "dVerFor", "150"),
                deElement));
        var result = new GeneratedFacturaXmlResult(
            cdc,
            document.ToString(SaveOptions.DisableFormatting),
            totalGravado10,
            totalGravado5,
            totalExento,
            totalIva,
            totalGeneral);

        ValidateGeneratedResult(result);
        return result;
    }

    private static InvoiceItem BuildItem(GenerateFacturaXmlItemInput item, int index)
    {
        ArgumentNullException.ThrowIfNull(item);
        ValidateRequired(item.Description, nameof(item.Description));

        if (item.Quantity <= 0)
        {
            throw new DomainException("Item quantity must be greater than zero.");
        }

        if (item.UnitPrice < 0)
        {
            throw new DomainException("Item unit price cannot be negative.");
        }

        if (!Enum.IsDefined(item.VatType))
        {
            throw new DomainException("Unsupported VAT type.");
        }

        return new InvoiceItem(
            $"ITEM-{index:000}",
            item.Description.Trim(),
            item.Quantity,
            item.UnitPrice,
            Round(item.Quantity * item.UnitPrice),
            item.VatType);
    }

    private static object BuildReceiverDocument(InvoiceReceiverDocumentType type, string document)
    {
        ValidateRequired(document, nameof(document));

        return type switch
        {
            InvoiceReceiverDocumentType.Ruc => new XElement(Ns + "dRucRec", document),
            InvoiceReceiverDocumentType.Ci => new XElement(Ns + "dNumIDRec", document),
            _ => throw new DomainException("Unsupported receiver document type.")
        };
    }

    private static string GetReceiverTypeCode(InvoiceReceiverDocumentType type)
    {
        return type switch
        {
            InvoiceReceiverDocumentType.Ruc => "1",
            InvoiceReceiverDocumentType.Ci => "2",
            _ => throw new DomainException("Unsupported receiver document type.")
        };
    }

    private static string GetSaleConditionCode(InvoiceSaleCondition condition)
    {
        return condition switch
        {
            InvoiceSaleCondition.Cash => "1",
            InvoiceSaleCondition.Credit => "2",
            _ => throw new DomainException("Unsupported sale condition.")
        };
    }

    private static string GetSaleConditionDescription(InvoiceSaleCondition condition)
    {
        return condition switch
        {
            InvoiceSaleCondition.Cash => "Contado",
            InvoiceSaleCondition.Credit => "Crédito",
            _ => throw new DomainException("Unsupported sale condition.")
        };
    }

    private static string GetVatAffectationCode(InvoiceVatType vatType)
    {
        return vatType switch
        {
            InvoiceVatType.Vat10 => "1",
            InvoiceVatType.Vat5 => "1",
            InvoiceVatType.Exempt => "3",
            _ => throw new DomainException("Unsupported VAT type.")
        };
    }

    private static decimal GetVatRate(InvoiceVatType vatType)
    {
        return vatType switch
        {
            InvoiceVatType.Vat10 => 10m,
            InvoiceVatType.Vat5 => 5m,
            InvoiceVatType.Exempt => 0m,
            _ => throw new DomainException("Unsupported VAT type.")
        };
    }

    private static string FormatTimestamp(DateTimeOffset value)
    {
        return value.ToString("yyyy-MM-ddTHH:mm:ss", Invariant);
    }

    private static string FormatDecimal(decimal value)
    {
        return value.ToString("0.######", Invariant);
    }

    private static decimal Round(decimal value)
    {
        return Math.Round(value, 2, MidpointRounding.AwayFromZero);
    }

    private static void ValidateRequired(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException($"{fieldName} is required.");
        }
    }

    private static void ValidateGeneratedResult(GeneratedFacturaXmlResult result)
    {
        if (!CdcGenerator.ValidateCDC(result.Cdc))
        {
            throw new DomainException("Generated CDC is invalid.");
        }

        var document = XDocument.Parse(result.Xml, LoadOptions.PreserveWhitespace);
        var de = document.Root?.Element(Ns + "DE")
            ?? throw new DomainException("Generated FE XML must contain DE.");
        var totals = de.Element(Ns + "gTotSub")
            ?? throw new DomainException("Generated FE XML must contain gTotSub.");

        ValidateDecimalElement(totals, "dSubExe", result.TotalExento);
        ValidateDecimalElement(totals, "dSub5", result.TotalGravado5);
        ValidateDecimalElement(totals, "dSub10", result.TotalGravado10);
        ValidateDecimalElement(totals, "dTotIVA", result.TotalIva);
        ValidateDecimalElement(totals, "dTotGralOpe", result.TotalGeneral);

        if (Round(result.TotalGeneral) != Round(result.TotalGravado10 + result.TotalGravado5 + result.TotalExento))
        {
            throw new DomainException("Generated FE XML totals are inconsistent.");
        }
    }

    private static void ValidateDecimalElement(XElement totals, string elementName, decimal expected)
    {
        var value = totals.Element(Ns + elementName)?.Value;
        if (!decimal.TryParse(value, NumberStyles.Number, Invariant, out var parsed))
        {
            throw new DomainException($"Generated FE XML must contain numeric total {elementName}.");
        }

        if (Round(parsed) != Round(expected))
        {
            throw new DomainException("Generated FE XML totals are inconsistent.");
        }
    }

    private sealed record InvoiceItem(
        string Code,
        string Description,
        decimal Quantity,
        decimal UnitPrice,
        decimal Total,
        InvoiceVatType VatType);
}
