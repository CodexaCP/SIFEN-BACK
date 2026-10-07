using Microsoft.Extensions.Configuration;
using SifenInvoicing.Application.XmlDe;
using SifenInvoicing.Infrastructure.XmlValidation;
using SifenInvoicing.Tests.XmlDe;

namespace SifenInvoicing.Tests;

/// <summary>
/// Piezas reales del flujo DE01 para las pruebas de integracion de EfInvoiceService: builder oficial y validador XSD
/// contra el paquete local v150 (sin Internet). Las decisiones de configuracion del operador (iTipTra, iIndPres)
/// se fijan aqui como datos de prueba; no son valores por defecto de produccion.
/// </summary>
internal static class DeTestKit
{
    public static Dictionary<string, string?> DeDefaults => new()
    {
        ["Sifen:De:DefaultTransactionType"] = "1",
        ["Sifen:De:DefaultPresenceIndicator"] = "1",
    };

    public static SifenDeXmlBuilder Builder(SifenDeBuilderOptions? options = null) =>
        new(options ?? new SifenDeBuilderOptions(DSisFactMode.EmitContributorSystem));

    public static ISifenDeXsdValidator Xsd() =>
        new SifenDeXsdValidator(
            new XmlSchemaValidator(),
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { [SifenDeXsdValidator.ConfigurationKey] = XsdPackage.Dir })
                .Build());
}
