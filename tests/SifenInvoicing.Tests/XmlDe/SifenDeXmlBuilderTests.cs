using System.Globalization;
using System.Xml.Linq;
using SifenInvoicing.Application.XmlDe;
using SifenInvoicing.Domain.Common;
using Xunit.Abstractions;

namespace SifenInvoicing.Tests.XmlDe;

/// <summary>
/// Fase 4.2 - SifenDeXmlBuilder (DE01 minimo). Estructura, fuentes y determinismo; la validacion contra el XSD oficial
/// local esta en <see cref="SifenDeXmlBuilderXsdTests"/>. dSisFact queda como contradiccion NT-10 vs XSD (configurable).
/// </summary>
public sealed class SifenDeXmlBuilderTests
{
    private static readonly XNamespace Ns = SifenDeXmlBuilder.Namespace;

    private static SifenDeBuildResult Build(SifenDeBuildInput? input = null, SifenDeBuilderOptions? options = null) =>
        new SifenDeXmlBuilder(options).Build(input ?? SifenDeBuilderFixtures.Input());

    private static XElement De(SifenDeBuildResult r) => XDocument.Parse(r.Xml).Root!.Element(Ns + "DE")!;

    private static string V(XElement e, params string[] path) => path.Aggregate(e, (cur, n) => cur.Element(Ns + n)!).Value;

    [Fact]
    public void Build_UsesReceivedCdc_AsIdAndDv_AndOfficialRootStructure()
    {
        var result = Build();
        var doc = XDocument.Parse(result.Xml);

        Assert.Equal(SifenDeBuilderFixtures.Cdc, result.Cdc);
        Assert.Equal(Ns + "rDE", doc.Root!.Name);
        Assert.Equal(new[] { "dVerFor", "DE" }, doc.Root.Elements().Select(e => e.Name.LocalName));
        Assert.Equal("150", V(doc.Root, "dVerFor"));
        var de = doc.Root.Element(Ns + "DE")!;
        Assert.Equal(SifenDeBuilderFixtures.Cdc, de.Attribute("Id")!.Value);
        Assert.Equal(SifenDeBuilderFixtures.Cdc[^1].ToString(), V(de, "dDVId"));
        Assert.Equal("2020-05-07T15:04:10", V(de, "dFecFirma"));
        Assert.Equal("2020-05-07T15:03:57", V(de, "gDatGralOpe", "dFeEmiDE"));
    }

    [Fact]
    public void Build_DoesNotEmit_DFeFinT_Signature_Qr_OrLegacyNames()
    {
        var all = XDocument.Parse(Build().Xml).Descendants().Select(e => e.Name.LocalName).ToHashSet();
        foreach (var forbidden in new[]
        {
            "dFeFinT", "Signature", "gCamFuFD", "dCarQR", "gReceptor", "dCodTipoDoc", "xNomEmi", "xNomRec", "xDesProSer",
            "gCamItemDet", "dTotalGs", "dCodMon", "dValTotItem",
        })
        {
            Assert.DoesNotContain(forbidden, all);
        }
    }

    [Fact]
    public void Build_Timbrado_And_OperationFollowCdc()
    {
        var de = De(Build());
        Assert.Equal(new[] { "iTipEmi", "dDesTipEmi", "dCodSeg" }, de.Element(Ns + "gOpeDE")!.Elements().Select(e => e.Name.LocalName));
        Assert.Equal("000000023", V(de, "gOpeDE", "dCodSeg"));
        Assert.Equal(
            new[] { "iTiDE", "dDesTiDE", "dNumTim", "dEst", "dPunExp", "dNumDoc", "dFeIniT" },
            de.Element(Ns + "gTimb")!.Elements().Select(e => e.Name.LocalName));
        Assert.Equal("1", V(de, "gTimb", "iTiDE"));
        Assert.Equal("12345678", V(de, "gTimb", "dNumTim"));
        Assert.Equal("2019-08-13", V(de, "gTimb", "dFeIniT"));
    }

