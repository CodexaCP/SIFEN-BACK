namespace SifenInvoicing.Application.Onboarding;

public sealed record TenantReadinessCheck(
    string Name,
    bool IsReady,
    string Summary);
