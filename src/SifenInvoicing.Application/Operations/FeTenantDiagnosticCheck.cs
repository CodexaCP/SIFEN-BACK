namespace SifenInvoicing.Application.Operations;

public sealed record FeTenantDiagnosticCheck(
    string Key,
    string Label,
    string Status,
    string Message);
