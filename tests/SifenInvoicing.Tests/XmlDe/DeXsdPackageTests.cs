using System.Security.Cryptography;
using System.Xml.Linq;
using System.Xml.Schema;
using Xunit.Abstractions;

namespace SifenInvoicing.Tests.XmlDe;

/// <summary>
/// Fase 4.1 - pruebas contra el paquete XSD OFICIAL v150 (carpeta XmlDe/XsdPackage/v150 o SIFEN_XSD_DIR).
/// Se OMITEN mientras el paquete no este presente. En el momento de escribirse, el paquete NO pudo descargarse
/// (el entorno no alcanza ekuatia.set.gov.py), por lo que estas pruebas estan ESCRITAS PERO NO EJECUTADAS.
/// Ninguna asercion de estructura debe "ajustarse" para pasar: si falla, la discrepancia XSD/Manual se documenta.
/// </summary>
public sealed class XsdPackageFactAttribute : FactAttribute
{
    public XsdPackageFactAttribute(string requiredFile = XsdPackage.DeRoot)
    {
        if (!XsdPackage.Has(requiredFile))
        {
            Skip = $"Paquete XSD v150 ausente ({requiredFile} no esta en {XsdPackage.Dir}): ejecutar tools/sifen-xsd/fetch-xsd.sh (PENDIENTE DE XSD).";
        }
    }
}

public sealed class DeXsdPackageTests
{
    private readonly ITestOutputHelper _out;
    public DeXsdPackageTests(ITestOutputHelper output) => _out = output;

    private static readonly XNamespace Ns = DeReferenceStructure.Namespace;

    private static List<string> Validate(XDocument doc)
    {
        var set = XsdPackage.Load(XsdPackage.DeRoot);
        var errors = new List<string>();
        doc.Validate(set, (_, e) => errors.Add(e.Message));
        return errors;
    }

    // ---------- paquete ----------

    [XsdPackageFact(XsdPackage.ReceptionRoot)]
    public void Package_AllImportsAndIncludes_Resolve_ForReceptionAndDe()
    {
        foreach (var root in new[] { XsdPackage.ReceptionRoot, XsdPackage.DeRoot })
        {
            var closure = XsdPackage.Closure(XsdPackage.Dir, root);
            foreach (var c in closure) _out.WriteLine($"{root}: {c.File} <- {c.By ?? "(raiz)"} {(c.Exists ? "OK" : "FALTA")}");
            Assert.Empty(closure.Where(c => !c.Exists).Select(c => $"{c.File} (referenciado por {c.By})"));
        }
    }

    [XsdPackageFact]
    public void Package_Manifest_HashesMatchFiles_AndNamesAreV150()
    {
        var manifest = Path.Combine(XsdPackage.Dir, "MANIFEST.tsv");
        Assert.True(File.Exists(manifest), "Falta MANIFEST.tsv (lo genera tools/sifen-xsd/fetch-xsd.sh).");
        foreach (var line in File.ReadLines(manifest).Skip(1).Where(l => l.Length > 0))
        {
            var cols = line.Split('\t');
            Assert.NotEqual("NO_DESCARGADO", cols[2]);
            var path = Path.Combine(XsdPackage.Dir, cols[0]);
            Assert.Equal(cols[3], Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant());
            Assert.Equal(long.Parse(cols[2]), new FileInfo(path).Length);
        }
    }

    [XsdPackageFact]
    public void Schema_LoadsCompiled_NamespaceAndRoot()
    {
        var problems = new List<string>();
        var set = XsdPackage.Load(XsdPackage.DeRoot, problems);
        Assert.Empty(problems);
        Assert.Contains(set.Schemas().Cast<XmlSchema>(), s => s.TargetNamespace == DeReferenceStructure.Namespace);
        Assert.True(set.GlobalElements.Contains(new System.Xml.XmlQualifiedName("rDE", DeReferenceStructure.Namespace)));
    }

    // ---------- estructura segun XSD ----------

    [XsdPackageFact]
    public void Xsd_RootChildren_DVerFor_DE_Signature_GCamFuFD_InOrder()
    {
        var kids = XsdModel.Children(XsdModel.FindElement(XsdPackage.Load(XsdPackage.DeRoot), "rDE")!);
        foreach (var k in kids) _out.WriteLine($"rDE/{k.Name} {k.Min}..{k.Max} choice={k.InChoice}");
        Assert.Equal(DeReferenceStructure.Order["rDE"], kids.Select(k => k.Name).ToArray());
    }

