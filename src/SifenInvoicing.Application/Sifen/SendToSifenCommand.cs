using SifenInvoicing.Domain.Tenants;

namespace SifenInvoicing.Application.Sifen;

public sealed record SendToSifenCommand(
    Guid TenantId,
    SifenEnvironmentType Environment,
    string Cdc,
    string SignedXml);
