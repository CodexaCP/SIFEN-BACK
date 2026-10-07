using SifenInvoicing.Application.Invoices;
using SifenInvoicing.Domain.Common;

namespace SifenInvoicing.Application.Fiscal;

/// <summary>Linea comercial de entrada. Precio unitario con impuestos incluidos (Manual v150 E721).</summary>
public sealed record FiscalLineInput(decimal Quantity, decimal UnitPrice, InvoiceVatType VatType);

/// <summary>Montos fiscales de un item (Manual v150 E727, EA008, E731-E737).</summary>
public sealed record FiscalLine(
    int Number,
    decimal Quantity,
    decimal UnitPrice,
    InvoiceVatType VatType,
    decimal TotalBruto,        // E727 dTotBruOpeItem = E721 * E711
    decimal TotalOperacion,    // EA008 dTotOpeItem (sin descuentos/anticipos)
    int AfectacionIva,         // E731 iAfecIVA: 1 gravado, 3 exento
    decimal ProporcionIva,     // E733 dPropIVA
    decimal TasaIva,           // E734 dTasaIVA
    decimal BaseGravadaIva,    // E735 dBasGravIVA
    decimal LiquidacionIva,    // E736 dLiqIVAItem
    decimal BaseExenta);       // E737 dBasExe (NT-13)

/// <summary>Subtotales y totales (Manual v150 F002-F037).</summary>
public sealed record FiscalTotals(
    decimal SubExento,         // F002 dSubExe
    decimal Sub5,              // F004 dSub5
    decimal Sub10,             // F005 dSub10
    decimal TotalOperacion,    // F008 dTotOpe
    decimal TotalDescuento,    // F009 dTotDesc
    decimal Redondeo,          // F013 dRedon
    decimal TotalGeneral,      // F014 dTotGralOpe
    decimal Iva5,              // F015 dIVA5
    decimal Iva10,             // F016 dIVA10
    decimal TotalIva,          // F017 dTotIVA
    decimal BaseGravada5,      // F018 dBaseGrav5
    decimal BaseGravada10,     // F019 dBaseGrav10
    decimal TotalBaseGravada); // F020 dTBasGraIVA

public sealed record FiscalDocumentModel(
    string CurrencyCode,
    IReadOnlyList<FiscalLine> Lines,
    FiscalTotals Totals);

/// <summary>
/// Politica de redondeo por item. PENDIENTE [XSD/TEST]: el Manual v150 admite hasta 8 decimales
/// (1-15p(0-8)) y SIFEN acepta tolerancias; los decimales por defecto son una decision provisoria.
/// </summary>
public sealed record FiscalRoundingPolicy(int Decimals)
{
    /// <summary>PYG sin decimales (provisorio, a confirmar en Test).</summary>
    public static FiscalRoundingPolicy Guarani { get; } = new(0);

    internal decimal Round(decimal value) => Math.Round(value, Decimals, MidpointRounding.AwayFromZero);
}

/// <summary>
/// Motor de calculo fiscal puro (sin XML, sin acceso a datos). Unica fuente de montos.
/// Alcance actual: PYG, IVA 10 %, 5 %, exento (E731 = 1 o 3), sin descuentos, anticipos, comision ni redondeo.
/// Fuentes: Manual v150 E727, EA008, E733-E737 y F002-F020; NT-13 (E735, dBasExe).
/// </summary>
public static class FiscalCalculationEngine
{
    public const string SupportedCurrency = "PYG";

