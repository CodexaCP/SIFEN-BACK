namespace SifenInvoicing.Application.Platform;

public sealed record CreatePlatformCompanyAdminCommand(
    Guid TenantId,
    string FullName,
    string Email,
    string Password);
