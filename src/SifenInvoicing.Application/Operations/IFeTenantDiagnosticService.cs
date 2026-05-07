namespace SifenInvoicing.Application.Operations;

public interface IFeTenantDiagnosticService
{
    Task<FeTenantDiagnosticResult> GetDiagnosticAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);
}