    public static FiscalDocumentModel Calculate(
        IReadOnlyList<FiscalLineInput> lines,
        string currencyCode,
        FiscalRoundingPolicy? rounding = null)
    {
        ArgumentNullException.ThrowIfNull(lines);

        if (!string.Equals(currencyCode, SupportedCurrency, StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainException($"Moneda no soportada: {currencyCode}. Solo {SupportedCurrency} en la primera emision.");
        }

        if (lines.Count == 0)
        {
            throw new DomainException("El documento requiere al menos un item.");
        }

        var policy = rounding ?? FiscalRoundingPolicy.Guarani;
        var result = new List<FiscalLine>(lines.Count);

        for (var i = 0; i < lines.Count; i++)
        {
            result.Add(CalculateLine(i + 1, lines[i], policy));
        }

        return new FiscalDocumentModel(SupportedCurrency, result, CalculateTotals(result));
    }

    private static FiscalLine CalculateLine(int number, FiscalLineInput input, FiscalRoundingPolicy policy)
    {
        if (input.Quantity <= 0)
        {
            throw new DomainException($"Item {number}: la cantidad debe ser mayor que cero.");
        }

        if (input.UnitPrice < 0)
        {
            throw new DomainException($"Item {number}: el precio unitario no puede ser negativo.");
        }

        var bruto = policy.Round(input.UnitPrice * input.Quantity);
        var total = bruto; // EA008 sin descuentos ni anticipos

        switch (input.VatType)
        {
            case InvoiceVatType.Vat10:
            case InvoiceVatType.Vat5:
            {
                var rate = input.VatType == InvoiceVatType.Vat10 ? 10m : 5m;
                var divisor = input.VatType == InvoiceVatType.Vat10 ? 1.1m : 1.05m;
                const decimal proportion = 100m;
                var baseGravada = policy.Round(total * (proportion / 100m) / divisor);   // E735
                var liquidacion = policy.Round(baseGravada * (rate / 100m));             // E736
                return new FiscalLine(number, input.Quantity, input.UnitPrice, input.VatType, bruto, total,
                    1, proportion, rate, baseGravada, liquidacion, 0m);
            }
            case InvoiceVatType.Exempt:
                // E731=3: E734=0, E735=0, E736=0. dBasExe (NT-13): valor del monto exento.
                // PENDIENTE [XSD]: formula exacta de dBasExe para E731=3 (aqui = EA008).
                return new FiscalLine(number, input.Quantity, input.UnitPrice, input.VatType, bruto, total,
                    3, 0m, 0m, 0m, 0m, total);
            default:
                throw new DomainException($"Item {number}: tipo de IVA no soportado.");
        }
    }

    private static FiscalTotals CalculateTotals(IReadOnlyList<FiscalLine> lines)
    {
        var subExe = lines.Where(l => l.VatType == InvoiceVatType.Exempt).Sum(l => l.TotalOperacion);
        var sub5 = lines.Where(l => l.VatType == InvoiceVatType.Vat5).Sum(l => l.TotalOperacion);
        var sub10 = lines.Where(l => l.VatType == InvoiceVatType.Vat10).Sum(l => l.TotalOperacion);
        var totalOpe = subExe + sub5 + sub10;                                            // F008
        var iva5 = lines.Where(l => l.VatType == InvoiceVatType.Vat5).Sum(l => l.LiquidacionIva);   // F015
        var iva10 = lines.Where(l => l.VatType == InvoiceVatType.Vat10).Sum(l => l.LiquidacionIva); // F016
        var base5 = lines.Where(l => l.VatType == InvoiceVatType.Vat5).Sum(l => l.BaseGravadaIva);  // F018
        var base10 = lines.Where(l => l.VatType == InvoiceVatType.Vat10).Sum(l => l.BaseGravadaIva);// F019

        return new FiscalTotals(
            subExe, sub5, sub10, totalOpe,
            TotalDescuento: 0m,
            Redondeo: 0m,
            TotalGeneral: totalOpe,                 // F014 = F008 - F013 + F025 (sin redondeo ni comision)
            iva5, iva10,
            TotalIva: iva5 + iva10,                 // F017 = F015 + F016 - F036 - F037 + F026 (ultimos en 0)
            base5, base10,
            TotalBaseGravada: base5 + base10);      // F020 = F018 + F019
    }
}
