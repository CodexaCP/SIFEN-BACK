using System.Globalization;
using SifenInvoicing.Domain.Common;

namespace SifenInvoicing.Application.Cdc;

public static class CdcGenerator
{
    private static readonly int[] Weights = [2, 3, 4, 5, 6, 7];

    public static string GenerateCDC(GenerateCdcInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var baseCdc = BuildBaseCdc(input);
        var verificationDigit = CalculateVerificationDigit(baseCdc);

        return string.Concat(baseCdc, verificationDigit.ToString(CultureInfo.InvariantCulture));
    }

    public static bool ValidateCDC(string cdc)
    {
        if (string.IsNullOrWhiteSpace(cdc) || cdc.Length != 44 || !IsNumeric(cdc))
        {
            return false;
        }

        var baseCdc = cdc[..43];
        var expectedDigit = CalculateVerificationDigit(baseCdc);
        var providedDigit = cdc[43] - '0';

        return expectedDigit == providedDigit;
    }

    private static string BuildBaseCdc(GenerateCdcInput input)
    {
        var tipoDoc = ValidateExactNumeric(input.TipoDoc, 2, nameof(input.TipoDoc));
        var ruc = ValidateExactNumeric(input.Ruc, 8, nameof(input.Ruc));
        var dvRuc = ValidateExactNumeric(input.DvRuc, 1, nameof(input.DvRuc));
        var establecimiento = ValidateVariableNumeric(input.Establecimiento, 1, 3, nameof(input.Establecimiento)).PadLeft(3, '0');
        var puntoExpedicion = ValidateVariableNumeric(input.PuntoExpedicion, 1, 3, nameof(input.PuntoExpedicion)).PadLeft(3, '0');
        var numeroDe = ValidateVariableNumeric(input.NumeroDe, 1, 7, nameof(input.NumeroDe)).PadLeft(7, '0');
        var tipoContribuyente = ValidateExactNumeric(input.TipoContribuyente, 1, nameof(input.TipoContribuyente));
        var tipoEmision = ValidateExactNumeric(input.TipoEmision, 1, nameof(input.TipoEmision));
        var codigoSeguridad = ValidateVariableNumeric(input.CodigoSeguridad, 1, 9, nameof(input.CodigoSeguridad)).PadLeft(9, '0');
        var fechaEmision = ValidateExactNumeric(input.FechaEmision, 8, nameof(input.FechaEmision));

        if (!DateTime.TryParseExact(
                fechaEmision,
                "yyyyMMdd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out _))
        {
            throw new DomainException($"{nameof(input.FechaEmision)} must be a valid date in yyyyMMdd format.");
        }

        return string.Concat(
            tipoDoc,
            ruc,
            dvRuc,
            establecimiento,
            puntoExpedicion,
            numeroDe,
            tipoContribuyente,
            tipoEmision,
            codigoSeguridad,
            fechaEmision);
    }

    private static int CalculateVerificationDigit(string baseCdc)
    {
        if (baseCdc.Length != 43 || !IsNumeric(baseCdc))
        {
            throw new DomainException("CDC base must contain exactly 43 numeric digits.");
        }

        var sum = 0;

        for (int baseIndex = baseCdc.Length - 1, weightIndex = 0; baseIndex >= 0; baseIndex--, weightIndex++)
        {
            sum += (baseCdc[baseIndex] - '0') * Weights[weightIndex % Weights.Length];
        }

        var dv = 11 - (sum % 11);

        return dv switch
        {
            10 => 1,
            11 => 0,
            _ => dv
        };
    }

    private static string ValidateExactNumeric(string? value, int length, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException($"{fieldName} is required.");
        }

        var normalized = value.Trim();

        if (normalized.Length != length)
        {
            throw new DomainException($"{fieldName} must contain exactly {length} digits.");
        }

        if (!IsNumeric(normalized))
        {
            throw new DomainException($"{fieldName} must contain only numeric digits.");
        }

        return normalized;
    }

    private static string ValidateVariableNumeric(string? value, int minLength, int maxLength, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException($"{fieldName} is required.");
        }

        var normalized = value.Trim();

        if (normalized.Length < minLength || normalized.Length > maxLength)
        {
            throw new DomainException($"{fieldName} must contain between {minLength} and {maxLength} digits.");
        }

        if (!IsNumeric(normalized))
        {
            throw new DomainException($"{fieldName} must contain only numeric digits.");
        }

        return normalized;
    }

    private static bool IsNumeric(string value)
    {
        foreach (var ch in value)
        {
            if (!char.IsAsciiDigit(ch))
            {
                return false;
            }
        }

        return true;
    }
}
