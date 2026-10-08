using System.Xml.Linq;

namespace SifenInvoicing.Tests;

/// <summary>
/// SOLO PRUEBAS. Inserta una ds:Signature estructuralmente valida pero SIN valor criptografico tras el DE, para los fakes de
/// <c>IXmlDocumentSigner</c> que no prueban criptografia. Nunca es una firma real ni evidencia de aceptacion SIFEN.
/// </summary>
internal static class StructuralSignatureStub
{
    private static readonly XNamespace Sifen = "http://ekuatia.set.gov.py/sifen/xsd";
    private static readonly XNamespace Ds = "http://www.w3.org/2000/09/xmldsig#";

    public static string Apply(string xml)
    {
        var document = XDocument.Parse(xml);
        var de = document.Root?.Element(Sifen + "DE");
        var cdc = de?.Attribute("Id")?.Value;
        if (de is null || cdc is null)
        {
            return xml;
        }

        de.AddAfterSelf(new XElement(Ds + "Signature",
            new XElement(Ds + "SignedInfo",
                new XElement(Ds + "CanonicalizationMethod", new XAttribute("Algorithm", "http://www.w3.org/TR/2001/REC-xml-c14n-20010315")),
                new XElement(Ds + "SignatureMethod", new XAttribute("Algorithm", "http://www.w3.org/2001/04/xmldsig-more#rsa-sha256")),
                new XElement(Ds + "Reference", new XAttribute("URI", "#" + cdc),
                    new XElement(Ds + "Transforms", new XElement(Ds + "Transform", new XAttribute("Algorithm", "http://www.w3.org/2000/09/xmldsig#enveloped-signature"))),
                    new XElement(Ds + "DigestMethod", new XAttribute("Algorithm", "http://www.w3.org/2001/04/xmlenc#sha256")),
                    new XElement(Ds + "DigestValue", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA="))),
            new XElement(Ds + "SignatureValue", "AAAA"),
            new XElement(Ds + "KeyInfo", new XElement(Ds + "X509Data", new XElement(Ds + "X509Certificate", "AAAA")))));
        return document.ToString(SaveOptions.DisableFormatting);
    }
}
