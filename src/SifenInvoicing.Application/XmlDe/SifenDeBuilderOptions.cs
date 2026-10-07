namespace SifenInvoicing.Application.XmlDe;

/// <summary>
/// dSisFact (A005). CONTRADICCION ABIERTA: NT-10 (04/02/2022, p.62) lo elimina; DE_v150.xsd lo declara 1..1 (valor 1).
/// PENDIENTE DE PRUEBA SIFEN TEST. El modo es provisional y configurable; ninguno es una decision definitiva.
/// </summary>
public enum DSisFactMode
{
    /// <summary>Segun NT-10: no se emite. El XSD publicado rechaza el documento (unico error esperado).</summary>
    Omit = 0,

    /// <summary>Segun DE_v150.xsd: se emite dSisFact=1 (1 = sistema de facturacion del contribuyente).</summary>
    EmitContributorSystem = 1,
}

/// <summary>Politica para elementos opcionales con valor cero (reglas de omision de ceros: PENDIENTE DE PRUEBA SIFEN).</summary>
public enum OptionalZeroPolicy
{
    /// <summary>Se informan aunque sean 0 (Manual: "completar con 0" cuando aplica).</summary>
    Emit = 0,

    /// <summary>Se omiten los subtotales/liquidaciones por tasa cuando valen 0.</summary>
    OmitPerRateTotals = 1,
}

/// <summary>
/// Leyenda del ambiente de prueba. CONTRADICCION Manual D105 vs Guia de Pruebas (texto y campo). Provisional:
/// dNomEmi usa el literal del Manual/muestra oficial; la leyenda de la Guia en el primer item es opcional.
/// </summary>
public sealed record TestEnvironmentLegend(string EmitterNameLiteral, string? FirstItemDescriptionLiteral)
{
    public static TestEnvironmentLegend Default { get; } =
        new("DE generado en ambiente de prueba - sin valor comercial ni fiscal", null);
}

public sealed record SifenDeBuilderOptions(
    DSisFactMode DSisFact = DSisFactMode.Omit,
    OptionalZeroPolicy ZeroPolicy = OptionalZeroPolicy.Emit,
    TestEnvironmentLegend? TestLegend = null)
{
    public static SifenDeBuilderOptions Default { get; } = new();
}
