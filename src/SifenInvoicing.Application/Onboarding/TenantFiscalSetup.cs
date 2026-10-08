using SifenInvoicing.Domain.Tenants;

namespace SifenInvoicing.Application.Onboarding;

/// <summary>Estado registrado del alta fiscal de un tenant por ambiente. Solo referencias de secretos, nunca valores.</summary>
public sealed record TenantFiscalSetup(
    Guid TenantId,
    SifenEnvironmentType Environment,
    TenantFiscalProfileView? Profile,
    IReadOnlyList<TenantFiscalStampView> Stamps,
    IReadOnlyList<TenantNumberingSequenceView> NumberingSequences,
    IReadOnlyList<TenantCertificateView> Certificates);

public sealed record TenantFiscalProfileView(
    string RucNumber,
    string RucCheckDigit,
    string LegalName,
    string? TradeName,
    int? TaxpayerType,
    string? Address,
    string? HouseNumber,
    string? DepartmentCode,
    string? DepartmentDescription,
    string? DistrictCode,
    string? DistrictDescription,
    string? CityCode,
    string? CityDescription,
    string? Phone,
    string? Email,
    IReadOnlyList<RegisterEconomicActivity> EconomicActivities);

public sealed record TenantFiscalStampView(
    Guid Id,
    string StampingNumber,
    DateOnly ValidFrom,
    DateOnly? ValidTo,
    bool IsActive);

public sealed record TenantNumberingSequenceView(
    Guid Id,
    string StampingNumber,
    string DocumentTypeCode,
    string EstablishmentCode,
    string ExpeditionPointCode,
    string Series,
    long NextNumber,
    bool IsActive);

public sealed record TenantCertificateView(
    Guid Id,
    CertificatePurpose Purpose,
    string Alias,
    string Subject,
    string FingerprintSha256,
    string SerialNumber,
    string CertificateSecretReference,
    string CertificatePasswordSecretReference,
    DateTimeOffset ValidFrom,
    DateTimeOffset ValidTo,
    bool IsActive);
