namespace SifenInvoicing.Application.XmlValidation;

public sealed record XmlSchemaValidationError(
    string Message,
    string? LineNumber,
    string? LinePosition);
