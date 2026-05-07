using SifenInvoicing.Application.Sifen;
using SifenInvoicing.Domain.Documents;
using System.Xml.Linq;

namespace SifenInvoicing.Infrastructure.Sifen;

public sealed class DefaultSifenResponseParser : ISifenResponseParser
{
    public ParsedSifenResponse ParseResponse(string? rawResponse)
    {
        if (string.IsNullOrWhiteSpace(rawResponse))
        {
            return Build(
                SifenResponseOutcome.EmptyResponse,
                SifenDocumentStatus.Failed,
                false,
                true,
                null,
                null,
                "NO_RESPONSE",
                "No se recibio respuesta de SIFEN.",
                "SIFEN response was empty.");
        }

        if (rawResponse.StartsWith("PENDING_TRANSPORT|", StringComparison.Ordinal))
        {
            return Build(
                SifenResponseOutcome.TechnicalError,
                SifenDocumentStatus.Failed,
                false,
                true,
                null,
                null,
                "PENDING_TRANSPORT",
                "El envio a SIFEN no fue completado.",
                "Submission queued for transport implementation.");
        }

        if (rawResponse.StartsWith("DIAGNOSTIC_TRANSPORT|", StringComparison.Ordinal))
        {
            var code = rawResponse["DIAGNOSTIC_TRANSPORT|".Length..].Trim();
            return Build(
                SifenResponseOutcome.TechnicalError,
                SifenDocumentStatus.Failed,
                false,
                true,
                null,
                null,
                code,
                "El envio a SIFEN quedo en modo diagnostico.",
                "SIFEN submission stayed in diagnostic mode.");
        }

        if (rawResponse.StartsWith("SOAP_TIMEOUT|", StringComparison.Ordinal))
        {
            return Build(
                SifenResponseOutcome.TechnicalError,
                SifenDocumentStatus.Failed,
                false,
                true,
                null,
                null,
                "SOAP_TIMEOUT",
                "SIFEN no respondio dentro del tiempo esperado.",
                "SIFEN SOAP request timed out.");
        }

        if (rawResponse.StartsWith("SOAP_TRANSPORT_ERROR|", StringComparison.Ordinal))
        {
            var detail = rawResponse["SOAP_TRANSPORT_ERROR|".Length..].Trim();
            return Build(
                SifenResponseOutcome.TechnicalError,
                SifenDocumentStatus.Failed,
                false,
                true,
                null,
                null,
                "SOAP_TRANSPORT_ERROR",
                "Ocurrio un error tecnico al enviar a SIFEN.",
                $"SIFEN SOAP transport failed: {detail}.");
        }

        if (rawResponse.StartsWith("HTTP_ERROR|", StringComparison.Ordinal))
        {
            var parts = rawResponse.Split('|', 3, StringSplitOptions.None);
            var statusCode = parts.Length > 1 ? parts[1] : "HTTP_ERROR";
            return Build(
                SifenResponseOutcome.TechnicalError,
                SifenDocumentStatus.Failed,
                false,
                true,
                null,
                null,
                $"HTTP_{statusCode}",
                "SIFEN devolvio un error tecnico HTTP.",
                "SIFEN SOAP endpoint returned an HTTP error.");
        }

        try
        {
            var document = XDocument.Parse(rawResponse);
            var protocol = document.Descendants().FirstOrDefault(element => element.Name.LocalName == "rProtDe");
            if (protocol is null)
            {
                return Build(
                    SifenResponseOutcome.InvalidXml,
                    SifenDocumentStatus.Failed,
                    false,
                    true,
                    null,
                    null,
                    "INVALID_RESPONSE",
                    "La respuesta de SIFEN no tiene el formato esperado.",
                    "SIFEN response protocol node rProtDe was not found.");
            }

            var cdc = protocol.Elements().FirstOrDefault(element => element.Name.LocalName == "Id")?.Value?.Trim();
            var status = protocol.Elements().FirstOrDefault(element => element.Name.LocalName == "dEstRes")?.Value?.Trim();
            var trackingId = protocol.Elements().FirstOrDefault(element => element.Name.LocalName == "dProtAut")?.Value?.Trim();
            var firstResultGroup = protocol.Elements().FirstOrDefault(element => element.Name.LocalName == "gResProc");
            var code = firstResultGroup?.Elements().FirstOrDefault(element => element.Name.LocalName == "dCodRes")?.Value?.Trim() ?? status;
            var message = firstResultGroup?.Elements().FirstOrDefault(element => element.Name.LocalName == "dMsgRes")?.Value?.Trim() ?? status;
            return MapProtocolResponse(cdc, trackingId, status, code, message);
        }
        catch
        {
            return Build(
                SifenResponseOutcome.InvalidXml,
                SifenDocumentStatus.Failed,
                false,
                true,
                null,
                null,
                "UNPARSEABLE_RESPONSE",
                "La respuesta de SIFEN no pudo interpretarse.",
                "SIFEN response could not be parsed as XML.");
        }
    }

