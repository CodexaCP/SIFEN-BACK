namespace SifenInvoicing.Application.Platform;

public sealed record PlatformCompanySummary(
    Guid Id,
    string Slug,
    string DisplayName,
    string PlanName,
    string Status,
    int? MaxInvoicesPerMonth,
    int? MaxUsers);
