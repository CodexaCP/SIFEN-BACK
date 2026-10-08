using System.Xml.Linq;
using SifenInvoicing.Domain.Common;

namespace SifenInvoicing.Application.Qr;

/// <summary>
/// Lee del rDE firmado los datos del QR (Manual v150 13.8.2): Id (A002), dFeEmiDE (D002), receptor (dRucRec D206 o
/// dNumIDRec D210), dTotGralOpe, dTotIVA, cantidad de gCamItem (E701) y DigestValue de la Signature (XS17). Una unica
/// fuente (el XML que se persiste) evita que el QR difiera del documento.
/// </summary>
public static class SifenDeQrDataExtractor
{
    private static readonly XNamespace Sifen = "http://ekuatia.set.gov.py/sifen/xsd";
    private static readonly XNamespace Ds = "http://www.w3.org/2000/09/xmldsig#";

    public sealed record QrData(
        string Cdc,
        string EmissionDateTimeText,
        string ReceiverParameterName,
        string ReceiverDocument,
        decimal TotalGeneral,
        decimal TotalIva,
        int ItemCount,
        string DigestValueBase64);

    public static QrData Extract(XDocument signedDe)
    {
        var root = signedDe.Root;
        var de = root?.Element(Sifen + "DE");
        var cdc = de?.Attribute("Id")?.Value;
        if (root is null || de is null || string.IsNullOrEmpty(cdc))
        {
            throw new DomainException("El rDE firmado no contiene DE@Id: no se puede generar el QR.");
        }

        var signatures = root.Elements(Ds + "Signature").ToList();
        if (signatures.Count != 1)
        {
            throw new DomainException("El QR requiere exactamente una ds:Signature hija de rDE (se genera despues de firmar).");
        }

        var reference = signatures[0].Element(Ds + "SignedInfo")?.Element(Ds + "Reference");
        var digest = reference?.Element(Ds + "DigestValue")?.Value.Trim();
        if (reference?.Attribute("URI")?.Value != "#" + cdc || string.IsNullOrEmpty(digest))
        {
            throw new DomainException("La Signature no referencia #CDC o no contiene DigestValue: no se puede generar el QR.");
        }

        var general = de.Element(Sifen + "gDatGralOpe");
        var emission = general?.Element(Sifen + "dFeEmiDE")?.Value;
        if (string.IsNullOrEmpty(emission))
        {
            throw new DomainException("El DE no contiene dFeEmiDE: no se puede generar el QR.");
        }

        var receiver = general!.Element(Sifen + "gDatRec");
        var ruc = receiver?.Element(Sifen + "dRucRec")?.Value;
        var identity = receiver?.Element(Sifen + "dNumIDRec")?.Value;
        var (receiverName, receiverValue) = !string.IsNullOrEmpty(ruc)
            ? ("dRucRec", ruc)
            : !string.IsNullOrEmpty(identity) ? ("dNumIDRec", identity) : ("dRucRec", "0");

        var totals = de.Element(Sifen + "gTotSub");
        var items = de.Element(Sifen + "gDtipDE")?.Elements(Sifen + "gCamItem").Count() ?? 0;

        return new QrData(
            cdc,
            emission,
            receiverName,
            receiverValue,
            ReadAmount(totals, "dTotGralOpe"),
            ReadAmount(totals, "dTotIVA"),
            items,
            digest);
    }

    /// <summary>Manual 13.8.2 (*): si el campo no tiene valor se completa con 0.</summary>
    private static decimal ReadAmount(XElement? totals, string name)
    {
        var text = totals?.Element(Sifen + name)?.Value;
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0m;
        }

        return decimal.Parse(text, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture);
    }
}
