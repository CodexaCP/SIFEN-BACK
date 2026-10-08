namespace SifenInvoicing.Application.Platform;

/// <summary>Borrado total de una compania. ConfirmationSlug debe repetir el slug; ActorTenantId es la compania de quien borra.</summary>
public sealed record DeletePlatformCompanyCommand(
    Guid TenantId,
    string? ConfirmationSlug,
    Guid? ActorTenantId);
