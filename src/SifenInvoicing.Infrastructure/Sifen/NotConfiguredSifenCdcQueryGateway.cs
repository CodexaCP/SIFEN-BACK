using SifenInvoicing.Application.Sifen;

namespace SifenInvoicing.Infrastructure.Sifen;

/// <summary>
/// PENDIENTE — WSDL/endpoint oficial de consulta (consulta.wsdl): operacion, SOAPAction y soap:address no estan
/// confirmados. Hasta tenerlos, la consulta por CDC no se ejecuta y el resultado es siempre Unavailable (el documento
/// indeterminado permanece sin reenviar). Sustituir por una implementacion que use SifenSoapEnvelopeBuilder.BuildCdcQuery,
/// ISifenSoapTransport y SifenCdcQueryResponseParser cuando el WSDL real este auditado.
/// </summary>
public sealed class NotConfiguredSifenCdcQueryGateway : ISifenCdcQueryGateway
{
    public Task<SifenCdcQueryResult> QueryByCdcAsync(QueryCdcCommand command, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new SifenCdcQueryResult(
            SifenCdcQueryOutcome.Unavailable,
            "PENDING_WSDL",
            null,
            null,
            null,
            "PENDIENTE - WSDL/endpoint oficial de consulta por CDC no confirmado. No se realizo ninguna llamada."));
    }
}
