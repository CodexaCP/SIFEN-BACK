using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SifenInvoicing.Domain.Common;

namespace SifenInvoicing.Application.Qr;

public enum SifenQrEnvironment
{
    Test = 1,
    Production = 2
}

/// <summary>
/// Datos del QR (Manual v150 sec. 13.8.2). <paramref name="ReceiverDocument"/>: dRucRec o dNumIDRec ("0" si no hay).
/// <paramref name="EmissionDateTimeText"/>: dFeEmiDE tal como aparece en el XML (AAAA-MM-DDThh:mm:ss).
/// <paramref name="DigestValueBase64"/>: texto base64 de DigestValue de la firma.
/// <paramref name="CscSecret"/> nunca se incluye en la URL; solo participa del hash.
/// </summary>
public sealed record SifenQrInput(
    SifenQrEnvironment Environment,
    string Cdc,
    string EmissionDateTimeText,
    string ReceiverDocument,
    decimal TotalGeneral,
    decimal TotalIva,
    int ItemCount,
    string DigestValueBase64,
    string IdCsc,
    string CscSecret,
    string Version = "150",
    string ReceiverParameterName = "dRucRec");

/// <summary>URL con "&amp;" literal. Al insertarla en dCarQR el serializador XML la escapa (Manual 13.8.4.5).</summary>
public sealed record SifenQrResult(string Url, string Hash);

/// <summary>Abstraccion reemplazable. La generacion de la imagen QR queda fuera (decision de libreria pendiente).</summary>
public interface ISifenQrBuilder
{
    SifenQrResult Build(SifenQrInput input);
}

/// <summary>
/// Fuente: Manual v150 sec. 13.8.3-13.8.4 (hash) y NT-10 (URLs sin 'www'; dTotIVA = 0 si no hay IVA).
/// PENDIENTE DE PRUEBA [TEST]: aceptacion del QR por la consulta publica de Test.
/// </summary>
public sealed class SifenQrBuilder : ISifenQrBuilder
{
    public const string TestBaseUrl = "https://ekuatia.set.gov.py/consultas-test/qr?";
    public const string ProductionBaseUrl = "https://ekuatia.set.gov.py/consultas/qr?";

    public SifenQrResult Build(SifenQrInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        Require(input.Cdc, nameof(input.Cdc));
        Require(input.EmissionDateTimeText, nameof(input.EmissionDateTimeText));
        Require(input.DigestValueBase64, nameof(input.DigestValueBase64));
        Require(input.IdCsc, nameof(input.IdCsc));
        Require(input.CscSecret, nameof(input.CscSecret));

        if (input.ItemCount < 1)
        {
            throw new DomainException("cItems debe ser al menos 1.");
        }

        if (input.ReceiverParameterName is not ("dRucRec" or "dNumIDRec"))
        {
            throw new DomainException("El parametro del receptor del QR debe ser dRucRec o dNumIDRec (Manual v150 13.8.2).");
        }

        var receiver = string.IsNullOrWhiteSpace(input.ReceiverDocument) ? "0" : input.ReceiverDocument.Trim();

        var data = string.Concat(
            "nVersion=", input.Version,
            "&Id=", input.Cdc,
            "&dFeEmiDE=", ToHex(input.EmissionDateTimeText),
            "&", input.ReceiverParameterName, "=", receiver,
            "&dTotGralOpe=", Number(input.TotalGeneral),
            "&dTotIVA=", Number(input.TotalIva),
            "&cItems=", input.ItemCount.ToString(CultureInfo.InvariantCulture),
            "&DigestValue=", ToHex(input.DigestValueBase64),
            "&IdCSC=", input.IdCsc);

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(data + input.CscSecret))).ToLowerInvariant();
        var baseUrl = input.Environment == SifenQrEnvironment.Production ? ProductionBaseUrl : TestBaseUrl;

        return new SifenQrResult(string.Concat(baseUrl, data, "&cHashQR=", hash), hash);
    }

    private static string ToHex(string text) =>
        Convert.ToHexString(Encoding.ASCII.GetBytes(text)).ToLowerInvariant();

    private static string Number(decimal value) =>
        value.ToString("0.########", CultureInfo.InvariantCulture);

    private static void Require(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException($"{name} es obligatorio para el QR.");
        }
    }
}
