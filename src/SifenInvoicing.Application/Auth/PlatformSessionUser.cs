namespace SifenInvoicing.Application.Auth;

public sealed record PlatformSessionUser(
    Guid UserId,
    int CompanyId,
    Guid? TenantId,
    string FullName,
    string Email,
    string Role,
    IReadOnlyCollection<string> Permissions,
    string? TenantName = null);
