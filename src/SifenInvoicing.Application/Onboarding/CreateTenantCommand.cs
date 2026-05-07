namespace SifenInvoicing.Application.Onboarding;

public sealed record CreateTenantCommand(
    string Slug,
    string DisplayName,
    int? MaxInvoicesPerMonth,
    int? MaxUsers);
