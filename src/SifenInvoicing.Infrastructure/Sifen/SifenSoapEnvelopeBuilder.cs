using System.Text;
using System.Text.RegularExpressions;

namespace SifenInvoicing.Infrastructure.Sifen;

/// <summary>
/// Arma los envelopes SOAP 1.2 (document/literal) hacia SIFEN. Solo usa nombres confirmados por la documentacion oficial:
/// envelope <c>http://www.w3.org/2003/05/soap-envelope</c> con <c>Header</c> vacio (Guia de Mejores Practicas), namespace de
/// datos <c>http://ekuatia.set.gov.py/sifen/xsd</c>, <c>rEnviDe{dId,xDE}</c> (Manual Tecnico) y
/// <c>rEnviConsDeRequest{dId,dCDC}</c> (Guia de Mejores Practicas). El namespace de la operacion, la SOAPAction y el
/// endpoint dependen del WSDL real y NO se definen aqui.
/// </summary>
public static class SifenSoapEnvelopeBuilder
{
    public const string SoapEnvelopeNamespace = "http://www.w3.org/2003/05/soap-envelope";
    public const string SifenNamespace = "http://ekuatia.set.gov.py/sifen/xsd";

    private static readonly Regex DIdPattern = new("^[0-9]{1,15}$", RegexOptions.Compiled);
    private static readonly Regex CdcPattern = new("^[0-9]{44}$", RegexOptions.Compiled);
    private static readonly Regex XmlDeclaration = new(@"^\s*<\?xml[^>]*\?>\s*", RegexOptions.Compiled);

    /// <summary>
    /// Envelope de recepcion sincrona. El DE firmado se inserta TAL CUAL (sin reparsear ni reserializar) para no alterar
    /// la firma ni el QR ya validados; solo se descarta la declaracion XML inicial.
    /// </summary>
    public static string BuildReception(string dId, string signedDeXml)
    {
        EnsureDId(dId);
        if (string.IsNullOrWhiteSpace(signedDeXml))
        {
            throw new ArgumentException("El DE firmado es obligatorio.", nameof(signedDeXml));
        }

        var rde = XmlDeclaration.Replace(signedDeXml.TrimStart('﻿'), string.Empty).TrimEnd();
        if (!rde.StartsWith("<rDE", StringComparison.Ordinal))
        {
            throw new ArgumentException("El XML firmado debe tener rDE como raiz.", nameof(signedDeXml));
        }

        var builder = new StringBuilder();
        builder.Append("<soap:Envelope xmlns:soap=\"").Append(SoapEnvelopeNamespace)
            .Append("\" xmlns:xsd=\"").Append(SifenNamespace).Append("\">")
            .Append("<soap:Header/>")
            .Append("<soap:Body>")
            .Append("<xsd:rEnviDe>")
            .Append("<xsd:dId>").Append(dId).Append("</xsd:dId>")
            .Append("<xsd:xDE>").Append(rde).Append("</xsd:xDE>")
            .Append("</xsd:rEnviDe>")
            .Append("</soap:Body>")
            .Append("</soap:Envelope>");
        return builder.ToString();
    }

    /// <summary>Envelope de consulta por CDC (<c>rEnviConsDeRequest</c>), estructura de la Guia de Mejores Practicas.</summary>
    public static string BuildCdcQuery(string dId, string cdc)
    {
        EnsureDId(dId);
        if (cdc is null || !CdcPattern.IsMatch(cdc))
        {
            throw new ArgumentException("El CDC debe tener 44 digitos.", nameof(cdc));
        }

        return "<soap:Envelope xmlns:soap=\"" + SoapEnvelopeNamespace + "\" xmlns:xsd=\"" + SifenNamespace + "\">" +
               "<soap:Header/><soap:Body><xsd:rEnviConsDeRequest>" +
               "<xsd:dId>" + dId + "</xsd:dId><xsd:dCDC>" + cdc + "</xsd:dCDC>" +
               "</xsd:rEnviConsDeRequest></soap:Body></soap:Envelope>";
    }

    /// <summary>Content-Type de SOAP 1.2 (RFC 3902). El parametro action solo se agrega si hay una SOAPAction confirmada.</summary>
    public static string BuildContentType(string? soapAction)
    {
        var contentType = "application/soap+xml; charset=utf-8";
        return string.IsNullOrWhiteSpace(soapAction)
            ? contentType
            : contentType + "; action=\"" + soapAction.Trim().Replace("\"", string.Empty) + "\"";
    }

    private static void EnsureDId(string dId)
    {
        if (dId is null || !DIdPattern.IsMatch(dId))
        {
            throw new ArgumentException("dId debe tener entre 1 y 15 digitos.", nameof(dId));
        }
    }
}
