using System.Xml.Linq;
using SifenInvoicing.Application.Sifen;

namespace SifenInvoicing.Infrastructure.Sifen;

/// <summary>
/// Interpreta la respuesta de la consulta por CDC. Codigos (Manual Tecnico 9.4.2 y Guia de Mejores Practicas):
/// 0420 = el DE no existe o no esta aprobado; 0422 = DE encontrado (se devuelve el XML en estado aprobado).
/// 0421 es contradictorio en el Manual ("RUC del certificado sin permiso" vs "CDC encontrado"), por eso se trata como
/// no reconocido y nunca decide un estado. La raiz de la respuesta tambien difiere entre fuentes (rResEnviConsDe /
/// rEnviConsDeResponse), asi que se busca por los campos y no por el nombre de la raiz.
/// </summary>
public static class SifenCdcQueryResponseParser
{
    public static SifenCdcQueryResult Parse(string? rawResponse)
    {
        if (string.IsNullOrWhiteSpace(rawResponse))
        {
            return new SifenCdcQueryResult(SifenCdcQueryOutcome.Unavailable, null, null, null, rawResponse, "Empty response.");
        }

        XDocument document;
        try
        {
            document = XDocument.Parse(rawResponse);
        }
        catch (System.Xml.XmlException)
        {
            return new SifenCdcQueryResult(SifenCdcQueryOutcome.Unavailable, null, null, null, rawResponse, "Response is not valid XML.");
        }

        if (document.Descendants().Any(element => element.Name.LocalName == "Fault"))
        {
            return new SifenCdcQueryResult(SifenCdcQueryOutcome.Unavailable, "SOAP_FAULT", null, null, rawResponse, "SOAP Fault.");
        }

        var code = Value(document, "dCodRes");
        var message = Value(document, "dMsgRes");
        var protocol = Value(document, "dProtAut");
        var outcome = code switch
        {
            "0422" => SifenCdcQueryOutcome.Found,
            "0420" => SifenCdcQueryOutcome.NotFound,
            null => SifenCdcQueryOutcome.Unavailable,
            _ => SifenCdcQueryOutcome.Unrecognized
        };

        return new SifenCdcQueryResult(outcome, code, message, protocol, rawResponse, null);
    }

    private static string? Value(XDocument document, string localName)
    {
        var value = document.Descendants().FirstOrDefault(element => element.Name.LocalName == localName)?.Value?.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }
}
