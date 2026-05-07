using SifenInvoicing.Domain.Tenants;

namespace SifenInvoicing.Application.Onboarding;

public sealed record TenantReadinessReport(
    Guid TenantId,
    SifenEnvironmentType Environment,
    bool IsReadyForSifenTest,
    IReadOnlyCollection<TenantReadinessCheck> Checks);