    [XsdPackageFact]
    public void Xsd_ChildOrder_MatchesReference_ForAllCoveredGroups()
    {
        var set = XsdPackage.Load(XsdPackage.DeRoot);
        var diffs = new List<string>();
        foreach (var (group, reference) in DeReferenceStructure.Order)
        {
            var el = XsdModel.FindElement(set, group);
            if (el is null) { diffs.Add($"{group}: no declarado en el XSD"); continue; }
            var xsd = XsdModel.Children(el).Select(k => k.Name).ToList();
            _out.WriteLine($"{group}: {string.Join(",", xsd)}");
            var missing = reference.Where(r => !xsd.Contains(r)).ToList();
            if (missing.Count > 0) diffs.Add($"{group}: el XSD no tiene {string.Join(",", missing)}");
            var common = reference.Where(xsd.Contains).ToList();
            if (!common.SequenceEqual(xsd.Where(common.Contains))) diffs.Add($"{group}: orden distinto (ref {string.Join(",", common)} / xsd {string.Join(",", xsd.Where(common.Contains))})");
        }

        Assert.Empty(diffs);
    }

    [XsdPackageFact]
    public void Xsd_MandatoryElementsOfReference_AreMinOccursAtLeastOne()
    {
        var set = XsdPackage.Load(XsdPackage.DeRoot);
        var bad = new List<string>();
        foreach (var (group, required) in DeReferenceStructure.Required)
        {
            var kids = XsdModel.Children(XsdModel.FindElement(set, group)!);
            foreach (var r in required)
            {
                var k = kids.FirstOrDefault(c => c.Name == r);
                if (k is null || k.Min < 1 || k.InChoice) bad.Add($"{group}/{r} -> {(k is null ? "ausente" : $"min={k.Min} choice={k.InChoice}")}");
            }
        }

        Assert.Empty(bad);
    }

    [XsdPackageFact]
    public void Xsd_DocumentsCardinalityAndTypeOfKeyElements()
    {
        // No afirma valores: registra lo que dice el XSD para cerrar los PENDIENTE (dFeFinT, dBasExe, gPaConEIni, ...).
        var set = XsdPackage.Load(XsdPackage.DeRoot);
        foreach (var (parent, child) in new[]
        {
            ("gTimb", "dFeFinT"), ("gTimb", "dFeIniT"), ("gTimb", "dSerieNum"), ("gCamIVA", "dBasExe"), ("gCamIVA", "dBasGravIVA"),
            ("gCamCond", "gPaConEIni"), ("gDatRec", "iTipIDRec"), ("gDatRec", "dNumIDRec"), ("gDatRec", "cDepRec"),
            ("gTotSub", "dSubExe"), ("gTotSub", "dTotalGs"), ("gOpeDE", "dInfoEmi"), ("DE", "dSisFact"),
        })
        {
            var p = XsdModel.FindElement(set, parent);
            var k = p is null ? null : XsdModel.Children(p).FirstOrDefault(c => c.Name == child);
            _out.WriteLine($"{parent}/{child}: {(k is null ? "NO EXISTE EN XSD" : $"{k.Min}..{k.Max} choice={k.InChoice}")}");
        }

        Assert.True(true);
    }

    [XsdPackageFact]
    public void Xsd_DocumentsFacetsOfKeyElements()
    {
        var set = XsdPackage.Load(XsdPackage.DeRoot);
        foreach (var name in new[] { "dVerFor", "dDVId", "dNumTim", "dEst", "dPunExp", "dNumDoc", "dCodSeg", "iTiDE", "cMoneOpe", "dTasaIVA", "dCantProSer", "dDesProSer", "dCodInt", "dFecFirma", "dCarQR" })
        {
            var el = XsdModel.FindElement(set, name);
            _out.WriteLine(el is null ? $"{name}: NO EXISTE" : $"{name}: " + string.Join("; ", XsdModel.Facets(el).Select(f => $"{f.Key}={string.Join("|", f.Value)}")));
        }

        Assert.True(true);
    }

    // ---------- validacion de documentos ----------

    [XsdPackageFact]
    public void Validation_MinimalFixture_WithPlaceholderSignature_IsEvaluated()
    {
        var errors = Validate(DeFixtures.MinimalDe01WithPlaceholderSignature());
        errors.ForEach(e => _out.WriteLine("XSD: " + e));
        Assert.Empty(errors);
    }

    [XsdPackageFact]
    public void Validation_OfficialSample_ResultIsDocumented()
    {
        // La muestra es anterior a NT-10/13: se espera discrepancia (dSisFact, falta dBasExe). No se "arregla" la muestra.
        var errors = Validate(XDocument.Load(DeFixtures.SamplePath));
        errors.ForEach(e => _out.WriteLine("XSD(muestra): " + e));
        Assert.True(true);
    }

    [XsdPackageFact]
    public void Negative_UnsignedFixture_IsRejected()
    {
        Assert.NotEmpty(Validate(DeFixtures.MinimalDe01()));
    }

