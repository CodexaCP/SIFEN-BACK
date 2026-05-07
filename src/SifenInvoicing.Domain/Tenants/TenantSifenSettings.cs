using SifenInvoicing.Domain.Common;

namespace SifenInvoicing.Domain.Tenants;

public sealed class TenantSifenSettings : TenantScopedEntity
{
    private TenantSifenSettings()
    {
        CscSecretReference = string.Empty;
        EstablishmentCode = string.Empty;
        ExpeditionPointCode = string.Empty;
        CurrentDocumentNumber = string.Empty;
    }

    private TenantSifenSettings(
        Guid id,
        Guid tenantId,
        SifenEnvironmentType environment,
        string? cscIdentifier,
        string cscSecretReference,
        string establishmentCode,
        string expeditionPointCode,
        string currentDocumentNumber,
        string? stampingNumber,
        string? certificateSecretReference,
        string? certificatePasswordSecretReference,
        string? certificateAlias,
        string? xmlSchemaRootPath,
        string? endpointUrl,
        string? transportMode)
        : base(id, tenantId)
    {
        Environment = environment;
        CscIdentifier = string.IsNullOrWhiteSpace(cscIdentifier) ? null : cscIdentifier.Trim();
        CscSecretReference = RequireValue(cscSecretReference, nameof(cscSecretReference));
        EstablishmentCode = RequireValue(establishmentCode, nameof(establishmentCode));
        ExpeditionPointCode = RequireValue(expeditionPointCode, nameof(expeditionPointCode));
        CurrentDocumentNumber = RequireValue(currentDocumentNumber, nameof(currentDocumentNumber));
        StampingNumber = NormalizeOptional(stampingNumber);
        CertificateSecretReference = NormalizeOptional(certificateSecretReference);
        CertificatePasswordSecretReference = NormalizeOptional(certificatePasswordSecretReference);
        CertificateAlias = NormalizeOptional(certificateAlias);
        XmlSchemaRootPath = NormalizeOptional(xmlSchemaRootPath);
        EndpointUrl = NormalizeOptional(endpointUrl);
        TransportMode = NormalizeOptional(transportMode);
        IsActive = true;
    }

    public SifenEnvironmentType Environment { get; private set; }

    public string? CscIdentifier { get; private set; }

    public string CscSecretReference { get; private set; }

    public string EstablishmentCode { get; private set; }

    public string ExpeditionPointCode { get; private set; }

    public string CurrentDocumentNumber { get; private set; }

    public string? StampingNumber { get; private set; }

    public string? CertificateSecretReference { get; private set; }

    public string? CertificatePasswordSecretReference { get; private set; }

    public string? CertificateAlias { get; private set; }

    public string? XmlSchemaRootPath { get; private set; }

    public string? EndpointUrl { get; private set; }

    public string? TransportMode { get; private set; }

    public bool IsActive { get; private set; }

    public static TenantSifenSettings Create(
        Guid tenantId,
        SifenEnvironmentType environment,
        string? cscIdentifier,
        string cscSecretReference,
        string establishmentCode,
        string expeditionPointCode,
        string currentDocumentNumber,
        string? stampingNumber,
        string? certificateSecretReference,
        string? certificatePasswordSecretReference,
        string? certificateAlias,
        string? xmlSchemaRootPath,
        string? endpointUrl,
        string? transportMode)
    {
        return new TenantSifenSettings(
            Guid.NewGuid(),
            tenantId,
            environment,
            cscIdentifier,
            cscSecretReference,
            establishmentCode,
            expeditionPointCode,
            currentDocumentNumber,
            stampingNumber,
            certificateSecretReference,
            certificatePasswordSecretReference,
            certificateAlias,
            xmlSchemaRootPath,
            endpointUrl,
            transportMode);
    }

    public void Update(
        SifenEnvironmentType environment,
        string? cscIdentifier,
        string cscSecretReference,
        string establishmentCode,
        string expeditionPointCode,
        string currentDocumentNumber,
        string? stampingNumber,
        string? certificateSecretReference,
        string? certificatePasswordSecretReference,
        string? certificateAlias,
        string? xmlSchemaRootPath,
        string? endpointUrl,
        string? transportMode)
    {
        Environment = environment;
        CscIdentifier = NormalizeOptional(cscIdentifier);
        CscSecretReference = RequireValue(cscSecretReference, nameof(cscSecretReference));
        EstablishmentCode = RequireValue(establishmentCode, nameof(establishmentCode));
        ExpeditionPointCode = RequireValue(expeditionPointCode, nameof(expeditionPointCode));
        CurrentDocumentNumber = RequireValue(currentDocumentNumber, nameof(currentDocumentNumber));
        StampingNumber = NormalizeOptional(stampingNumber);
        CertificateSecretReference = NormalizeOptional(certificateSecretReference);
        CertificatePasswordSecretReference = NormalizeOptional(certificatePasswordSecretReference);
        CertificateAlias = NormalizeOptional(certificateAlias);
        XmlSchemaRootPath = NormalizeOptional(xmlSchemaRootPath);
        EndpointUrl = NormalizeOptional(endpointUrl);
        TransportMode = NormalizeOptional(transportMode);
        IsActive = true;
    }

    private static string RequireValue(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException($"{parameterName} is required.");
        }

        return value.Trim();
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
