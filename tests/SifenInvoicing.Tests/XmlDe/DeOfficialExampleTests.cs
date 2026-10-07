using System.Text;
using System.Xml.Linq;
using SifenInvoicing.Application.Cdc;

namespace SifenInvoicing.Tests.XmlDe;

/// <summary>
/// Fase 4.0 - muestra oficial "Estructura xml_DE" (set.gov.py). Es anterior a NT-10/13 (trae dSisFact, no trae dBasExe):
/// se usa para orden/estructura y para la relacion DE / Signature / gCamFuFD / QR, no como DE01 v150 final.
/// </summary>
public sealed class DeOfficialExampleTests
{
    private static readonly XNamespace Ds = DeReferenceStructure.DsigNamespace;
    private static XDocument Sample => XDocument.Load(DeFixtures.SamplePath);

    private static string Hex(string text) => Convert.ToHexString(Encoding.ASCII.GetBytes(text)).ToLowerInvariant();

    [Fact]
    public void Sample_ConformsOrder_ExceptKnownPreNtElements()
    {
        // allowRemoved: la muestra conserva dSisFact (retirado por NT-10); dBasExe (NT-13) falta y se ignora aqui.
        var errors = DeConformance.Check(Sample, allowRemovedByNt: true, signed: true).Where(e => !e.Contains("dBasExe")).ToList();
        Assert.Empty(errors);
    }

    [Fact]
    public void Sample_RDE_ChildOrder_IsDVerFor_DE_Signature_GCamFuFD()
    {
        Assert.Equal(new[] { "dVerFor", "DE", "Signature", "gCamFuFD" }, Sample.Root!.Elements().Select(e => e.Name.LocalName));
        Assert.Equal(Ds + "Signature", Sample.Root.Elements().ElementAt(2).Name);
    }

    [Fact]
    public void Sample_Cdc_HasInconsistentCheckDigit_DocumentedDiscrepancy()
    {
        // La muestra trae DV=1; el algoritmo del Manual 10.1 (verificado con su propio ejemplo) da 9.
        // Se trata la muestra como ilustrativa: NO se cambia el algoritmo (PENDIENTE DE PRUEBA SIFEN).
        var de = Sample.Root!.Element(DeFixtures.Ns + "DE")!;
        var id = de.Attribute("Id")!.Value;
        Assert.Equal(44, id.Length);
        Assert.Equal(id[^1].ToString(), de.Element(DeFixtures.Ns + "dDVId")!.Value);
        Assert.False(CdcGenerator.ValidateCDC(id));
        Assert.True(CdcGenerator.ValidateCDC(id[..43] + "9"));
    }

    [Fact]
    public void Sample_Signature_ReferencesDeId_AndQrCarriesHexOfDigestValue()
    {
        var root = Sample.Root!;
        var id = root.Element(DeFixtures.Ns + "DE")!.Attribute("Id")!.Value;
        var reference = root.Element(Ds + "Signature")!.Descendants(Ds + "Reference").Single();
        Assert.Equal("#" + id, reference.Attribute("URI")!.Value);

        var digest = reference.Element(Ds + "DigestValue")!.Value;
        var qr = root.Element(DeFixtures.Ns + "gCamFuFD")!.Element(DeFixtures.Ns + "dCarQR")!.Value;
        var query = qr[(qr.IndexOf('?') + 1)..].Split('&').Select(p => p.Split('=', 2)).ToDictionary(p => p[0], p => p[1]);

        // QR: DigestValue = hex del TEXTO base64 de la firma => el QR se arma DESPUES de firmar.
        Assert.Equal(Hex(digest), query["DigestValue"]);
        Assert.Equal(id, query["Id"]);
        Assert.Equal(Hex(root.Descendants(DeFixtures.Ns + "dFeEmiDE").Single().Value), query["dFeEmiDE"]);
        Assert.Equal("2", query["cItems"]);
        Assert.Equal(root.Descendants(DeFixtures.Ns + "dTotGralOpe").Single().Value, query["dTotGralOpe"]);
        Assert.Equal(root.Descendants(DeFixtures.Ns + "dTotIVA").Single().Value, query["dTotIVA"]);
        Assert.Equal(root.Descendants(DeFixtures.Ns + "dRucRec").Single().Value, query["dRucRec"]);
    }

    [Fact]
    public void Sample_DocumentsKnownContradictions()
    {
        var root = Sample.Root!;
        // Contradiccion documentada: la muestra oficial conserva dSisFact (A005, retirado por NT-10).
        Assert.NotEmpty(root.Descendants(DeFixtures.Ns + "dSisFact"));
        // Muestra con dos transforms en la firma; NT-16 exige solo el enveloped (PENDIENTE DE PRUEBA SIFEN).
        Assert.Equal(2, root.Element(Ds + "Signature")!.Descendants(Ds + "Transform").Count());
    }
}
