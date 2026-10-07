using System.Xml.Linq;

namespace SifenInvoicing.Tests.XmlDe;

/// <summary>
/// DE01 minimo de REFERENCIA escrito a mano (Test, PYG, IVA 10%, 2 items, receptor simple, contado). Es un fixture de
/// prueba, NO un builder: no prueba que SIFEN lo acepte (PENDIENTE DE PRUEBA SIFEN) ni que valide contra el XSD.
/// El CDC usa los componentes de la muestra oficial pero con el DV recalculado (mod 11, Manual 10.1): la muestra trae DV 1 y el algoritmo del Manual da 9. Sin firma ni gCamFuFD (se agregan despues).
/// </summary>
public static class DeFixtures
{
    public const string Cdc = "01000000019001001100005022020050710000000239";

    public static readonly XNamespace Ns = DeReferenceStructure.Namespace;

    public static string MinimalDe01Xml() => $"""
<?xml version="1.0" encoding="UTF-8"?>
<rDE xmlns="{DeReferenceStructure.Namespace}" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xsi:schemaLocation="{DeReferenceStructure.Namespace} siRecepDE_v150.xsd"><dVerFor>150</dVerFor><DE Id="{Cdc}"><dDVId>9</dDVId><dFecFirma>2020-05-07T15:04:10</dFecFirma><gOpeDE><iTipEmi>1</iTipEmi><dDesTipEmi>Normal</dDesTipEmi><dCodSeg>000000023</dCodSeg></gOpeDE><gTimb><iTiDE>1</iTiDE><dDesTiDE>Factura electrónica</dDesTiDE><dNumTim>12345678</dNumTim><dEst>001</dEst><dPunExp>001</dPunExp><dNumDoc>1000050</dNumDoc><dFeIniT>2019-08-13</dFeIniT></gTimb><gDatGralOpe><dFeEmiDE>2020-05-07T15:03:57</dFeEmiDE><gOpeCom><iTipTra>1</iTipTra><dDesTipTra>Venta de mercadería</dDesTipTra><iTImp>1</iTImp><dDesTImp>IVA</dDesTImp><cMoneOpe>PYG</cMoneOpe><dDesMoneOpe>Guarani</dDesMoneOpe></gOpeCom><gEmis><dRucEm>00000001</dRucEm><dDVEmi>9</dDVEmi><iTipCont>2</iTipCont><cTipReg>3</cTipReg><dNomEmi>DE generado en ambiente de prueba - sin valor comercial ni fiscal</dNomEmi><dDirEmi>CALLE 1 CASI CALLE 2</dDirEmi><dNumCas>0</dNumCas><cDepEmi>1</cDepEmi><dDesDepEmi>CAPITAL</dDesDepEmi><cCiuEmi>1</cCiuEmi><dDesCiuEmi>ASUNCION (DISTRITO)</dDesCiuEmi><dTelEmi>012123456</dTelEmi><dEmailE>correo@correo.com</dEmailE><gActEco><cActEco>46510</cActEco><dDesActEco>COMERCIO AL POR MAYOR DE EQUIPOS INFORMÁTICOS Y SOFTWARE</dDesActEco></gActEco></gEmis><gDatRec><iNatRec>1</iNatRec><iTiOpe>1</iTiOpe><cPaisRec>PRY</cPaisRec><dDesPaisRe>Paraguay</dDesPaisRe><iTiContRec>2</iTiContRec><dRucRec>00000002</dRucRec><dDVRec>7</dDVRec><dNomRec>RECEPTOR DEL DOCUMENTO</dNomRec></gDatRec></gDatGralOpe><gDtipDE><gCamFE><iIndPres>1</iIndPres><dDesIndPres>Operación presencial</dDesIndPres></gCamFE><gCamCond><iCondOpe>1</iCondOpe><dDCondOpe>Contado</dDCondOpe><gPaConEIni><iTiPago>1</iTiPago><dDesTiPag>Efectivo</dDesTiPag><dMonTiPag>2200000</dMonTiPag><cMoneTiPag>PYG</cMoneTiPag><dDMoneTiPag>Guarani</dDMoneTiPag></gPaConEIni></gCamCond>{Item("A1", "ITEM UNO")}{Item("A2", "ITEM DOS")}</gDtipDE><gTotSub><dSubExe>0</dSubExe><dSubExo>0</dSubExo><dSub5>0</dSub5><dSub10>2200000</dSub10><dTotOpe>2200000</dTotOpe><dTotDesc>0</dTotDesc><dTotDescGlotem>0</dTotDescGlotem><dTotAntItem>0</dTotAntItem><dTotAnt>0</dTotAnt><dPorcDescTotal>0</dPorcDescTotal><dDescTotal>0</dDescTotal><dAnticipo>0</dAnticipo><dRedon>0</dRedon><dTotGralOpe>2200000</dTotGralOpe><dIVA5>0</dIVA5><dIVA10>200000</dIVA10><dTotIVA>200000</dTotIVA><dBaseGrav5>0</dBaseGrav5><dBaseGrav10>2000000</dBaseGrav10><dTBasGraIVA>2000000</dTBasGraIVA></gTotSub></DE></rDE>
""";

