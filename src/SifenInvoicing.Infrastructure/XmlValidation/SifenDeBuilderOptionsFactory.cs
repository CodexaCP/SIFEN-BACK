using Microsoft.Extensions.Configuration;
using SifenInvoicing.Domain.Common;

using SifenInvoicing.Application.XmlDe;

namespace SifenInvoicing.Infrastructure.XmlValidation;

/// <summary>
/// Lee Sifen:De:* . Las contradicciones abiertas (dSisFact, leyenda de Test, omision de ceros) se resuelven por
/// configuracion explicita, nunca por el codigo; el valor ausente es el default provisional de <see cref="SifenDeBuilderOptions"/>.
/// </summary>
public static class SifenDeBuilderOptionsFactory
{
    public static SifenDeBuilderOptions Create(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var section = configuration.GetSection("Sifen:De");
        return new SifenDeBuilderOptions(
            Parse(section["DSisFact"], SifenDeBuilderOptions.Default.DSisFact, "Sifen:De:DSisFact"),
            Parse(section["ZeroPolicy"], SifenDeBuilderOptions.Default.ZeroPolicy, "Sifen:De:ZeroPolicy"));
    }

    private static T Parse<T>(string? value, T fallback, string key) where T : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return Enum.TryParse<T>(value, true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw new DomainException($"{key} tiene un valor no valido: '{value}'.");
    }
}
