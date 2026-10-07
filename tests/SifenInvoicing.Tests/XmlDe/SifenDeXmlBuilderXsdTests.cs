using System.Xml.Linq;
using SifenInvoicing.Application.XmlDe;
using Xunit.Abstractions;

namespace SifenInvoicing.Tests.XmlDe;

/// <summary>
/// Fase 4.2 - la salida del builder contra el paquete XSD oficial local (XmlDe/XsdPackage/v150), sin red.
/// Signature y gCamFuFD se agregan con valores de relleno SOLO para evaluar estructura (no son firma ni QR reales).
/// La contradiccion dSisFact (NT-10 lo elimina; DE_v150.xsd lo exige 1..1) es el unico bloqueo de validacion esperado.
/// </summary>
public sealed class SifenDeXmlBuilderXsdTests
{
    private readonly ITestOutputHelper _out;
    public SifenDeXmlBuilderXsdTests(ITestOutputHelper output) => _out = output;

    private static readonly XNamespace Ns = SifenDeXmlBuilder.Namespace;

    private static XDocument Built(SifenDeBuilderOptions? options = null, SifenDeBuildInput? input = null) =>
        DeFixtures.AttachPlaceholderSignature(XDocument.Parse(new SifenDeXmlBuilder(options).Build(input ?? SifenDeBuilderFixtures.Input()).Xml));

    private static readonly SifenDeBuilderOptions WithDSisFact = new(DSisFact: DSisFactMode.EmitContributorSystem);

    [XsdPackageFact]
    public void Default_Omit_FailsXsdOnlyBecauseOfDSisFact()
    {
        var errors = XsdPackage.ValidateDe(Built());
        errors.ForEach(e => _out.WriteLine("XSD: " + e));
        var single = Assert.Single(errors);
        Assert.Contains("dSisFact", single);
    }

    [XsdPackageFact]
    public void EmitDSisFact_ValidatesAgainstOfficialXsd()
    {
        var errors = XsdPackage.ValidateDe(Built(WithDSisFact));
        errors.ForEach(e => _out.WriteLine("XSD: " + e));
        Assert.Empty(errors);
    }

    [XsdPackageFact]
    public void EmitDSisFact_NonTaxpayerReceiver_Validates()
    {
        Assert.Empty(XsdPackage.ValidateDe(Built(WithDSisFact, SifenDeBuilderFixtures.Input(SifenDeBuilderFixtures.NonTaxpayerReceiver()))));
    }

    [XsdPackageFact]
    public void EmitDSisFact_ZeroPolicyOmit_Validates()
    {
        Assert.Empty(XsdPackage.ValidateDe(Built(new SifenDeBuilderOptions(DSisFactMode.EmitContributorSystem, OptionalZeroPolicy.OmitPerRateTotals))));
    }

    [XsdPackageFact]
    public void EmitDSisFact_ProductionAndSeriesAndFirstItemLegend_Validate()
    {
        var input = SifenDeBuilderFixtures.Input();
        input = input with { Ambiente = SifenDeEnvironment.Production, Timbrado = input.Timbrado with { Series = "AB" } };
        Assert.Empty(XsdPackage.ValidateDe(Built(WithDSisFact, input)));

        var legend = new SifenDeBuilderOptions(DSisFactMode.EmitContributorSystem, TestLegend: new TestEnvironmentLegend(
            "DOCUMENTO ELECTRÓNICO SIN VALOR COMERCIAL NI FISCAL - GENERADO EN AMBIENTE DE PRUEBA",
            "DOCUMENTO ELECTRÓNICO SIN VALOR COMERCIAL NI FISCAL - GENERADO EN AMBIENTE DE PRUEBA"));
        Assert.Empty(XsdPackage.ValidateDe(Built(legend)));
    }

    [XsdPackageFact]
    public void EmitDSisFact_OutputConformsToReferenceStructure_OrderAndMandatory()
    {
        var doc = XDocument.Parse(new SifenDeXmlBuilder(WithDSisFact).Build(SifenDeBuilderFixtures.Input()).Xml);
        var errors = DeConformance.Check(doc, allowRemovedByNt: true);
        Assert.Empty(errors);
    }

    // ---------- negativos sobre la salida del builder: el XSD oficial los rechaza ----------

    private static XElement De(XDocument d) => d.Root!.Element(Ns + "DE")!;

    private void Rejected(string caso, XDocument d, string expected)
    {
        var errors = XsdPackage.ValidateDe(d);
        errors.ForEach(e => _out.WriteLine($"{caso}: {e}"));
        Assert.Contains(errors, e => e.Contains(expected, StringComparison.Ordinal));
    }

    [XsdPackageFact]
    public void Xsd_RejectsMutatedBuilderOutput()
    {
        var outOfOrder = Built(WithDSisFact);
        var gTimb = De(outOfOrder).Element(Ns + "gTimb")!;
        var est = gTimb.Element(Ns + "dEst")!; est.Remove(); gTimb.Element(Ns + "dNumDoc")!.AddAfterSelf(est);
        Rejected("fuera de orden", outOfOrder, "dPunExp");

        var unknown = Built(WithDSisFact);
        De(unknown).Element(Ns + "gOpeDE")!.Add(new XElement(Ns + "dElementoInventado", "x"));
        Rejected("desconocido", unknown, "dElementoInventado");

        var missing = Built(WithDSisFact);
        De(missing).Descendants(Ns + "dBasExe").First().Remove();
        Rejected("obligatorio ausente", missing, "dBasExe");

        var wrongType = Built(WithDSisFact);
        De(wrongType).Element(Ns + "gTimb")!.Element(Ns + "dNumTim")!.Value = "ABC";
        Rejected("tipo", wrongType, "dNumTim");

        var restriction = Built(WithDSisFact);
        De(restriction).Element(Ns + "gTimb")!.Element(Ns + "dEst")!.Value = "12345";
        Rejected("restriccion", restriction, "dEst");

        var badStructure = Built(WithDSisFact);
        var items = De(badStructure).Element(Ns + "gDtipDE")!.Elements(Ns + "gCamItem").ToList();
        var iva = items[0].Element(Ns + "gCamIVA")!; iva.Remove(); items[0].Element(Ns + "gValorItem")!.Add(iva);
        Rejected("estructura", badStructure, "gCamIVA");

        var wrongNs = XDocument.Parse(Built(WithDSisFact).ToString(SaveOptions.DisableFormatting)
            .Replace(SifenDeXmlBuilder.Namespace + "\"", "http://ekuatia.set.gov.py/sifen/otro\"", StringComparison.Ordinal));
        Rejected("namespace", wrongNs, "otro");
    }
}
