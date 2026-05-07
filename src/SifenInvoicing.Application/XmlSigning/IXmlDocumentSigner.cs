namespace SifenInvoicing.Application.XmlSigning;

public interface IXmlDocumentSigner
{
    Task<SignedXmlDocumentResult> SignAsync(
        SignXmlDocumentCommand command,
        CancellationToken cancellationToken = default);
}
