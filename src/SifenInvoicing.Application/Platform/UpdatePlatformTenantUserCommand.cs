namespace SifenInvoicing.Application.Platform;

public sealed record UpdatePlatformTenantUserCommand(
    Guid TenantId,
    Guid UserId,
    string FullName,
    string Email,
    string Role,
    bool IsActive);
