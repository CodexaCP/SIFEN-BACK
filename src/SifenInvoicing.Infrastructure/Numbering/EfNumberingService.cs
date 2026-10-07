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
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var candidates = await (
                from sequence in _dbContext.NumberingSequences
                join stamp in _dbContext.FiscalStamps on sequence.FiscalStampId equals stamp.Id
                where sequence.TenantId == tenantId &&
                      sequence.Environment == environment &&
                      sequence.DocumentTypeCode == documentTypeCode &&
                      sequence.IsActive &&
                      stamp.IsActive
                select new { sequence, stamp })
                .ToListAsync(cancellationToken);

            var valid = candidates.Where(item => item.stamp.IsValidOn(emissionDate)).ToList();

            if (valid.Count == 0)
            {
                throw new UserFacingException(
                    "FISCAL_NUMBERING_NOT_CONFIGURED",
                    "Configuration",
                    "No hay un timbrado y una numeracion vigentes configurados para emitir.",
                    "Configura el timbrado, el establecimiento y el punto de expedicion de la empresa.",
                    false,
                    422,
                    "No active fiscal stamp / numbering sequence for the tenant, environment, document type and date.");
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

            return new ReservedNumber(
                chosen.sequence.Id,
                chosen.stamp.StampingNumber,
                chosen.stamp.ValidFrom,
                chosen.stamp.ValidTo,
                chosen.sequence.DocumentTypeCode,
                chosen.sequence.EstablishmentCode,
                chosen.sequence.ExpeditionPointCode,
                string.IsNullOrEmpty(chosen.sequence.Series) ? null : chosen.sequence.Series,
                number);
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
}
