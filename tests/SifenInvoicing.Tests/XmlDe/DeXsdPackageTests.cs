using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;

namespace SifenInvoicing.Tests.XmlDe;

/// <summary>
/// Fase 4.0 - validacion contra el paquete XSD OFICIAL v150. Se omiten si SIFEN_XSD_DE_ROOT no apunta al XSD raiz
/// (p. ej. siRecepDE_v150.xsd, con sus imports/includes en la misma carpeta). El paquete NO estaba disponible en Fase 4.0:
/// estas pruebas NO fueron ejecutadas y no prueban nada hasta correrse con el paquete real.
/// </summary>
public sealed class DeXsdPackageFactAttribute : FactAttribute
{
    public const string Variable = "SIFEN_XSD_DE_ROOT";

    public DeXsdPackageFactAttribute()
    {
        var path = Environment.GetEnvironmentVariable(Variable);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            Skip = $"{Variable} no definida o inexistente: XSD oficial v150 no disponible (PENDIENTE DE XSD).";
        }
    }
}

public sealed class DeXsdPackageTests
{
    private static (XmlSchemaSet Set, List<string> LoadProblems) LoadSet()
    {
        var path = Environment.GetEnvironmentVariable(DeXsdPackageFactAttribute.Variable)!;
        var problems = new List<string>();
        var set = new XmlSchemaSet { XmlResolver = new XmlUrlResolver() };
        set.ValidationEventHandler += (_, e) => problems.Add(e.Message);
        set.Add(null, path);   // resuelve xs:import / xs:include relativos
        set.Compile();
        return (set, problems);
    }

    private static List<string> Validate(XDocument doc)
    {
        var (set, _) = LoadSet();
        var errors = new List<string>();
        doc.Validate(set, (_, e) => errors.Add(e.Message));
        return errors;
    }

    [DeXsdPackageFact]
    public void Xsd_LoadsWithImportsAndIncludes_TargetNamespaceIsSifen()
    {
        var (set, problems) = LoadSet();
        Assert.Empty(problems);
        Assert.Contains(set.Schemas().Cast<XmlSchema>(), s => s.TargetNamespace == DeReferenceStructure.Namespace);
        Assert.True(set.GlobalElements.Contains(new XmlQualifiedName("rDE", DeReferenceStructure.Namespace)));
    }

    [DeXsdPackageFact]
    public void Xsd_MinimalFixture_WithoutSignatureAndFuFD_IsReportedAsIncomplete()
    {
        // rDE exige Signature y gCamFuFD (1-1): el fixture sin firmar NO debe validar contra el XSD de rDE.
        Assert.NotEmpty(Validate(DeFixtures.MinimalDe01()));
    }

    [DeXsdPackageFact]
    public void Xsd_RejectsRemovedElementAndWrongOrder()
    {
        var withRemoved = DeFixtures.MinimalDe01();
        withRemoved.Root!.Element(DeFixtures.Ns + "DE")!.Element(DeFixtures.Ns + "dFecFirma")!
            .AddAfterSelf(new XElement(DeFixtures.Ns + "dSisFact", "1"));
        Assert.Contains(Validate(withRemoved), e => e.Contains("dSisFact"));
    }

    [DeXsdPackageFact]
    public void Xsd_Rejects_MalformedCdcId()
    {
        var doc = DeFixtures.MinimalDe01();
        doc.Root!.Element(DeFixtures.Ns + "DE")!.SetAttributeValue("Id", "123");
        Assert.NotEmpty(Validate(doc));
    }
}
