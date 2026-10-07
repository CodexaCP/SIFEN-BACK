using Microsoft.EntityFrameworkCore;
using SifenInvoicing.Application.Numbering;
using SifenInvoicing.Domain.Common;
using SifenInvoicing.Domain.Tenants;
using SifenInvoicing.Infrastructure.Persistence;

namespace SifenInvoicing.Infrastructure.Numbering;

/// <summary>
/// Reserva atomica con concurrencia optimista (NumberingSequence.Version). La reserva se guarda de inmediato
/// (dentro de la transaccion abierta por el llamador) para que el conflicto se detecte en este punto;
/// ante conflicto se recarga y se reintenta. Dos reservas nunca obtienen el mismo numero.
/// Si la transaccion del llamador se revierte, el numero vuelve a estar disponible (sin huecos).
/// En SQL Server la reserva es un unico UPDATE ... OUTPUT atomico (sin conflictos ni reintentos); los demas
/// proveedores (SQLite, InMemory) conservan la ruta optimista.
/// </summary>
public sealed class EfNumberingService : INumberingService
{
    private const int MaxAttempts = 25;

    private readonly SifenDbContext _dbContext;

    public EfNumberingService(SifenDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ReservedNumber> ReserveAsync(
        Guid tenantId,
        SifenEnvironmentType environment,
        string documentTypeCode,
        DateOnly emissionDate,
        CancellationToken cancellationToken = default)
    {
        var atomic = _dbContext.Database.IsSqlServer();

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var query =
                from sequence in _dbContext.NumberingSequences
                join stamp in _dbContext.FiscalStamps on sequence.FiscalStampId equals stamp.Id
                where sequence.TenantId == tenantId &&
                      sequence.Environment == environment &&
                      sequence.DocumentTypeCode == documentTypeCode &&
                      sequence.IsActive &&
                      stamp.IsActive
                select new { sequence, stamp };

            var candidates = await (atomic ? query.AsNoTracking() : query).ToListAsync(cancellationToken);

            var valid = candidates.Where(item => item.stamp.IsValidOn(emissionDate)).ToList();

            if (valid.Count == 0)
            {
                throw NotConfigured();
            }

            if (valid.Count > 1)
            {
                // PENDIENTE (decision de producto): seleccion de punto de expedicion cuando hay varios activos.
                throw new UserFacingException(
                    "FISCAL_NUMBERING_AMBIGUOUS",
                    "Configuration",
                    "Hay mas de un punto de expedicion activo y todavia no se puede elegir cual usar.",
                    "Deja un unico punto de expedicion activo para el tipo de documento.",
                    false,
                    422,
                    "Multiple active numbering sequences; emission point selection is not implemented.");
            }

            var chosen = valid[0];

            long number;
            if (atomic)
            {
                number = await ReserveAtomicAsync(tenantId, chosen.sequence.Id, cancellationToken);
                return ToReserved(chosen.sequence, chosen.stamp, number);
            }

            try
            {
                number = chosen.sequence.Reserve();
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                // Otro proceso reservo primero: se descarta el estado local y se reintenta con el valor vigente.
                foreach (var entry in ex.Entries)
                {
                    entry.State = EntityState.Detached;
                }

                continue;
            }

            return ToReserved(chosen.sequence, chosen.stamp, number);
        }

        throw new UserFacingException(
            "FISCAL_NUMBERING_BUSY",
            "Configuration",
            "No pudimos reservar el numero de documento. Intenta nuevamente.",
            "Reintenta la emision con la misma Idempotency-Key.",
            true,
            503,
            "Numbering reservation exceeded the maximum number of optimistic concurrency attempts.");
    }

    /// <summary>
    /// SQL Server: un unico UPDATE ... OUTPUT. Espera el bloqueo de fila del dueño actual y evalua sobre el valor
    /// confirmado, por lo que no hay lectura previa, conflicto ni reintento. El SQL crudo no aplica los filtros
    /// globales, por eso TenantId va explicito. Corre en la transaccion actual del contexto, si existe.
    /// </summary>
    private async Task<long> ReserveAtomicAsync(Guid tenantId, Guid sequenceId, CancellationToken cancellationToken)
    {
        var max = NumberingSequence.MaxDocumentNumber;
        var reserved = await _dbContext.Database
            .SqlQuery<long>($@"UPDATE NumberingSequences
SET NextNumber = NextNumber + 1, Version = Version + 1
OUTPUT deleted.NextNumber AS [Value]
WHERE Id = {sequenceId} AND TenantId = {tenantId} AND IsActive = 1 AND NextNumber <= {max}")
            .ToListAsync(cancellationToken);

        if (reserved.Count == 1)
        {
            return reserved[0];
        }

        // 0 filas: agotada, desactivada o inexistente. Se distingue igual que la ruta optimista.
        var current = await _dbContext.NumberingSequences
            .AsNoTracking()
            .Where(item => item.Id == sequenceId && item.TenantId == tenantId)
            .Select(item => new { item.IsActive, item.NextNumber })
            .SingleOrDefaultAsync(cancellationToken);

        if (current is { IsActive: true } && current.NextNumber > max)
        {
            throw new DomainException("Numbering sequence is exhausted for this stamp.");
        }

        throw NotConfigured();
    }

    private static ReservedNumber ToReserved(NumberingSequence sequence, FiscalStamp stamp, long number) =>
        new(
            sequence.Id,
            stamp.StampingNumber,
            stamp.ValidFrom,
            stamp.ValidTo,
            sequence.DocumentTypeCode,
            sequence.EstablishmentCode,
            sequence.ExpeditionPointCode,
            string.IsNullOrEmpty(sequence.Series) ? null : sequence.Series,
            number);

    private static UserFacingException NotConfigured() =>
        new(
            "FISCAL_NUMBERING_NOT_CONFIGURED",
            "Configuration",
            "No hay un timbrado y una numeracion vigentes configurados para emitir.",
            "Configura el timbrado, el establecimiento y el punto de expedicion de la empresa.",
            false,
            422,
            "No active fiscal stamp / numbering sequence for the tenant, environment, document type and date.");
}
