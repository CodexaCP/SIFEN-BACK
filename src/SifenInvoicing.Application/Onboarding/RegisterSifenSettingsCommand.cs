using SifenInvoicing.Domain.Tenants;

namespace SifenInvoicing.Application.Onboarding;

public sealed record RegisterSifenSettingsCommand(
    Guid TenantId,
    SifenEnvironmentType Environment,
    string? CscIdentifier,
    string CscSecretReference,
    string EstablishmentCode,
    string ExpeditionPointCode,
    string CurrentDocumentNumber,
    string? StampingNumber,
    string? CertificateSecretReference,
    string? CertificatePasswordSecretReference,
    string? CertificateAlias,
    string? XmlSchemaRootPath,
    string? EndpointUrl,
    string? TransportMode);
