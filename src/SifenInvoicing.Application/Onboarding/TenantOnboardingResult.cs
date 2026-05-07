namespace SifenInvoicing.Application.Onboarding;

public sealed record TenantOnboardingResult(
    Guid Id,
    string Slug,
    string DisplayName,
    string Status,
    string IsolationMode,
    int? MaxInvoicesPerMonth,
    int? MaxUsers);