    [Fact]
    public void Build_Series_IsEmittedBetweenNumDocAndFeIniT_WhenInformed()
    {
        var input = SifenDeBuilderFixtures.Input();
        input = input with { Timbrado = input.Timbrado with { Series = "AB" } };
        var names = De(Build(input)).Element(Ns + "gTimb")!.Elements().Select(e => e.Name.LocalName).ToList();
        Assert.Equal("dSerieNum", names[names.IndexOf("dFeIniT") - 1]);
    }

    [Fact]
    public void Build_Emitter_MapsProfileData_AndTestLegendReplacesName()
    {
        var gEmis = De(Build()).Element(Ns + "gDatGralOpe")!.Element(Ns + "gEmis")!;
        Assert.Equal("80000001", V(gEmis, "dRucEm"));
        Assert.Equal("3", V(gEmis, "dDVEmi"));
        Assert.Equal("2", V(gEmis, "iTipCont"));
        Assert.Equal("DE generado en ambiente de prueba - sin valor comercial ni fiscal", V(gEmis, "dNomEmi"));
        Assert.Equal("CALLE 1 CASI CALLE 2", V(gEmis, "dDirEmi"));
        Assert.Equal("0", V(gEmis, "dNumCas"));
        Assert.Equal("1", V(gEmis, "cDepEmi"));
        Assert.Equal("46510", V(gEmis, "gActEco", "cActEco"));
    }

    [Fact]
    public void Build_Production_UsesLegalName_AndNoLegend()
    {
        var input = SifenDeBuilderFixtures.Input() with { Ambiente = SifenDeEnvironment.Production };
        var gEmis = De(Build(input, new SifenDeBuilderOptions(TestLegend: new TestEnvironmentLegend("LEYENDA", "LEYENDA ITEM")))).Element(Ns + "gDatGralOpe")!.Element(Ns + "gEmis")!;
        Assert.Equal("EMPRESA DE PRUEBA S.A.", V(gEmis, "dNomEmi"));
    }

    [Fact]
    public void Build_FirstItemLegend_IsOptionalAndOnlyInTest()
    {
        var withLegend = De(Build(options: new SifenDeBuilderOptions(TestLegend: new TestEnvironmentLegend("X NOMBRE", "DOCUMENTO ELECTRÓNICO SIN VALOR COMERCIAL NI FISCAL - GENERADO EN AMBIENTE DE PRUEBA"))));
        var items = withLegend.Element(Ns + "gDtipDE")!.Elements(Ns + "gCamItem").ToList();
        Assert.StartsWith("DOCUMENTO ELECTRÓNICO", V(items[0], "dDesProSer"));
        Assert.Equal("ITEM DOS", V(items[1], "dDesProSer"));
        var without = De(Build()).Element(Ns + "gDtipDE")!.Elements(Ns + "gCamItem").First();
        Assert.Equal("ITEM UNO", V(without, "dDesProSer"));
    }

    [Fact]
    public void Build_TaxpayerReceiver_UsesRuc_NotIdentityDocument()
    {
        var gDatRec = De(Build()).Element(Ns + "gDatGralOpe")!.Element(Ns + "gDatRec")!;
        Assert.Equal("1", V(gDatRec, "iNatRec"));
        Assert.Equal("1", V(gDatRec, "iTiOpe"));
        Assert.Equal("80000002", V(gDatRec, "dRucRec"));
        Assert.Equal("1", V(gDatRec, "dDVRec"));
        Assert.Null(gDatRec.Element(Ns + "iTipIDRec"));
        Assert.Null(gDatRec.Element(Ns + "dNumIDRec"));
    }

