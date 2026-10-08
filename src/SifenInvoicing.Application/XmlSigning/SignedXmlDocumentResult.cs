namespace SifenInvoicing.Application.XmlSigning;

public sealed record SignedXmlDocumentResult(
    string SignedXml,
    string DocumentId,
    string CanonicalizationMethod,
    string SignatureMethod,
    string DigestMethod,
    string TransformMethod,
    string? DigestValue = null,
    string? CertificateFingerprintSha256 = null);
