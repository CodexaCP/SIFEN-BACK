namespace SifenInvoicing.Application.Platform;

public sealed record UpdatePlatformCompanyPlanCommand(
    Guid TenantId,
    string? PlanName,
    int? MaxInvoicesPerMonth,
    int? MaxUsers,
    bool? Active);