    [Fact]
    public void Build_NonTaxpayerReceiver_UsesIdentityDocument_NotRuc()
    {
        var gDatRec = De(Build(SifenDeBuilderFixtures.Input(SifenDeBuilderFixtures.NonTaxpayerReceiver()))).Element(Ns + "gDatGralOpe")!.Element(Ns + "gDatRec")!;
        Assert.Equal("2", V(gDatRec, "iNatRec"));
        Assert.Equal("2", V(gDatRec, "iTiOpe"));
        Assert.Equal("1", V(gDatRec, "iTipIDRec"));
        Assert.Equal("Cédula paraguaya", V(gDatRec, "dDTipIDRec"));
        Assert.Equal("1234567", V(gDatRec, "dNumIDRec"));
        Assert.Null(gDatRec.Element(Ns + "iTiContRec"));
        Assert.Null(gDatRec.Element(Ns + "dRucRec"));
        Assert.Null(gDatRec.Element(Ns + "dDVRec"));
    }

    [Fact]
    public void Build_Items_ConsumeFiscalModel_WithoutRecalculating()
    {
        var input = SifenDeBuilderFixtures.Input();
        var items = De(Build(input)).Element(Ns + "gDtipDE")!.Elements(Ns + "gCamItem").ToList();
        Assert.Equal(2, items.Count);
        for (var i = 0; i < 2; i++)
        {
            var line = input.Fiscal.Lines[i];
            Assert.Equal(new[] { "dCodInt", "dDesProSer", "cUniMed", "dDesUniMed", "dCantProSer", "gValorItem", "gCamIVA" }, items[i].Elements().Select(e => e.Name.LocalName));
            Assert.Equal(line.Quantity, decimal.Parse(V(items[i], "dCantProSer"), CultureInfo.InvariantCulture));
            Assert.Equal(line.UnitPrice, decimal.Parse(V(items[i], "gValorItem", "dPUniProSer"), CultureInfo.InvariantCulture));
            Assert.Equal(line.TotalOperacion, decimal.Parse(V(items[i], "gValorItem", "gValorRestaItem", "dTotOpeItem"), CultureInfo.InvariantCulture));
            Assert.Equal("10", V(items[i], "gCamIVA", "dTasaIVA"));
            Assert.Equal("100", V(items[i], "gCamIVA", "dPropIVA"));
            Assert.Equal(line.BaseGravadaIva, decimal.Parse(V(items[i], "gCamIVA", "dBasGravIVA"), CultureInfo.InvariantCulture));
            Assert.Equal(line.LiquidacionIva, decimal.Parse(V(items[i], "gCamIVA", "dLiqIVAItem"), CultureInfo.InvariantCulture));
            Assert.Equal("0", V(items[i], "gCamIVA", "dBasExe"));
        }

        Assert.Equal("1000000", V(items[0], "gCamIVA", "dBasGravIVA"));
        Assert.Equal("100000", V(items[0], "gCamIVA", "dLiqIVAItem"));
    }

    [Fact]
    public void Build_GTotSub_HasAllMandatoryNt001Fields_AndEngineValues()
    {
        var totals = De(Build()).Element(Ns + "gTotSub")!;
        foreach (var required in new[] { "dTotOpe", "dTotDesc", "dTotDescGlotem", "dTotAntItem", "dTotAnt", "dPorcDescTotal", "dDescTotal", "dAnticipo", "dRedon", "dTotGralOpe" })
        {
            Assert.NotNull(totals.Element(Ns + required));
        }

        Assert.Equal("2200000", V(totals, "dTotGralOpe"));
        Assert.Equal("2200000", V(totals, "dSub10"));
        Assert.Equal("200000", V(totals, "dIVA10"));
        Assert.Equal("200000", V(totals, "dTotIVA"));
        Assert.Equal("2000000", V(totals, "dBaseGrav10"));
        Assert.Equal("2000000", V(totals, "dTBasGraIVA"));
        Assert.Null(totals.Element(Ns + "dTotalGs"));
    }

