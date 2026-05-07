namespace SifenInvoicing.Application.Operations;

public sealed record FeTenantDiagnosticResult(
    Guid TenantId,
    string GlobalStatus,
    string Mode,
    IReadOnlyCollection<FeTenantDiagnosticCheck> Checks);
