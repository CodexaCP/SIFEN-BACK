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

    private static List<string> Validate(XDocument doc) => XsdPackage.ValidateDe(doc);

    // ---------- paquete ----------

    [XsdPackageFact(XsdPackage.ReceptionRoot)]
    public void Package_AllImportsAndIncludes_Resolve_ForReceptionAndDe()
    {
        foreach (var root in XsdPackage.RequiredRoots)
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
        var required = XsdPackage.RequiredRoots.SelectMany(r => XsdPackage.Closure(XsdPackage.Dir, r)).Select(c => c.File)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var line in File.ReadLines(manifest).Skip(1).Where(l => l.Length > 0))
        {
            var cols = line.Split('\t');
            if (cols[2] == "NO_DESCARGADO")
            {
                // Solo se tolera un faltante publicado como tal por DNIT fuera del cierre requerido (p. ej. rde/150/*.xsd
                // de siRecepRDE_v150.xsd, 404 en el servidor oficial). Queda documentado en el manifiesto.
                _out.WriteLine($"NO_DESCARGADO (fuera del cierre requerido): {cols[0]} <- {cols[5]}");
                Assert.DoesNotContain(cols[0], required);
                continue;
            }

            var path = Path.Combine(XsdPackage.Dir, cols[0]);
            Assert.Equal(cols[3], Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant());
            Assert.Equal(long.Parse(cols[2]), new FileInfo(path).Length);
        }
    }

    [XsdPackageFact]
    public void Schema_LoadsCompiled_NamespaceAndRoot()
    {
        var problems = new List<string>();
        var set = XsdPackage.Load(XsdPackage.ReceptionRoot, problems);
        Assert.Empty(problems);
        Assert.Contains(set.Schemas().Cast<XmlSchema>(), s => s.TargetNamespace == DeReferenceStructure.Namespace);
        Assert.True(set.GlobalElements.Contains(new System.Xml.XmlQualifiedName("rDE", DeReferenceStructure.Namespace)));
    }

    // ---------- estructura segun XSD ----------

    [XsdPackageFact]
    public void Xsd_RootChildren_DVerFor_DE_Signature_GCamFuFD_InOrder()
    {
        var kids = XsdModel.Children(XsdModel.FindElement(XsdPackage.Load(XsdPackage.ReceptionRoot), "rDE")!);
        foreach (var k in kids) _out.WriteLine($"rDE/{k.Name} {k.Min}..{k.Max} choice={k.InChoice}");
        Assert.Equal(DeReferenceStructure.Order["rDE"], kids.Select(k => k.Name).ToArray());
    }

    [XsdPackageFact]
    public void Xsd_ChildOrder_MatchesReference_ForAllCoveredGroups()
    {
        var set = XsdPackage.Load(XsdPackage.ReceptionRoot);
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
        var set = XsdPackage.Load(XsdPackage.ReceptionRoot);
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
    public void Xsd_ConditionallyRequiredGroups_AreOptionalInXsd_AndMustBeEnforcedByBuilder()
    {
        var set = XsdPackage.Load(XsdPackage.ReceptionRoot);
        foreach (var (path, rule) in DeReferenceStructure.RequiredForDe01ButOptionalInXsd)
        {
            var parts = path.Split('/');
            var k = XsdModel.Children(XsdModel.FindElement(set, parts[0])!).Single(c => c.Name == parts[1]);
            _out.WriteLine($"{path}: XSD {k.Min}..{k.Max}; {rule}");
            Assert.Equal(0m, k.Min);
        }
    }

    [XsdPackageFact]
    public void Xsd_GTimb_DoesNotDeclareDFeFinT_AndKeyCardinalitiesAreConfirmed()
    {
        // Cierra F-5 de Fase 4.0: el Manual (C009) lista dFeFinT 1-1, pero el XSD publicado no lo declara.
        var set = XsdPackage.Load(XsdPackage.ReceptionRoot);
        Card Of(string parent, string child) =>
            XsdModel.Children(XsdModel.FindElement(set, parent)!).Where(c => c.Name == child).Select(c => new Card(c.Min, c.Max)).SingleOrDefault();
        Assert.Null(Of("gTimb", "dFeFinT"));
        Assert.Equal(new Card(1, 1), Of("gCamIVA", "dBasExe"));
        Assert.Equal(new Card(1, 1), Of("gCamIVA", "dBasGravIVA"));
        Assert.Equal(new Card(0, 999), Of("gCamCond", "gPaConEIni"));
        Assert.Equal(new Card(1, 999), Of("gDtipDE", "gCamItem"));
        Assert.Equal(new Card(1, 1), Of("gTotSub", "dTotGralOpe"));
        Assert.Equal(new Card(0, 1), Of("gTotSub", "dTotalGs"));
    }

    private sealed record Card(decimal Min, decimal Max);

    [XsdPackageFact]
    public void Xsd_DocumentsCardinalityAndTypeOfKeyElements()
    {
        // No afirma valores: registra lo que dice el XSD para cerrar los PENDIENTE (dFeFinT, dBasExe, gPaConEIni, ...).
        var set = XsdPackage.Load(XsdPackage.ReceptionRoot);
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
        var set = XsdPackage.Load(XsdPackage.ReceptionRoot);
        foreach (var name in new[] { "dVerFor", "dDVId", "dNumTim", "dEst", "dPunExp", "dNumDoc", "dCodSeg", "iTiDE", "cMoneOpe", "dTasaIVA", "dCantProSer", "dDesProSer", "dCodInt", "dFecFirma", "dCarQR" })
        {
            var el = XsdModel.FindElement(set, name);
            _out.WriteLine(el is null ? $"{name}: NO EXISTE" : $"{name}: " + string.Join("; ", XsdModel.Facets(el).Select(f => $"{f.Key}={string.Join("|", f.Value)}")));
        }

        Assert.True(true);
    }

    // ---------- validacion de documentos ----------

    [XsdPackageFact]
    public void Validation_MinimalFixture_OnlyFailsOnDSisFact_ContradictionNt10VsXsd()
    {
        // Fixture DE01 (sin dSisFact, por NT-10). El XSD publicado exige dSisFact: ese debe ser el UNICO error.
        var errors = Validate(DeFixtures.MinimalDe01WithPlaceholderSignature());
        errors.ForEach(e => _out.WriteLine("XSD: " + e));
        var single = Assert.Single(errors);
        Assert.Contains("dSisFact", single);
    }

    [XsdPackageFact]
    public void Validation_MinimalFixture_WithPublishedXsdDSisFact_IsValid()
    {
        // Misma forma + dSisFact=1 (variante XSD publicado): valida sin errores. No decide que se emita dSisFact.
        var errors = Validate(DeFixtures.WithPublishedXsdDSisFact(DeFixtures.MinimalDe01WithPlaceholderSignature()));
        errors.ForEach(e => _out.WriteLine("XSD: " + e));
        Assert.Empty(errors);
    }

    [XsdPackageFact]
    public void Xsd_DSisFact_IsDeclaredMandatory_InPublishedDeV150()
    {
        // Contradiccion documentada: NT-10 (04/02/2022, p.62) elimina A005 dSisFact; DE_v150.xsd lo declara 1..1, max 1.
        // Si DNIT republica el XSD sin dSisFact, esta prueba falla y la contradiccion se cierra.
        var set = XsdPackage.Load(XsdPackage.ReceptionRoot);
        var k = XsdModel.Children(XsdModel.FindElement(set, "DE")!).Single(c => c.Name == "dSisFact");
        Assert.Equal(1m, k.Min);
        Assert.Equal(1m, k.Max);
        Assert.Equal(new List<string> { "1" }, XsdModel.Facets(XsdModel.FindElement(set, "dSisFact")!)["MaxInclusiveFacet"]);
    }

    [XsdPackageFact]
    public void Validation_OfficialSample_ResultIsDocumented()
    {
        // La muestra es anterior a NT-13: se espera discrepancia (falta dBasExe, RUC 0000000x fuera de patron, certificado de
        // relleno). No se "arregla" la muestra.
        var errors = Validate(XDocument.Load(DeFixtures.SamplePath));
        errors.ForEach(e => _out.WriteLine("XSD(muestra): " + e));
        Assert.Contains(errors, e => e.Contains("dBasExe"));
        Assert.Contains(errors, e => e.Contains("dRucEm"));
        Assert.DoesNotContain(errors, e => e.Contains("dSisFact"));
    }

    [XsdPackageFact]
    public void Negative_UnsignedFixture_IsRejected()
    {
        Assert.NotEmpty(Validate(DeFixtures.WithPublishedXsdDSisFact(DeFixtures.MinimalDe01())));
    }

    [XsdPackageFact]
    public void Negative_OutOfOrder_UnknownElement_MissingMandatory_WrongType_OutOfRestriction_WrongNamespace()
    {
        XDocument Fresh() => DeFixtures.WithPublishedXsdDSisFact(DeFixtures.MinimalDe01WithPlaceholderSignature());
        XElement De(XDocument d) => d.Root!.Element(Ns + "DE")!;
        void Rejected(string caso, XDocument d, string expected)
        {
            var errors = Validate(d);
            errors.ForEach(e => _out.WriteLine($"{caso}: {e}"));
            Assert.Contains(errors, e => e.Contains(expected, StringComparison.Ordinal));
        }

        Assert.Empty(Validate(Fresh()));

        var outOfOrder = Fresh();
        var gTimb = De(outOfOrder).Element(Ns + "gTimb")!;
        var est = gTimb.Element(Ns + "dEst")!; est.Remove(); gTimb.Element(Ns + "dNumDoc")!.AddAfterSelf(est);
        Rejected("fuera de orden", outOfOrder, "dPunExp");

        var unknown = Fresh();
        De(unknown).Element(Ns + "gOpeDE")!.Add(new XElement(Ns + "dElementoInventado", "x"));
        Rejected("desconocido", unknown, "dElementoInventado");

        var missing = Fresh();
        De(missing).Descendants(Ns + "dBasExe").First().Remove();
        Rejected("obligatorio ausente", missing, "dBasExe");

        var wrongType = Fresh();
        De(wrongType).Element(Ns + "gTimb")!.Element(Ns + "dNumTim")!.Value = "ABC";
        Rejected("tipo", wrongType, "dNumTim");

        var outOfRestriction = Fresh();
        De(outOfRestriction).Element(Ns + "gTimb")!.Element(Ns + "dEst")!.Value = "12345";
        Rejected("restriccion", outOfRestriction, "dEst");

        var badStructure = Fresh();
        var sig = badStructure.Root!.Element(XName.Get("Signature", DeReferenceStructure.DsigNamespace))!;
        sig.Remove(); De(badStructure).Add(sig);
        Rejected("estructura (Signature dentro de DE)", badStructure, "Signature");

        var wrongNs = XDocument.Parse(Fresh().ToString().Replace(DeReferenceStructure.Namespace + "\"", "http://ekuatia.set.gov.py/sifen/otro\"", StringComparison.Ordinal));
        Rejected("namespace", wrongNs, "rDE");
    }

    // ---------- firma / QR segun XSD ----------

    [XsdPackageFact]
    public void Signature_AndGCamFuFD_ArePeersOfDE_InsideRDE()
    {
        var kids = XsdModel.Children(XsdModel.FindElement(XsdPackage.Load(XsdPackage.ReceptionRoot), "rDE")!).Select(k => k.Name).ToList();
        Assert.True(kids.IndexOf("DE") < kids.IndexOf("Signature") && kids.IndexOf("Signature") < kids.IndexOf("gCamFuFD"));
        var fufd = XsdModel.Children(XsdModel.FindElement(XsdPackage.Load(XsdPackage.ReceptionRoot), "gCamFuFD")!);
        Assert.Contains(fufd, c => c.Name == "dCarQR");
    }

    [XsdPackageFact]
    public void Signature_XmlDsigSchema_AcceptsOnlyDeclaredAlgorithmsStructure_Documented()
    {
        // Documenta (no asume) si el XSD limita Algorithm/URI de CanonicalizationMethod, SignatureMethod, Transform.
        var set = XsdPackage.Load(XsdPackage.ReceptionRoot);
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
        var set = XsdPackage.Load(XsdPackage.WsReceptionRoot, problems);
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
