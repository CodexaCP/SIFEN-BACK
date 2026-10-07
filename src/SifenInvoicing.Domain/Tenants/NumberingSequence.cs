using SifenInvoicing.Domain.Common;

namespace SifenInvoicing.Domain.Tenants;

/// <summary>
/// Secuencia de numeracion por timbrado + tipo de documento + establecimiento + punto de expedicion (+ serie).
/// Clave oficial: Manual v150 sec. 10.5 (p.59) y C005-C007. Numero: 0000001-9999999.
/// La reserva es atomica: <see cref="Version"/> es un token de concurrencia que el servicio verifica al guardar.
/// </summary>
public sealed class NumberingSequence : TenantScopedEntity
{
    public const long MaxDocumentNumber = 9_999_999;

    private NumberingSequence()
    {
        DocumentTypeCode = string.Empty;
        EstablishmentCode = string.Empty;
        ExpeditionPointCode = string.Empty;
    }

    private NumberingSequence(
        Guid id,
        Guid tenantId,
        SifenEnvironmentType environment,
        Guid fiscalStampId,
        string documentTypeCode,
        string establishmentCode,
        string expeditionPointCode,
        string? series,
        long nextNumber)
        : base(id, tenantId)
    {
        if (fiscalStampId == Guid.Empty)
        {
            throw new DomainException("fiscalStampId is required.");
        }

        if (nextNumber < 1 || nextNumber > MaxDocumentNumber)
        {
            throw new DomainException($"nextNumber must be between 1 and {MaxDocumentNumber}.");
        }

        Environment = environment;
        FiscalStampId = fiscalStampId;
        DocumentTypeCode = RequireDigits(documentTypeCode, 2, nameof(documentTypeCode));
        EstablishmentCode = RequireDigits(establishmentCode, 3, nameof(establishmentCode));
        ExpeditionPointCode = RequireDigits(expeditionPointCode, 3, nameof(expeditionPointCode));
        Series = string.IsNullOrWhiteSpace(series) ? string.Empty : series.Trim();
        NextNumber = nextNumber;
        IsActive = true;
    }

    public SifenEnvironmentType Environment { get; private set; }

    public Guid FiscalStampId { get; private set; }

    /// <summary>iTiDE: "01" factura electronica.</summary>
    public string DocumentTypeCode { get; private set; }

    public string EstablishmentCode { get; private set; }

    public string ExpeditionPointCode { get; private set; }

    /// <summary>Serie; cadena vacia cuando no aplica (no NULL: asi la unicidad funciona igual en cualquier motor).</summary>
    public string Series { get; private set; } = string.Empty;

    /// <summary>Proximo numero a entregar.</summary>
    public long NextNumber { get; private set; }

    /// <summary>Token de concurrencia (se incrementa en cada reserva).</summary>
    public long Version { get; private set; }

    public bool IsActive { get; private set; }

    public static NumberingSequence Create(
        Guid tenantId,
        SifenEnvironmentType environment,
        Guid fiscalStampId,
        string documentTypeCode,
        string establishmentCode,
        string expeditionPointCode,
        string? series = null,
        long firstNumber = 1) =>
        new(Guid.NewGuid(), tenantId, environment, fiscalStampId, documentTypeCode, establishmentCode, expeditionPointCode, series, firstNumber);

    /// <summary>Entrega el siguiente numero. El llamador debe persistirlo en la misma transaccion que el documento.</summary>
    public long Reserve()
    {
        if (!IsActive)
        {
            throw new DomainException("Numbering sequence is not active.");
        }

        if (NextNumber > MaxDocumentNumber)
        {
            throw new DomainException("Numbering sequence is exhausted for this stamp.");
        }

        var number = NextNumber;
        NextNumber++;
        Version++;
        return number;
    }

    public void Deactivate() => IsActive = false;

    private static string RequireDigits(string value, int length, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length != length || !value.Trim().All(char.IsAsciiDigit))
        {
            throw new DomainException($"{name} must contain exactly {length} digits.");
        }

        return value.Trim();
    }
}
