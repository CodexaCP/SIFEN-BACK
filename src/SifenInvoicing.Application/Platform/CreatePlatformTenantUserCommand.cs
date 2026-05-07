namespace SifenInvoicing.Application.Platform;

public sealed record CreatePlatformTenantUserCommand(
    Guid TenantId,
    string FullName,
    string Email,
    string Password,
    string Role,
    bool IsActive);
