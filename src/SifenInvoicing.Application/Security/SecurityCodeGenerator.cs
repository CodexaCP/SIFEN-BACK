using System.Globalization;
using System.Security.Cryptography;
using SifenInvoicing.Domain.Common;

namespace SifenInvoicing.Application.Security;

/// <summary>
/// Codigo de seguridad (dCodSeg). Manual v150 sec. 10.3 (p.57), campo B004: 9 digitos, aleatorio,
/// distinto en cada DE, no secuencial (000000001-999999999), sin relacion con datos del DE
/// y distinto del numero de documento. Siempre generado por el servidor.
/// </summary>
public static class SecurityCodeGenerator
{
    private const int MaxAttempts = 16;

    public static string Generate(string documentNumber) =>
        Generate(documentNumber, static () => RandomNumberGenerator.GetInt32(1, 1_000_000_000));

    /// <summary>Sobrecarga con fuente aleatoria inyectable (pruebas).</summary>
    public static string Generate(string documentNumber, Func<int> nextValue)
    {
        ArgumentNullException.ThrowIfNull(nextValue);

        if (string.IsNullOrWhiteSpace(documentNumber) || !documentNumber.Trim().All(char.IsAsciiDigit))
        {
            throw new DomainException("El numero de documento debe ser numerico.");
        }

        var number = int.Parse(documentNumber.Trim(), CultureInfo.InvariantCulture);

        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var value = nextValue();

            if (value < 1 || value > 999_999_999 || value == number)
            {
                continue;
            }

            return value.ToString("D9", CultureInfo.InvariantCulture);
        }

        throw new DomainException("No se pudo generar un codigo de seguridad valido.");
    }
}