    [Fact]
    public void Build_Cash_PaymentEqualsTotalGeneral_InPyg()
    {
        var cond = De(Build()).Element(Ns + "gDtipDE")!.Element(Ns + "gCamCond")!;
        Assert.Equal("1", V(cond, "iCondOpe"));
        Assert.Equal("Contado", V(cond, "dDCondOpe"));
        Assert.Equal("2200000", V(cond, "gPaConEIni", "dMonTiPag"));
        Assert.Equal("PYG", V(cond, "gPaConEIni", "cMoneTiPag"));
    }

    [Fact]
    public void Build_ZeroPolicy_OmitPerRateTotals_DropsZeroValuedPerRateFields()
    {
        var emit = De(Build(options: new SifenDeBuilderOptions(ZeroPolicy: OptionalZeroPolicy.Emit))).Element(Ns + "gTotSub")!;
        Assert.NotNull(emit.Element(Ns + "dSubExe"));
        Assert.NotNull(emit.Element(Ns + "dSub5"));
        var omit = De(Build(options: new SifenDeBuilderOptions(ZeroPolicy: OptionalZeroPolicy.OmitPerRateTotals))).Element(Ns + "gTotSub")!;
        Assert.Null(omit.Element(Ns + "dSubExe"));
        Assert.Null(omit.Element(Ns + "dSub5"));
        Assert.Null(omit.Element(Ns + "dIVA5"));
        Assert.NotNull(omit.Element(Ns + "dSub10"));
        Assert.NotNull(omit.Element(Ns + "dTotIVA"));
    }

    [Fact]
    public void Build_DSisFact_IsConfigurable_DefaultOmitFollowingNt10()
    {
        Assert.Null(De(Build()).Element(Ns + "dSisFact"));
        var emitted = De(Build(options: new SifenDeBuilderOptions(DSisFact: DSisFactMode.EmitContributorSystem)));
        Assert.Equal("1", V(emitted, "dSisFact"));
        Assert.Equal(new[] { "dDVId", "dFecFirma", "dSisFact", "gOpeDE" }, emitted.Elements().Take(4).Select(e => e.Name.LocalName));
    }

    [Fact]
    public void Build_IsDeterministic_ForSameInput()
    {
        Assert.Equal(Build().Xml, Build().Xml);
        Assert.Equal(
            Build(options: new SifenDeBuilderOptions(DSisFact: DSisFactMode.EmitContributorSystem)).Xml,
            Build(options: new SifenDeBuilderOptions(DSisFact: DSisFactMode.EmitContributorSystem)).Xml);
    }

    [Fact]
    public void Build_UsesNoDateWithZone()
    {
        var xml = Build().Xml;
        Assert.DoesNotMatch(@"T\d\d:\d\d:\d\d(Z|[+-]\d\d:\d\d)", xml);
    }

    // ---------- negativos de entrada: el builder falla antes de producir XML ----------

