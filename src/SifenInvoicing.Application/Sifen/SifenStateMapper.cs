using SifenInvoicing.Domain.Documents;

namespace SifenInvoicing.Application.Sifen;

/// <summary>
/// Traduce el resultado de un envio (transporte + parser) a los estados internos de transmision y fiscal.
/// Politica de la aplicacion (no oficial SIFEN): ante duda se declara Indeterminate y se prohibe reenviar sin consultar por CDC.
/// </summary>
public static class SifenStateMapper
{
    public static (SifenTransmissionState Transmission, SifenFiscalState Fiscal) Map(
        SifenSubmissionResult submission,
        ParsedSifenResponse parsed)
    {
        ArgumentNullException.ThrowIfNull(submission);
        ArgumentNullException.ThrowIfNull(parsed);

        if (submission.IsDiagnostic)
        {
            return (SifenTransmissionState.NotSent, SifenFiscalState.None);
        }

        switch (parsed.Outcome)
        {
            case SifenResponseOutcome.Approved:
                return (SifenTransmissionState.Delivered, SifenFiscalState.Approved);
            case SifenResponseOutcome.Observed:
                return (SifenTransmissionState.Delivered, SifenFiscalState.ApprovedWithObservations);
            case SifenResponseOutcome.Rejected:
                return (SifenTransmissionState.Delivered, SifenFiscalState.Rejected);
        }

        return (ClassifyFailedTransmission(submission, parsed), SifenFiscalState.None);
    }

    private static SifenTransmissionState ClassifyFailedTransmission(SifenSubmissionResult submission, ParsedSifenResponse parsed)
    {
        switch (submission.TransportCode)
        {
            case "SOAP_TIMEOUT":
            case "SOAP_TRANSPORT_ERROR":
            case "HTTP_OK": // respondio, pero no se pudo interpretar el resultado de procesamiento
                return SifenTransmissionState.Indeterminate;
            case "HTTP_ERROR":
                return submission.HttpStatusCode is >= 500
                    ? SifenTransmissionState.Indeterminate
                    : SifenTransmissionState.NotDelivered;
        }

        // Sin codigo de transporte (gateways alternativos): decidir por lo que dijo el parser.
        return parsed.Outcome is SifenResponseOutcome.Unknown or SifenResponseOutcome.InvalidXml or SifenResponseOutcome.EmptyResponse
            || parsed.StatusCode is "SOAP_FAULT"
            ? SifenTransmissionState.Indeterminate
            : SifenTransmissionState.NotDelivered;
    }
}
