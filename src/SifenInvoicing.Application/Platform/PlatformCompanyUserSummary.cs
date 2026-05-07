namespace SifenInvoicing.Application.Platform;

public sealed record PlatformCompanyUserSummary(
    Guid UserId,
    Guid TenantId,
    string FullName,
    string Email,
    string Role,
    bool IsActive);