    [XsdPackageFact]
    public void Negative_OutOfOrder_UnknownElement_MissingMandatory_WrongType_OutOfRestriction_WrongNamespace()
    {
        XDocument Fresh() => DeFixtures.MinimalDe01WithPlaceholderSignature();
        XElement De(XDocument d) => d.Root!.Element(Ns + "DE")!;

        var outOfOrder = Fresh();
        var gTimb = De(outOfOrder).Element(Ns + "gTimb")!;
        var est = gTimb.Element(Ns + "dEst")!; est.Remove(); gTimb.Element(Ns + "dNumDoc")!.AddAfterSelf(est);
        Assert.NotEmpty(Validate(outOfOrder));

        var unknown = Fresh();
        De(unknown).Element(Ns + "gOpeDE")!.Add(new XElement(Ns + "dElementoInventado", "x"));
        Assert.NotEmpty(Validate(unknown));

        var missing = Fresh();
        De(missing).Descendants(Ns + "dBasExe").First().Remove();
        Assert.NotEmpty(Validate(missing));

        var wrongType = Fresh();
        De(wrongType).Element(Ns + "gTimb")!.Element(Ns + "dNumTim")!.Value = "ABC";
        Assert.NotEmpty(Validate(wrongType));

        var outOfRestriction = Fresh();
        De(outOfRestriction).Element(Ns + "gTimb")!.Element(Ns + "dEst")!.Value = "12345";
        Assert.NotEmpty(Validate(outOfRestriction));

        var removedByNt = Fresh();
        De(removedByNt).Element(Ns + "dFecFirma")!.AddAfterSelf(new XElement(Ns + "dSisFact", "1"));
        Assert.NotEmpty(Validate(removedByNt));

        var wrongNs = XDocument.Parse(DeFixtures.MinimalDe01Xml().Replace(DeReferenceStructure.Namespace + "\"", "http://ekuatia.set.gov.py/sifen/otro\"", StringComparison.Ordinal));
        Assert.NotEmpty(Validate(wrongNs));
    }

    // ---------- firma / QR segun XSD ----------

    [XsdPackageFact]
    public void Signature_AndGCamFuFD_ArePeersOfDE_InsideRDE()
    {
        var kids = XsdModel.Children(XsdModel.FindElement(XsdPackage.Load(XsdPackage.DeRoot), "rDE")!).Select(k => k.Name).ToList();
        Assert.True(kids.IndexOf("DE") < kids.IndexOf("Signature") && kids.IndexOf("Signature") < kids.IndexOf("gCamFuFD"));
        var fufd = XsdModel.Children(XsdModel.FindElement(XsdPackage.Load(XsdPackage.DeRoot), "gCamFuFD")!);
        Assert.Contains(fufd, c => c.Name == "dCarQR");
    }

    [XsdPackageFact]
    public void Signature_XmlDsigSchema_AcceptsOnlyDeclaredAlgorithmsStructure_Documented()
    {
        // Documenta (no asume) si el XSD limita Algorithm/URI de CanonicalizationMethod, SignatureMethod, Transform.
        var set = XsdPackage.Load(XsdPackage.DeRoot);
        foreach (var name in new[] { "CanonicalizationMethod", "SignatureMethod", "Transform", "DigestMethod", "Reference" })
        {
            var el = XsdModel.FindElement(set, name);
            _out.WriteLine($"{name}: " + (el is null ? "NO EXISTE" : string.Join(",", XsdModel.Children(el).Select(c => $"{c.Name}[{c.Min}..{c.Max}]"))));
        }

        Assert.True(true);
    }
}

public sealed class WsXsdPackageTests
{
    private readonly ITestOutputHelper _out;
    public WsXsdPackageTests(ITestOutputHelper output) => _out = output;

    [XsdPackageFact(XsdPackage.ReceptionRoot)]
    public void Reception_Loads_AndDocumentsRequestResponseStructure()
    {
        var problems = new List<string>();
        var set = XsdPackage.Load(XsdPackage.ReceptionRoot, problems);
        Assert.Empty(problems);
        foreach (var name in new[] { "rEnviDe", "rRetEnviDe", "rProtDe", "gResProc", "xDE", "dId" })
        {
            var el = XsdModel.FindElement(set, name);
            _out.WriteLine(el is null
                ? $"{name}: NO EXISTE EN EL PAQUETE CARGADO"
                : $"{name}: " + string.Join(", ", XsdModel.Children(el).Select(c => $"{c.Name}[{c.Min}..{c.Max}]")));
        }

        // Sin aserciones de contenido: SOAPAction/endpoint/operaciones NO estan en los XSD (PENDIENTE DE WSDL).
    }

    [XsdPackageFact("protProcesDE_v150.xsd")]
    public void ProtProces_Loads()
    {
        var problems = new List<string>();
        XsdPackage.Load("protProcesDE_v150.xsd", problems);
        Assert.Empty(problems);
    }
}
