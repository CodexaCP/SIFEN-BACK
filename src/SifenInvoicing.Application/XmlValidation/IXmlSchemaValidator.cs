namespace SifenInvoicing.Application.XmlValidation;

public interface IXmlSchemaValidator
{
    Task<XmlSchemaValidationResult> ValidateAsync(
        string xml,
        string rootSchemaPath,
        CancellationToken cancellationToken = default);
}
