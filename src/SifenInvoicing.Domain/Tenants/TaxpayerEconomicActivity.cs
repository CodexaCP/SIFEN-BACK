using SifenInvoicing.Domain.Common;

namespace SifenInvoicing.Domain.Tenants;

/// <summary>
/// gActEco (Manual v150 D130-D132, 1-9 por emisor): cActEco (A, 1-8) y dDesActEco (A, 1-300). El codigo y la descripcion
/// los define la DNIT (Tabla 3, consulta publica en servicios.set.gov.py); el sistema no los infiere ni los inventa.
/// </summary>
public sealed class TaxpayerEconomicActivity : TenantScopedEntity
{
    public const int MaxPerTaxpayer = 9;

    private TaxpayerEconomicActivity()
    {
        Code = string.Empty;
        Description = string.Empty;
    }

    private TaxpayerEconomicActivity(Guid id, Guid tenantId, Guid taxpayerProfileId, string code, string description, int sortOrder)
        : base(id, tenantId)
    {
        TaxpayerProfileId = taxpayerProfileId == Guid.Empty
            ? throw new DomainException("taxpayerProfileId is required.")
            : taxpayerProfileId;
        Code = RequireValue(code, nameof(code), 8);
        Description = RequireValue(description, nameof(description), 300);
        SortOrder = sortOrder;
    }

    public Guid TaxpayerProfileId { get; private set; }

    public string Code { get; private set; }

    public string Description { get; private set; }

    public int SortOrder { get; private set; }

    public static TaxpayerEconomicActivity Create(
        Guid tenantId,
        Guid taxpayerProfileId,
        string code,
        string description,
        int sortOrder = 0)
        => new(Guid.NewGuid(), tenantId, taxpayerProfileId, code, description, sortOrder);

    private static string RequireValue(string value, string parameterName, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException($"{parameterName} is required.");
        }

        var normalized = value.Trim();
        return normalized.Length <= maxLength
            ? normalized
            : throw new DomainException($"{parameterName} cannot exceed {maxLength} characters.");
    }
}
