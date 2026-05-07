namespace SifenInvoicing.Application.Platform;

public sealed record CreatePlatformCompanyCommand(
    string Slug,
    string DisplayName,
    string PlanName,
    int? MaxInvoicesPerMonth,
    int? MaxUsers,
    string? AdminFullName = null,
    string? AdminEmail = null,
    string? AdminPassword = null);