    private static ParsedSifenResponse MapProtocolResponse(
        string? cdc,
        string? trackingId,
        string? status,
        string? code,
        string? message)
    {
        var normalizedStatus = Normalize(status);
        var normalizedCode = Normalize(code);
        var normalizedMessage = Normalize(message);
        var descriptor = string.Join(" | ", new[] { normalizedStatus, normalizedCode, normalizedMessage }
            .Where(value => !string.IsNullOrWhiteSpace(value)));
        var lowerDescriptor = descriptor.ToLowerInvariant();

        if (!string.IsNullOrWhiteSpace(trackingId) || ContainsAny(lowerDescriptor, "aprob", "0300"))
        {
            return Build(
                SifenResponseOutcome.Approved,
                SifenDocumentStatus.Accepted,
                true,
                true,
                cdc,
                trackingId,
                normalizedCode ?? "APPROVED",
                "Factura electronica aprobada por SIFEN.",
                descriptor);
        }

        if (ContainsAny(lowerDescriptor, "rechaz", "deneg", "rechazo"))
        {
            return Build(
                SifenResponseOutcome.Rejected,
                SifenDocumentStatus.Rejected,
                false,
                true,
                cdc,
                trackingId,
                normalizedCode ?? "REJECTED",
                "Factura electronica rechazada por SIFEN.",
                descriptor);
        }

        if (ContainsAny(lowerDescriptor, "observ"))
        {
            return Build(
                SifenResponseOutcome.Observed,
                SifenDocumentStatus.Submitted,
                false,
                false,
                cdc,
                trackingId,
                normalizedCode ?? "OBSERVED",
                "Factura electronica observada. Requiere revision operativa.",
                descriptor);
        }

        if (ContainsAny(lowerDescriptor, "error", "fall", "exception"))
        {
            return Build(
                SifenResponseOutcome.TechnicalError,
                SifenDocumentStatus.Failed,
                false,
                true,
                cdc,
                trackingId,
                normalizedCode ?? "TECHNICAL_ERROR",
                "SIFEN devolvio un error tecnico.",
                descriptor);
        }

        return Build(
            SifenResponseOutcome.Unknown,
            SifenDocumentStatus.Submitted,
            false,
            false,
            cdc,
            trackingId,
            normalizedCode ?? "UNKNOWN_RESPONSE",
            "SIFEN devolvio una respuesta no reconocida. Requiere revision.",
            descriptor);
    }

    private static ParsedSifenResponse Build(
        SifenResponseOutcome outcome,
        SifenDocumentStatus statusHint,
        bool isSuccessful,
        bool isFinal,
        string? cdc,
        string? trackingId,
        string? statusCode,
        string? statusMessage,
        string? technicalMessage)
    {
        return new ParsedSifenResponse(
            outcome,
            statusHint,
            isSuccessful,
            isFinal,
            cdc,
            trackingId,
            statusCode,
            statusMessage,
            technicalMessage);
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static bool ContainsAny(string value, params string[] fragments)
    {
        return fragments.Any(fragment => value.Contains(fragment, StringComparison.Ordinal));
    }
}
