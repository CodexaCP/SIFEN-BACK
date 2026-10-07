using SifenInvoicing.Domain.Common;

namespace SifenInvoicing.Domain.Tenants;

/// <summary>
/// Timbrado del contribuyente (Manual v150 gTimb: dNumTim, dFeIniT, dFeFinT). Configuracion fiscal por tenant y ambiente.
/// PENDIENTE [XSD]: obligatoriedad/uso de dFeFinT (contradiccion Manual vs. timbrado electronico sin fecha fin).
/// </summary>
public sealed class FiscalStamp : TenantScopedEntity
{
    private FiscalStamp()
    {
        StampingNumber = string.Empty;
    }

    private FiscalStamp(
        Guid id,
        Guid tenantId,
        SifenEnvironmentType environment,
        string stampingNumber,
        DateOnly validFrom,
        DateOnly? validTo)
        : base(id, tenantId)
    {
        if (string.IsNullOrWhiteSpace(stampingNumber) ||
            stampingNumber.Trim().Length != 8 ||
            !stampingNumber.Trim().All(char.IsAsciiDigit))
        {
            throw new DomainException("stampingNumber must contain exactly 8 digits (dNumTim).");
        }

        if (validTo.HasValue && validTo.Value < validFrom)
        {
            throw new DomainException("validTo cannot be earlier than validFrom.");
        }

        Environment = environment;
        StampingNumber = stampingNumber.Trim();
        ValidFrom = validFrom;
        ValidTo = validTo;
        IsActive = true;
    }

    public SifenEnvironmentType Environment { get; private set; }

    /// <summary>dNumTim: 8 digitos.</summary>
    public string StampingNumber { get; private set; }

    /// <summary>dFeIniT: inicio de vigencia.</summary>
    public DateOnly ValidFrom { get; private set; }

    /// <summary>dFeFinT: fin de vigencia, opcional.</summary>
    public DateOnly? ValidTo { get; private set; }

    public bool IsActive { get; private set; }

    public static FiscalStamp Create(
        Guid tenantId,
        SifenEnvironmentType environment,
        string stampingNumber,
        DateOnly validFrom,
        DateOnly? validTo = null) =>
        new(Guid.NewGuid(), tenantId, environment, stampingNumber, validFrom, validTo);

    public void Deactivate() => IsActive = false;

    public bool IsValidOn(DateOnly date) => IsActive && date >= ValidFrom && (!ValidTo.HasValue || date <= ValidTo.Value);
}
