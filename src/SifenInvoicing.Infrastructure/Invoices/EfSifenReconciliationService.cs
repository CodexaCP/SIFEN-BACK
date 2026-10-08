using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SifenInvoicing.Application.Diagnostics;
using SifenInvoicing.Application.Sifen;
using SifenInvoicing.Application.Tenancy;
using SifenInvoicing.Domain.Common;
using SifenInvoicing.Domain.Documents;
using SifenInvoicing.Infrastructure.Persistence;

namespace SifenInvoicing.Infrastructure.Invoices;

/// <summary>
/// Reconcilia un envio indeterminado (timeout, corte, 5xx) consultando SIFEN por CDC. Nunca reenvia el DE: solo
/// registra lo que SIFEN informa y, si confirma que no existe, habilita el reintento del MISMO documento.
/// </summary>
public sealed class EfSifenReconciliationService : ISifenReconciliationService
{
    private readonly SifenDbContext _dbContext;
    private readonly ITenantContextAccessor _tenantContextAccessor;
    private readonly ISifenCdcQueryGateway _queryGateway;
    private readonly ISystemClock _clock;

    public EfSifenReconciliationService(
        SifenDbContext dbContext,
        ITenantContextAccessor tenantContextAccessor,
        ISifenCdcQueryGateway queryGateway,
        ISystemClock clock)
    {
        _dbContext = dbContext;
        _tenantContextAccessor = tenantContextAccessor;
        _queryGateway = queryGateway;
        _clock = clock;
    }

    public async Task<SifenReconciliationResult> ReconcileAsync(Guid invoiceId, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContextAccessor.Current.ResolvedTenantId
                       ?? throw new DomainException("Tenant context is required.");

        var document = await _dbContext.Documents.FirstOrDefaultAsync(item => item.Id == invoiceId, cancellationToken)
                       ?? throw new DomainException("Invoice was not found for the current tenant.");

        if (document.TransmissionState is not (SifenTransmissionState.Indeterminate or SifenTransmissionState.Sending))
        {
            throw new DomainException("Only invoices with an indeterminate transmission can be reconciled.");
        }

        var query = await _queryGateway.QueryByCdcAsync(
            new QueryCdcCommand(tenantId, document.Environment, document.Cdc),
            cancellationToken);
        var decision = SifenReconciliationDecider.Decide(query);

        string message;
        switch (decision)
        {
            case SifenReconciliationDecision.ConfirmApproved:
                document.MarkAccepted(
                    query.ProtocolNumber, query.ResponseCode, "Documento encontrado en SIFEN por consulta de CDC.",
                    query.RawResponse, _clock.UtcNow);
                // La consulta devuelve el DE "en estado aprobado" pero no distingue Aprobado de Aprobado con observacion.
                document.SetSifenStates(SifenTransmissionState.Delivered, SifenFiscalState.Approved);
                message = "SIFEN confirmo el documento por CDC. No se reenvia.";
                break;
            case SifenReconciliationDecision.AllowResend:
                document.MarkFailed(
                    "CDC_NOT_FOUND", "SIFEN informo que el CDC no existe o no esta aprobado.", query.RawResponse, _clock.UtcNow);
                document.SetSifenStates(SifenTransmissionState.NotDelivered, SifenFiscalState.NotFoundInSifen);
                message = "SIFEN no tiene el DE aprobado; el reintento del mismo documento esta permitido.";
                break;
            default:
                message = "La consulta por CDC no fue concluyente; el documento sigue indeterminado y no debe reenviarse.";
                break;
        }

        _dbContext.DocumentLogs.Add(SifenDocumentLog.Create(
            tenantId,
            document.Id,
            decision == SifenReconciliationDecision.RemainIndeterminate ? DocumentLogLevel.Warning : DocumentLogLevel.Information,
            "sifen.reconcile",
            message,
            JsonSerializer.Serialize(new
            {
                query.Outcome,
                query.ResponseCode,
                query.ResponseMessage,
                query.Detail,
                decision
            })));
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new SifenReconciliationResult(
            document.Id, document.Cdc, decision, document.TransmissionState.ToString(), document.FiscalState.ToString(), message);
    }
}
