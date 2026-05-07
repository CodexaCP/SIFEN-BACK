namespace SifenInvoicing.Infrastructure.XmlSigning;

public static class SifenXmlSignatureAlgorithms
{
    public const string CanonicalizationMethod = "http://www.w3.org/TR/2001/REC-xml-c14n-20010315";
    public const string SignatureMethod = "http://www.w3.org/2001/04/xmldsig-more#rsa-sha256";
    public const string DigestMethod = "http://www.w3.org/2001/04/xmlenc#sha256";
    public const string EnvelopedSignatureTransform = "http://www.w3.org/2000/09/xmldsig#enveloped-signature";
}
