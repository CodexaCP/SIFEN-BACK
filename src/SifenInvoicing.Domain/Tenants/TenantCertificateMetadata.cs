using SifenInvoicing.Domain.Common;

namespace SifenInvoicing.Domain.Tenants;

public sealed class TenantCertificateMetadata : TenantScopedEntity
{
    private TenantCertificateMetadata()
    {
        Alias = string.Empty;
        Subject = string.Empty;
        FingerprintSha256 = string.Empty;
        SerialNumber = string.Empty;
        CertificateSecretReference = string.Empty;
        CertificatePasswordSecretReference = string.Empty;
    }

    private TenantCertificateMetadata(
        Guid id,
        Guid tenantId,
        SifenEnvironmentType environment,
        CertificatePurpose purpose,
        string alias,
        string subject,
        string fingerprintSha256,
        string serialNumber,
        string certificateSecretReference,
        string certificatePasswordSecretReference,
        DateTimeOffset validFrom,
        DateTimeOffset validTo)
        : base(id, tenantId)
    {
        Environment = environment;
        Purpose = purpose;
        Alias = RequireValue(alias, nameof(alias));
        Subject = RequireValue(subject, nameof(subject));
        FingerprintSha256 = RequireValue(fingerprintSha256, nameof(fingerprintSha256));
        SerialNumber = RequireValue(serialNumber, nameof(serialNumber));
        CertificateSecretReference = RequireValue(certificateSecretReference, nameof(certificateSecretReference));
        CertificatePasswordSecretReference = RequireValue(certificatePasswordSecretReference, nameof(certificatePasswordSecretReference));
        ValidFrom = validFrom;
        ValidTo = validTo;
        IsActive = true;
    }

    public SifenEnvironmentType Environment { get; private set; }

    public CertificatePurpose Purpose { get; private set; }

    public string Alias { get; private set; }

    public string Subject { get; private set; }

    public string FingerprintSha256 { get; private set; }

    public string SerialNumber { get; private set; }

    public string CertificateSecretReference { get; private set; }

    public string CertificatePasswordSecretReference { get; private set; }

    public DateTimeOffset ValidFrom { get; private set; }

    public DateTimeOffset ValidTo { get; private set; }

    public bool IsActive { get; private set; }

    public static TenantCertificateMetadata Create(
        Guid tenantId,
        SifenEnvironmentType environment,
        CertificatePurpose purpose,
        string alias,
        string subject,
        string fingerprintSha256,
        string serialNumber,
        string certificateSecretReference,
        string certificatePasswordSecretReference,
        DateTimeOffset validFrom,
        DateTimeOffset validTo)
    {
        if (validTo <= validFrom)
        {
            throw new DomainException("Certificate valid-to date must be after valid-from date.");
        }

        return new TenantCertificateMetadata(
            Guid.NewGuid(),
            tenantId,
            environment,
            purpose,
            alias,
            subject,
            fingerprintSha256,
            serialNumber,
            certificateSecretReference,
            certificatePasswordSecretReference,
            validFrom,
            validTo);
    }

    private static string RequireValue(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException($"{parameterName} is required.");
        }

        return value.Trim();
    }
}
