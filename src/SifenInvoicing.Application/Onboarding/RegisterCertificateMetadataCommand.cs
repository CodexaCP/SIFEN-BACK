using SifenInvoicing.Domain.Tenants;

namespace SifenInvoicing.Application.Onboarding;

public sealed record RegisterCertificateMetadataCommand(
    Guid TenantId,
    SifenEnvironmentType Environment,
    CertificatePurpose Purpose,
    string Alias,
    string Subject,
    string FingerprintSha256,
    string SerialNumber,
    string CertificateSecretReference,
    string CertificatePasswordSecretReference,
    DateTimeOffset ValidFrom,
    DateTimeOffset ValidTo);
