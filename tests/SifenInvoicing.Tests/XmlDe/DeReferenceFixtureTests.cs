using System.Xml.Linq;
using SifenInvoicing.Application.Cdc;

namespace SifenInvoicing.Tests.XmlDe;

/// <summary>
/// Fase 4.0 - pruebas del fixture de referencia y de la muestra oficial. Verifican FORMA (orden, obligatorios, elementos
/// retirados por NT), no aceptacion de SIFEN ni validez XSD (PENDIENTE: XSD oficial v150 no disponible).
/// </summary>
public sealed class DeReferenceFixtureTests
{
    private static XElement De(XDocument d) => d.Root!.Element(DeFixtures.Ns + "DE")!;
    private static string V(XElement e, params string[] path) =>
        path.Aggregate(e, (cur, n) => cur.Element(DeFixtures.Ns + n)!).Value;

    [Fact]
    public void Fixture_RootNamespaceAndVersion()
    {
        var doc = DeFixtures.MinimalDe01();
        Assert.Equal(DeFixtures.Ns + "rDE", doc.Root!.Name);
        Assert.Equal("150", V(doc.Root, "dVerFor"));
        Assert.Equal(new[] { "dVerFor", "DE" }, doc.Root.Elements().Select(e => e.Name.LocalName));
    }

    [Fact]
    public void Fixture_ConformsToReferenceOrderAndMandatory()
    {
        Assert.Empty(DeConformance.Check(DeFixtures.MinimalDe01()));
    }

    [Fact]
    public void Fixture_DoesNotEmit_ElementsRemovedByNt()
    {
        Assert.Empty(DeFixtures.MinimalDe01().Descendants().Where(e => DeReferenceStructure.RemovedByNt.ContainsKey(e.Name.LocalName)));
    }

    [Fact]
    public void Fixture_IsTipo01_WithTimbradoAndCdcInId()
    {
        var de = De(DeFixtures.MinimalDe01());
        Assert.Equal("1", V(de, "gTimb", "iTiDE"));
        Assert.Equal("12345678", V(de, "gTimb", "dNumTim"));
        Assert.Equal(DeFixtures.Cdc, de.Attribute("Id")!.Value);
        Assert.True(CdcGenerator.ValidateCDC(DeFixtures.Cdc));
        Assert.Equal(DeFixtures.Cdc[^1].ToString(), V(de, "dDVId"));
    }

    [Fact]
    public void Fixture_CdcComponentsMatchFields()
    {
        var de = De(DeFixtures.MinimalDe01());
        var cdc = DeFixtures.Cdc;
        Assert.Equal("01", cdc[..2]);
        Assert.Equal(V(de, "gDatGralOpe", "gEmis", "dRucEm"), cdc[2..10]);
        Assert.Equal(V(de, "gDatGralOpe", "gEmis", "dDVEmi"), cdc[10..11]);
        Assert.Equal(V(de, "gTimb", "dEst"), cdc[11..14]);
        Assert.Equal(V(de, "gTimb", "dPunExp"), cdc[14..17]);
        Assert.Equal(V(de, "gTimb", "dNumDoc").PadLeft(7, '0'), cdc[17..24]);
        Assert.Equal(V(de, "gDatGralOpe", "gEmis", "iTipCont"), cdc[24..25]);
        Assert.Equal("20200507", cdc[25..33]);
        Assert.Equal(V(de, "gDatGralOpe", "dFeEmiDE")[..10].Replace("-", ""), cdc[25..33]);
        Assert.Equal(V(de, "gOpeDE", "iTipEmi"), cdc[33..34]);
        Assert.Equal(V(de, "gOpeDE", "dCodSeg"), cdc[34..43]);
    }

    [Fact]
    public void Fixture_OperationAndReceiver()
    {
        var g = De(DeFixtures.MinimalDe01()).Element(DeFixtures.Ns + "gDatGralOpe")!;
        Assert.Equal("PYG", V(g, "gOpeCom", "cMoneOpe"));
        Assert.Equal("1", V(g, "gOpeCom", "iTImp"));
        Assert.Equal("1", V(g, "gDatRec", "iNatRec"));
        Assert.Equal("80000002", V(g, "gDatRec", "dRucRec"));
    }

    [Fact]
    public void Fixture_TwoItems_Iva10_TotalsConsistent()
    {
        var de = De(DeFixtures.MinimalDe01());
        var items = de.Element(DeFixtures.Ns + "gDtipDE")!.Elements(DeFixtures.Ns + "gCamItem").ToList();
        Assert.Equal(2, items.Count);
        foreach (var i in items)
        {
            Assert.Equal("10", V(i, "gCamIVA", "dTasaIVA"));
            Assert.Equal(1100000m, decimal.Parse(V(i, "gValorItem", "gValorRestaItem", "dTotOpeItem")));
            // base = total / 1,1 ; liquidacion = base * 10% (Manual v150 E735/E736)
            Assert.Equal(1000000m, decimal.Parse(V(i, "gCamIVA", "dBasGravIVA")));
            Assert.Equal(100000m, decimal.Parse(V(i, "gCamIVA", "dLiqIVAItem")));
            Assert.Equal("0", V(i, "gCamIVA", "dBasExe"));
        }

        var t = de.Element(DeFixtures.Ns + "gTotSub")!;
        Assert.Equal(2200000m, decimal.Parse(V(t, "dSub10")));
        Assert.Equal(2200000m, decimal.Parse(V(t, "dTotGralOpe")));
        Assert.Equal(200000m, decimal.Parse(V(t, "dIVA10")));
        Assert.Equal(200000m, decimal.Parse(V(t, "dTotIVA")));
        Assert.Equal(2000000m, decimal.Parse(V(t, "dBaseGrav10")));
    }

    [Fact]
    public void Fixture_Contado_HasPaymentEntry_AmountEqualsTotal()
    {
        var cond = De(DeFixtures.MinimalDe01()).Element(DeFixtures.Ns + "gDtipDE")!.Element(DeFixtures.Ns + "gCamCond")!;
        Assert.Equal("1", V(cond, "iCondOpe"));
        Assert.Equal("1", V(cond, "gPaConEIni", "iTiPago"));
        Assert.Equal("PYG", V(cond, "gPaConEIni", "cMoneTiPag"));
        Assert.Equal("2200000", V(cond, "gPaConEIni", "dMonTiPag"));
    }

    [Fact]
    public void Checker_Rejects_RemovedElement_WrongOrder_MissingMandatory()
    {
        var withRemoved = DeFixtures.MinimalDe01();
        De(withRemoved).Element(DeFixtures.Ns + "dFecFirma")!.AddAfterSelf(new XElement(DeFixtures.Ns + "dSisFact", "1"));
        Assert.Contains(DeConformance.Check(withRemoved), e => e.Contains("dSisFact"));

        var wrongOrder = DeFixtures.MinimalDe01();
        var gTimb = De(wrongOrder).Element(DeFixtures.Ns + "gTimb")!;
        gTimb.Element(DeFixtures.Ns + "dEst")!.AddBeforeSelf(gTimb.Element(DeFixtures.Ns + "dNumDoc")!.Extract());
        Assert.Contains(DeConformance.Check(wrongOrder), e => e.Contains("fuera de orden"));

        var missing = DeFixtures.MinimalDe01();
        De(missing).Descendants(DeFixtures.Ns + "dBasExe").First().Remove();
        Assert.Contains(DeConformance.Check(missing), e => e.Contains("dBasExe"));
    }
}

internal static class XElementExt
{
    public static XElement Extract(this XElement e) { e.Remove(); return e; }
}
