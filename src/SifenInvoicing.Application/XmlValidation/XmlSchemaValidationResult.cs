namespace SifenInvoicing.Application.XmlValidation;

public sealed record XmlSchemaValidationResult(
    bool IsValid,
    IReadOnlyCollection<XmlSchemaValidationError> Errors);