    private static void Rejects(Func<SifenDeBuildInput, SifenDeBuildInput> mutate, string expectedFragment)
    {
        var ex = Assert.Throws<DomainException>(() => new SifenDeXmlBuilder().Build(mutate(SifenDeBuilderFixtures.Input())));
        Assert.Contains(expectedFragment, ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rejects_InvalidCdc()
    {
        Rejects(i => i with { Cdc = i.Cdc[..43] + (i.Cdc[43] == '0' ? '1' : '0') }, "CDC is invalid");
        Rejects(i => i with { Cdc = "123" }, "CDC is invalid");
        Rejects(i => i with { Cdc = "" }, "CDC is invalid");
    }

    [Fact]
    public void Rejects_InvalidEmitterRucAndCheckDigit()
    {
        Rejects(i => i with { Emisor = i.Emisor with { Ruc = "0000001" } }, "RUC");
        Rejects(i => i with { Emisor = i.Emisor with { Ruc = "8000000A" } }, "RUC");
        Rejects(i => i with { Emisor = i.Emisor with { RucCheckDigit = "4" } }, "check digit does not match modulo 11");
        Rejects(i => i with { Emisor = i.Emisor with { RucCheckDigit = "" } }, "check digit");
    }

    [Fact]
    public void Rejects_InvalidReceiverRucAndCheckDigit()
    {
        Rejects(i => i with { Receptor = i.Receptor with { RucCheckDigit = "9" } }, "check digit does not match modulo 11");
        Rejects(i => i with { Receptor = i.Receptor with { Ruc = "12" } }, "RUC");
    }

    [Fact]
    public void Rejects_InvalidDocumentNumberAndStamp()
    {
        Rejects(i => i with { Timbrado = i.Timbrado with { DocumentNumber = "0000000" } }, "dNumDoc");
        Rejects(i => i with { Timbrado = i.Timbrado with { DocumentNumber = "100005" } }, "dNumDoc");
        Rejects(i => i with { Timbrado = i.Timbrado with { DocumentNumber = "1000051" } }, "does not match the CDC");
        Rejects(i => i with { Timbrado = i.Timbrado with { StampingNumber = "1234567" } }, "dNumTim");
        Rejects(i => i with { Timbrado = i.Timbrado with { StampingNumber = "ABCDEFGH" } }, "dNumTim");
        Rejects(i => i with { Timbrado = i.Timbrado with { Establishment = "002" } }, "dEst does not match");
        Rejects(i => i with { Timbrado = i.Timbrado with { ExpeditionPoint = "01" } }, "dPunExp");
        Rejects(i => i with { Timbrado = i.Timbrado with { Series = "a1" } }, "dSerieNum");
        Rejects(i => i with { Timbrado = i.Timbrado with { ValidFrom = new DateOnly(2018, 1, 1) } }, "dFeIniT");
    }

    [Fact]
    public void Rejects_ItemWithoutDescriptionOrCode()
    {
        Rejects(i => i with { Items = new[] { i.Items[0] with { Description = "  " }, i.Items[1] } }, "dDesProSer");
        Rejects(i => i with { Items = new[] { i.Items[0] with { Code = "" }, i.Items[1] } }, "dCodInt");
        Rejects(i => i with { Items = new[] { i.Items[0], i.Items[1] with { Code = new string('X', 51) } } }, "dCodInt");
        Rejects(i => i with { Items = new[] { i.Items[0] } }, "Each fiscal line requires its item data");
    }

    [Fact]
    public void Rejects_InvalidQuantityAndVat()
    {
        Rejects(i => i with { Fiscal = i.Fiscal with { Lines = new[] { i.Fiscal.Lines[0] with { Quantity = 0m }, i.Fiscal.Lines[1] } } }, "quantity");
        Rejects(i => i with { Fiscal = i.Fiscal with { Lines = new[] { i.Fiscal.Lines[0] with { TasaIva = 7m }, i.Fiscal.Lines[1] } } }, "VAT rate");
        Rejects(i => i with { Fiscal = i.Fiscal with { Lines = new[] { i.Fiscal.Lines[0] with { AfectacionIva = 2 }, i.Fiscal.Lines[1] } } }, "iAfecIVA");
    }

    [Fact]
    public void Rejects_InvalidCurrency()
    {
        Rejects(i => i with { Operacion = i.Operacion with { CurrencyCode = "USD" } }, "PYG");
        Rejects(i => i with { Fiscal = i.Fiscal with { CurrencyCode = "USD" } }, "currency");
    }

    [Fact]
    public void Rejects_IncompatibleReceiverBranches()
    {
        Rejects(i => i with { Receptor = i.Receptor with { OperationType = SifenDeOperationType.B2C } }, "B2B");
        Rejects(i => i with { Receptor = i.Receptor with { IdentityDocumentType = 1, IdentityDocumentNumber = "123" } }, "must not inform identity");
        Rejects(i => i with { Receptor = SifenDeBuilderFixtures.NonTaxpayerReceiver() with { Ruc = "80000002", RucCheckDigit = "1" } }, "must not inform RUC");
        Rejects(i => i with { Receptor = SifenDeBuilderFixtures.NonTaxpayerReceiver() with { OperationType = SifenDeOperationType.B2B } }, "B2C");
        Rejects(i => i with { Receptor = SifenDeBuilderFixtures.NonTaxpayerReceiver() with { IdentityDocumentType = 5 } }, "iTipIDRec");
        Rejects(i => i with { Receptor = i.Receptor with { CountryCode = "ARG", CountryDescription = "Argentina" } }, "PRY");
    }

    [Fact]
    public void Rejects_IncompleteEmitterProfile_WithoutInventingValues()
    {
        Rejects(i => i with { Emisor = i.Emisor with { EconomicActivities = Array.Empty<SifenDeEconomicActivity>() } }, "gActEco");
        Rejects(i => i with { Emisor = i.Emisor with { HouseNumber = null } }, "dNumCas");
        Rejects(i => i with { Emisor = i.Emisor with { Phone = null } }, "dTelEmi");
        Rejects(i => i with { Emisor = i.Emisor with { Email = null } }, "dEmailE");
        Rejects(i => i with { Emisor = i.Emisor with { DepartmentCode = null } }, "cDepEmi");
        Rejects(i => i with { Emisor = i.Emisor with { CityCode = null } }, "cCiuEmi");
        Rejects(i => i with { Emisor = i.Emisor with { TaxpayerType = 1 } }, "iTipCont does not match the CDC");
    }

    [Fact]
    public void Rejects_DateNotMatchingCdc()
    {
        Rejects(i => i with { FechaEmision = new DateTime(2020, 5, 8, 10, 0, 0) }, "CDC date");
    }
}

public sealed class SifenDeEmitterMapperTests
{
    private static SifenInvoicing.Domain.Tenants.TaxpayerProfile Profile(bool complete = true)
    {
        var p = SifenInvoicing.Domain.Tenants.TaxpayerProfile.Create(Guid.NewGuid(), "80000001", "3", "EMPRESA DE PRUEBA S.A.");
        p.UpdateFiscalData(2, "CALLE 1 CASI CALLE 2", complete ? "0" : null, complete ? "1" : null, complete ? "CAPITAL" : null, null, null, complete ? "1" : null, complete ? "ASUNCION (DISTRITO)" : null, complete ? "012123456" : null, complete ? "correo@correo.com" : null);
        return p;
    }

    private static readonly SifenDeEconomicActivity[] Activities = { new("46510", "COMERCIO") };

    [Fact]
    public void Map_CompleteProfile_ProducesEmitterThatBuildsTheDe()
    {
        var emitter = SifenDeEmitterMapper.Map(Profile(), Activities);
        Assert.Equal("80000001", emitter.Ruc);
        Assert.Equal(0, emitter.HouseNumber);
        Assert.Equal(1, emitter.DepartmentCode);
        var input = SifenDeBuilderFixtures.Input() with { Emisor = emitter };
        Assert.NotEmpty(new SifenDeXmlBuilder().Build(input).Xml);
    }

    [Fact]
    public void Map_IncompleteProfile_ListsAllMissingFields_AndInventsNothing()
    {
        var ex = Assert.Throws<DomainException>(() => SifenDeEmitterMapper.Map(Profile(complete: false), null));
        foreach (var f in new[] { "dNumCas", "cDepEmi", "cCiuEmi", "dTelEmi", "dEmailE", "gActEco" })
        {
            Assert.Contains(f, ex.Message);
        }
    }

    [Fact]
    public void Map_CompleteProfileWithoutActivities_IsReportedAsIncompleteModel()
    {
        var ex = Assert.Throws<DomainException>(() => SifenDeEmitterMapper.Map(Profile(), null));
        Assert.Contains("gActEco", ex.Message);
    }
}