    /// <summary>
    /// Forma firmada con valores de relleno (base64 ficticio): sirve SOLO para validar estructura contra el XSD.
    /// No es una firma real ni un QR real.
    /// </summary>
    public static XDocument MinimalDe01WithPlaceholderSignature()
    {
        var doc = MinimalDe01();
        XNamespace ds = DeReferenceStructure.DsigNamespace;
        var sig = new XElement(ds + "Signature",
            new XElement(ds + "SignedInfo",
                new XElement(ds + "CanonicalizationMethod", new XAttribute("Algorithm", "http://www.w3.org/2001/10/xml-exc-c14n#")),
                new XElement(ds + "SignatureMethod", new XAttribute("Algorithm", "http://www.w3.org/2001/04/xmldsig-more#rsa-sha256")),
                new XElement(ds + "Reference", new XAttribute("URI", "#" + Cdc),
                    new XElement(ds + "Transforms", new XElement(ds + "Transform", new XAttribute("Algorithm", "http://www.w3.org/2000/09/xmldsig#enveloped-signature"))),
                    new XElement(ds + "DigestMethod", new XAttribute("Algorithm", "http://www.w3.org/2001/04/xmlenc#sha256")),
                    new XElement(ds + "DigestValue", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA="))),
            new XElement(ds + "SignatureValue", "AAAA"),
            new XElement(ds + "KeyInfo", new XElement(ds + "X509Data", new XElement(ds + "X509Certificate", "AAAA"))));
        doc.Root!.Element(Ns + "DE")!.AddAfterSelf(sig);
        sig.AddAfterSelf(new XElement(Ns + "gCamFuFD", new XElement(Ns + "dCarQR", "https://ekuatia.set.gov.py/consultas-test/qr?nVersion=150")));
        return doc;
    }

    private static string Item(string code, string desc) =>
        $"<gCamItem><dCodInt>{code}</dCodInt><dDesProSer>{desc}</dDesProSer><cUniMed>77</cUniMed><dDesUniMed>UNI</dDesUniMed><dCantProSer>1</dCantProSer><gValorItem><dPUniProSer>1100000</dPUniProSer><dTotBruOpeItem>1100000</dTotBruOpeItem><gValorRestaItem><dDescItem>0</dDescItem><dPorcDesIt>0</dPorcDesIt><dDescGloItem>0</dDescGloItem><dTotOpeItem>1100000</dTotOpeItem></gValorRestaItem></gValorItem><gCamIVA><iAfecIVA>1</iAfecIVA><dDesAfecIVA>Gravado IVA</dDesAfecIVA><dPropIVA>100</dPropIVA><dTasaIVA>10</dTasaIVA><dBasGravIVA>1000000</dBasGravIVA><dLiqIVAItem>100000</dLiqIVAItem><dBasExe>0</dBasExe></gCamIVA></gCamItem>";

    public static XDocument MinimalDe01() => XDocument.Parse(MinimalDe01Xml());

    public static string SamplePath => Path.Combine(AppContext.BaseDirectory, "XmlDe", "Samples", "official-estructura-de.xml");
}
