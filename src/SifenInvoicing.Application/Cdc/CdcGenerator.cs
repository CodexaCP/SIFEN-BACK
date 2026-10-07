using System.Globalization;
using SifenInvoicing.Domain.Common;

namespace SifenInvoicing.Application.Cdc;

/// <summary>
/// CDC (44 digitos). Fuente: Manual Tecnico SIFEN v150 sec. 10.1 (p.56) y ejemplo
/// 01|44444401|7|001|001|0014528|2|20170125|1|587326098|8.
/// Orden: tipoDoc(2) RUC(8) DV-RUC(1) est(3) punto(3) numero(7) tipoContribuyente(1)
/// fecha AAAAMMDD(8) tipoEmision(1) codigoSeguridad(9) DV(1).
/// DV: modulo 11, pesos 2..11 ciclicos de derecha a izquierda (verificado contra el ejemplo del Manual).
/// PENDIENTE DE PRUEBA [TEST]: resto 0/1 -> DV 0 (decision provisoria, ver sifen-reglas-oficiales.md).
/// PENDIENTE [DOC]: relleno de RUC de menos de 8 digitos (dRucEm admite 3-8); por ahora se exigen 8.
/// </summary>
public static class CdcGenerator
{
    private const int MaxWeight = 11;

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
            fechaEmision,
            tipoEmision,
            codigoSeguridad);
    }

    private static int CalculateVerificationDigit(string baseCdc)
    {
        if (baseCdc.Length != 43 || !IsNumeric(baseCdc))
        {
            throw new DomainException("CDC base must contain exactly 43 numeric digits.");
        }

        return CalculateModulo11(baseCdc);
    }

    /// <summary>
    /// Modulo 11 del Manual v150 (pesos 2..11 ciclicos de derecha a izquierda), compartido por el DV del CDC y el DV del RUC
    /// (D102/D207 "Segun algoritmo modulo 11"). Fase 4.2: reutilizado sin cambios para validar el DV del RUC.
    /// </summary>
    public static int CalculateModulo11(string digits)
    {
        if (string.IsNullOrEmpty(digits) || !IsNumeric(digits))
        {
            throw new DomainException("Modulo 11 input must contain only numeric digits.");
        }

        var baseCdc = digits;
        var sum = 0;

        for (int i = baseCdc.Length - 1, weight = 2; i >= 0; i--, weight = weight == MaxWeight ? 2 : weight + 1)
        {
            sum += (baseCdc[i] - '0') * weight;
        }

        var remainder = sum % 11;

        // [TEST] resto 0 o 1 -> 0 (provisorio); en otro caso 11 - resto.
        return remainder > 1 ? 11 - remainder : 0;
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
