using SifenInvoicing.Domain.Common;

namespace SifenInvoicing.Domain.Tenants;

public sealed class TaxpayerProfile : TenantScopedEntity
{
    private TaxpayerProfile()
    {
        RucNumber = string.Empty;
        RucCheckDigit = string.Empty;
        LegalName = string.Empty;
    }

    private TaxpayerProfile(
        Guid id,
        Guid tenantId,
        string rucNumber,
        string rucCheckDigit,
        string legalName,
        string? tradeName)
        : base(id, tenantId)
    {
        RucNumber = RequireValue(rucNumber, nameof(rucNumber));
        RucCheckDigit = RequireValue(rucCheckDigit, nameof(rucCheckDigit));
        LegalName = RequireValue(legalName, nameof(legalName));
        TradeName = string.IsNullOrWhiteSpace(tradeName) ? null : tradeName.Trim();
        IsActive = true;
    }

    public string RucNumber { get; private set; }

    public string RucCheckDigit { get; private set; }

    public string LegalName { get; private set; }

    public string? TradeName { get; private set; }

    public bool IsActive { get; private set; }

    /// <summary>iTipCont (Manual v150 D013): 1 = persona fisica, 2 = persona juridica. Alimenta el CDC.</summary>
    public int? TaxpayerType { get; private set; }

    /// <summary>dDirEmi (D106). Direccion del emisor: dato fiscal del tenant, nunca del request.</summary>
    public string? Address { get; private set; }

    public string? HouseNumber { get; private set; }

    /// <summary>Codigos y descripciones de departamento/distrito/ciudad: PENDIENTE [DOC] (tablas oficiales).</summary>
    public string? DepartmentCode { get; private set; }

    public string? DepartmentDescription { get; private set; }

    public string? DistrictCode { get; private set; }

    public string? DistrictDescription { get; private set; }

    public string? CityCode { get; private set; }

    public string? CityDescription { get; private set; }

    public string? Phone { get; private set; }

    public string? Email { get; private set; }

    public void UpdateFiscalData(
        int taxpayerType,
        string address,
        string? houseNumber,
        string? departmentCode,
        string? departmentDescription,
        string? districtCode,
        string? districtDescription,
        string? cityCode,
        string? cityDescription,
        string? phone,
        string? email)
    {
        if (taxpayerType is not (1 or 2))
        {
            throw new DomainException("taxpayerType must be 1 (persona fisica) or 2 (persona juridica).");
        }

        TaxpayerType = taxpayerType;
        Address = RequireValue(address, nameof(address));
        HouseNumber = Optional(houseNumber);
        DepartmentCode = Optional(departmentCode);
        DepartmentDescription = Optional(departmentDescription);
        DistrictCode = Optional(districtCode);
        DistrictDescription = Optional(districtDescription);
        CityCode = Optional(cityCode);
        CityDescription = Optional(cityDescription);
        Phone = Optional(phone);
        Email = Optional(email);
    }

    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static TaxpayerProfile Create(
        Guid tenantId,
        string rucNumber,
        string rucCheckDigit,
        string legalName,
        string? tradeName = null)
    {
        return new TaxpayerProfile(Guid.NewGuid(), tenantId, rucNumber, rucCheckDigit, legalName, tradeName);
    }

    public void Update(
        string rucNumber,
        string rucCheckDigit,
        string legalName,
        string? tradeName = null)
    {
        RucNumber = RequireValue(rucNumber, nameof(rucNumber));
        RucCheckDigit = RequireValue(rucCheckDigit, nameof(rucCheckDigit));
        LegalName = RequireValue(legalName, nameof(legalName));
        TradeName = string.IsNullOrWhiteSpace(tradeName) ? null : tradeName.Trim();
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
}
