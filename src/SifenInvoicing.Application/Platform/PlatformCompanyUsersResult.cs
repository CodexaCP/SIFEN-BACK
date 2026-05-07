namespace SifenInvoicing.Application.Platform;

public sealed record PlatformCompanyUsersResult(
    Guid TenantId,
    string CompanyName,
    string PlanName,
    int ActiveUsers,
    int? MaxUsers,
    IReadOnlyCollection<PlatformCompanyUserSummary> Users);
