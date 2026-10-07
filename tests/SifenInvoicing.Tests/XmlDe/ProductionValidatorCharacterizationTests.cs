using SifenInvoicing.Infrastructure.XmlValidation;
using Xunit.Abstractions;

namespace SifenInvoicing.Tests.XmlDe;

/// <summary>
/// Fase 4.1 - caracterizacion (NO correccion) de XmlSchemaValidator de produccion frente al paquete XSD oficial v150.
/// Documenta brechas para la Fase 4.3; no modifica produccion.
/// </summary>
public sealed class ProductionValidatorCharacterizationTests
{
    private readonly ITestOutputHelper _out;
    public ProductionValidatorCharacterizationTests(ITestOutputHelper output) => _out = output;

    private static string Root => Path.Combine(XsdPackage.Dir, XsdPackage.ReceptionRoot);

    [XsdPackageFact(XsdPackage.ReceptionRoot)]
    public async Task Gap_WrongNamespaceDocument_IsReportedValid_BecauseWarningsAreIgnored()
    {
        // siRecepDE_v150.xsd incluye DE_v150.xsd por URL https absoluta: compilar exige red hacia ekuatia.set.gov.py
        // (el validador de produccion no usa resolver local). Si no hay red, se documenta y no se afirma nada mas.
        var xml = DeFixtures.MinimalDe01Xml().Replace(DeReferenceStructure.Namespace + "\"", "http://ekuatia.set.gov.py/sifen/otro\"", StringComparison.Ordinal);
        try
        {
            var result = await new XmlSchemaValidator().ValidateAsync(xml, Root);
            _out.WriteLine($"Namespace incorrecto -> IsValid={result.IsValid}, errores={result.Errors.Count}");
            Assert.True(result.IsValid); // BRECHA: deberia rechazarse (falta ReportValidationWarnings / chequeo de raiz)
        }
        catch (Exception ex) when (ex is System.Xml.XmlException or System.Net.Http.HttpRequestException or System.Xml.Schema.XmlSchemaException or IOException)
        {
            _out.WriteLine($"El validador de produccion no pudo compilar el paquete oficial sin red/resolver: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
