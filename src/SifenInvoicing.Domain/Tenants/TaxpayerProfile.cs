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
