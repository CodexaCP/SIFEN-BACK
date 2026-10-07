using SifenInvoicing.Infrastructure.XmlValidation;
using Xunit.Abstractions;

namespace SifenInvoicing.Tests.XmlDe;

/// <summary>
/// Fase 4.3 - el XmlSchemaValidator de produccion compila el paquete oficial v150 SIN Internet (resolver local) y trata
/// las advertencias como errores. Sustituye a la caracterizacion de brechas de la Fase 4.1.
/// </summary>
public sealed class ProductionValidatorCharacterizationTests
{
    private readonly ITestOutputHelper _out;
    public ProductionValidatorCharacterizationTests(ITestOutputHelper output) => _out = output;

    private static string Root => Path.Combine(XsdPackage.Dir, XsdPackage.ReceptionRoot);

    [XsdPackageFact(XsdPackage.ReceptionRoot)]
    public async Task WrongNamespaceDocument_IsRejected()
    {
        var xml = DeFixtures.MinimalDe01Xml().Replace(DeReferenceStructure.Namespace + "\"", "http://ekuatia.set.gov.py/sifen/otro\"", StringComparison.Ordinal);
        var result = await new XmlSchemaValidator().ValidateAsync(xml, Root);
        _out.WriteLine($"Namespace incorrecto -> IsValid={result.IsValid}, errores={result.Errors.Count}");
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Message.StartsWith("Warning:", StringComparison.Ordinal) || e.Message.StartsWith("Error:", StringComparison.Ordinal));
    }

    [XsdPackageFact(XsdPackage.ReceptionRoot)]
    public async Task OfficialPackage_CompilesWithoutNetwork_AndValidAnnotatedDocumentPasses()
    {
        var xml = DeFixtures.WithPublishedXsdDSisFact(DeFixtures.MinimalDe01WithPlaceholderSignature()).ToString();
        var result = await new XmlSchemaValidator().ValidateAsync(xml, Root);
        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(e => e.Message)));
    }

    [XsdPackageFact(XsdPackage.ReceptionRoot)]
    public async Task MissingDSisFact_IsRejectedByPublishedXsd()
    {
        var xml = DeFixtures.MinimalDe01WithPlaceholderSignature().ToString();
        var result = await new XmlSchemaValidator().ValidateAsync(xml, Root);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Message.Contains("dSisFact", StringComparison.Ordinal));
    }
}
