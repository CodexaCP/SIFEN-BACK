using SifenInvoicing.Domain.Tenants;

namespace SifenInvoicing.Application.Onboarding;

/// <summary>Datos fiscales del emisor (perfil del contribuyente). Codigos geograficos: PENDIENTE [DOC] (tablas oficiales).</summary>
public sealed record RegisterFiscalProfileCommand(
    Guid TenantId,
    int TaxpayerType,
    string Address,
    string? HouseNumber,
    string? DepartmentCode,
    string? DepartmentDescription,
    string? DistrictCode,
    string? DistrictDescription,
    string? CityCode,
    string? CityDescription,
    string? Phone,
    string? Email);

public sealed record RegisterFiscalStampCommand(
    Guid TenantId,
    SifenEnvironmentType Environment,
    string StampingNumber,
    DateOnly ValidFrom,
    DateOnly? ValidTo);

public sealed record RegisterNumberingSequenceCommand(
    Guid TenantId,
    SifenEnvironmentType Environment,
    string StampingNumber,
    string DocumentTypeCode,
    string EstablishmentCode,
    string ExpeditionPointCode,
    string? Series,
    long FirstNumber);
