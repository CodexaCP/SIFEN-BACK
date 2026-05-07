namespace SifenInvoicing.Application.Operations;

public interface IOperationalReadinessReporter
{
    Task<IReadOnlyCollection<OperationalDependencyStatus>> GetSnapshotAsync(
        CancellationToken cancellationToken = default);

    Task<TenantFeOperationalDiagnostic> GetTenantFeDiagnosticAsync(
        Guid tenantId,
        Domain.Tenants.SifenEnvironmentType environment,
        CancellationToken cancellationToken = default);

    Task<TenantFeOperationalDiagnostic> GetTenantFeDiagnosticAsync(
        Domain.Tenants.SifenEnvironmentType environment,
        CancellationToken cancellationToken = default);

    Task<TenantFeOperationalDiagnostic> GetTenantFeDiagnosticAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);

    Task<TenantFeOperationalDiagnostic> GetTenantFeDiagnosticAsync(
        CancellationToken cancellationToken = default);
}
