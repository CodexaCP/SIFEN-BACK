using SifenInvoicing.Domain.Tenants;

namespace SifenInvoicing.Application.Numbering;

public sealed record ReservedNumber(
    Guid SequenceId,
    string StampingNumber,
    DateOnly StampValidFrom,
    DateOnly? StampValidTo,
    string DocumentTypeCode,
    string EstablishmentCode,
    string ExpeditionPointCode,
    string? Series,
    long Number)
{
    /// <summary>dNumDoc: 7 digitos con ceros a la izquierda.</summary>
    public string FormattedNumber => Number.ToString("D7", System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// Reserva atomica de numeros por timbrado/tipo/establecimiento/punto. El numero nunca lo decide el cliente.
/// Debe invocarse dentro de la transaccion que despues persiste el documento: si esa transaccion se revierte,
/// el numero no queda consumido.
/// </summary>
public interface INumberingService
{
    Task<ReservedNumber> ReserveAsync(
        Guid tenantId,
        SifenEnvironmentType environment,
        string documentTypeCode,
        DateOnly emissionDate,
        CancellationToken cancellationToken = default);
}
