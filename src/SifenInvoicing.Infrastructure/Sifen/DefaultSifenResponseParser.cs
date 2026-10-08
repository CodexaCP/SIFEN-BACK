using SifenInvoicing.Application.Sifen;
using SifenInvoicing.Domain.Documents;
using System.Globalization;
using System.Text;
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
            var fault = document.Descendants().FirstOrDefault(element => element.Name.LocalName == "Fault");
            if (fault is not null)
            {
                return BuildFault(fault);
            }

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

            var cdc = Child(protocol, "Id");
            var digest = Child(protocol, "dDigVal");
            var estado = Child(protocol, "dEstRes");
            var trackingId = Child(protocol, "dProtAut");
            var results = protocol.Elements()
                .Where(element => element.Name.LocalName == "gResProc")
                .Select(group => (Code: Child(group, "dCodRes"), Message: Child(group, "dMsgRes")))
                .ToList();
            var code = results.Select(item => item.Code).FirstOrDefault(value => value is not null);
            var message = results.Select(item => item.Message).FirstOrDefault(value => value is not null);
            var detail = string.Join(" | ", results.Select(item => $"{item.Code}: {item.Message}"));

            // El resultado se decide SOLO por dEstRes (valores del Manual Tecnico: Aprobado / Aprobado con observacion /
            // Rechazado). Ni dProtAut ni fragmentos de texto ni codigos de otros servicios (p. ej. 0300 es de lote) aprueban.
            return NormalizeEstado(estado) switch
            {
                "aprobado" => Build(
                    SifenResponseOutcome.Approved, SifenDocumentStatus.Accepted, true, true, cdc, trackingId,
                    code ?? "APPROVED", "Factura electronica aprobada por SIFEN.", detail, digest),
                "aprobado con observacion" => Build(
                    SifenResponseOutcome.Observed, SifenDocumentStatus.Accepted, true, true, cdc, trackingId,
                    code ?? "APPROVED_WITH_OBSERVATIONS", "Factura electronica aprobada por SIFEN con observaciones.", detail, digest),
                "rechazado" => Build(
                    SifenResponseOutcome.Rejected, SifenDocumentStatus.Rejected, false, true, cdc, trackingId,
                    code ?? "REJECTED", message ?? "Factura electronica rechazada por SIFEN.", detail, digest),
                _ => Build(
                    SifenResponseOutcome.Unknown, SifenDocumentStatus.Submitted, false, false, cdc, trackingId,
                    code ?? "UNKNOWN_RESPONSE",
                    "SIFEN devolvio una respuesta no reconocida. Requiere revision.",
                    string.IsNullOrWhiteSpace(estado) ? detail : $"dEstRes={estado} | {detail}", digest)
            };
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

    // SOAP 1.2: env:Fault/env:Code/env:Value y env:Reason/env:Text. Un Fault no es un resultado fiscal.
    private static ParsedSifenResponse BuildFault(XElement fault)
    {
        var faultCode = fault.Descendants().FirstOrDefault(element => element.Name.LocalName == "Value")?.Value?.Trim();
        var reason = fault.Descendants().FirstOrDefault(element => element.Name.LocalName == "Text")?.Value?.Trim();
        return Build(
            SifenResponseOutcome.TechnicalError,
            SifenDocumentStatus.Failed,
            false,
            true,
            null,
            null,
            "SOAP_FAULT",
            "SIFEN devolvio un SOAP Fault.",
            $"SOAP Fault: {faultCode} {reason}".Trim());
    }

    private static string? Child(XElement parent, string localName)
    {
        return Normalize(parent.Elements().FirstOrDefault(element => element.Name.LocalName == localName)?.Value);
    }

    private static string NormalizeEstado(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder();
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
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
        string? technicalMessage,
        string? digestValue = null)
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
            technicalMessage,
            digestValue);
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
