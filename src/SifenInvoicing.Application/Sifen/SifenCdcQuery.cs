using SifenInvoicing.Domain.Tenants;

namespace SifenInvoicing.Application.Sifen;

public sealed record QueryCdcCommand(Guid TenantId, SifenEnvironmentType Environment, string Cdc);

public enum SifenCdcQueryOutcome
{
    /// <summary>No se pudo consultar (servicio no configurado, timeout, error de transporte). No permite concluir nada.</summary>
    Unavailable = 0,

    /// <summary>dCodRes 0422: SIFEN devolvio el DE (en estado aprobado segun la Guia de Mejores Practicas).</summary>
    Found = 1,

    /// <summary>dCodRes 0420: el DE no existe o no esta aprobado en SIFEN.</summary>
    NotFound = 2,

    /// <summary>Respuesta recibida pero con un codigo no confirmado documentalmente (p. ej. 0421, contradictorio en el Manual).</summary>
    Unrecognized = 3
}

public sealed record SifenCdcQueryResult(
    SifenCdcQueryOutcome Outcome,
    string? ResponseCode,
    string? ResponseMessage,
    string? ProtocolNumber,
    string? RawResponse,
    string? Detail);

public interface ISifenCdcQueryGateway
{
    Task<SifenCdcQueryResult> QueryByCdcAsync(QueryCdcCommand command, CancellationToken cancellationToken = default);
}

public enum SifenReconciliationDecision
{
    /// <summary>Sin informacion concluyente: no reenviar y volver a consultar.</summary>
    RemainIndeterminate = 0,

    /// <summary>La consulta confirmo que SIFEN tiene el DE: registrar como aprobado, no reenviar.</summary>
    ConfirmApproved = 1,

    /// <summary>La consulta confirmo que SIFEN no tiene el DE aprobado: el reenvio del MISMO DE pasa a estar permitido.</summary>
    AllowResend = 2
}

/// <summary>Decision pura (sin I/O) de que hacer con un envio indeterminado a partir del resultado de la consulta por CDC.</summary>
public static class SifenReconciliationDecider
{
    public static SifenReconciliationDecision Decide(SifenCdcQueryResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.Outcome switch
        {
            SifenCdcQueryOutcome.Found => SifenReconciliationDecision.ConfirmApproved,
            SifenCdcQueryOutcome.NotFound => SifenReconciliationDecision.AllowResend,
            _ => SifenReconciliationDecision.RemainIndeterminate
        };
    }
}

public sealed record SifenReconciliationResult(
    Guid InvoiceId,
    string Cdc,
    SifenReconciliationDecision Decision,
    string TransmissionState,
    string FiscalState,
    string Message);

public interface ISifenReconciliationService
{
    /// <summary>Consulta el CDC de un documento con transmision indeterminada y aplica la decision. Nunca reenvia el DE.</summary>
    Task<SifenReconciliationResult> ReconcileAsync(Guid invoiceId, CancellationToken cancellationToken = default);
}
