namespace SifenInvoicing.Tests.XmlDe;

/// <summary>
/// FASE 4.0 - Especificacion de referencia del DE tipo 01 (solo para pruebas; NO es codigo de produccion).
/// Orden y cardinalidad: Manual Tecnico v150 (tablas) + muestra oficial "Estructura xml_DE" + NT-10/13/16.
/// PENDIENTE DE XSD: el orden aqui es el de la muestra oficial; debe confirmarse contra DE_v150.xsd (no disponible).
/// Cubre solo los grupos del DE01 minimo (Test, PYG, IVA 10%, 2 items, receptor simple, contado).
/// </summary>
public static class DeReferenceStructure
{
    public const string Namespace = "http://ekuatia.set.gov.py/sifen/xsd";
    public const string DsigNamespace = "http://www.w3.org/2000/09/xmldsig#";
    public const string Version = "150";

    /// <summary>Hijos permitidos, en orden (subconjunto cubierto por el DE01 minimo).</summary>
    public static readonly IReadOnlyDictionary<string, string[]> Order = new Dictionary<string, string[]>
    {
        ["rDE"] = new[] { "dVerFor", "DE", "Signature", "gCamFuFD" },
        ["DE"] = new[] { "dDVId", "dFecFirma", "gOpeDE", "gTimb", "gDatGralOpe", "gDtipDE", "gTotSub" },
        ["gOpeDE"] = new[] { "iTipEmi", "dDesTipEmi", "dCodSeg", "dInfoEmi", "dInfoFisc" },
        ["gTimb"] = new[] { "iTiDE", "dDesTiDE", "dNumTim", "dEst", "dPunExp", "dNumDoc", "dSerieNum", "dFeIniT" },
        ["gDatGralOpe"] = new[] { "dFeEmiDE", "gOpeCom", "gEmis", "gDatRec" },
        ["gOpeCom"] = new[] { "iTipTra", "dDesTipTra", "iTImp", "dDesTImp", "cMoneOpe", "dDesMoneOpe" },
        ["gEmis"] = new[]
        {
            "dRucEm", "dDVEmi", "iTipCont", "cTipReg", "dNomEmi", "dNomFanEmi", "dDirEmi", "dNumCas", "dCompDir1",
            "dCompDir2", "cDepEmi", "dDesDepEmi", "cDisEmi", "dDesDisEmi", "cCiuEmi", "dDesCiuEmi", "dTelEmi",
            "dEmailE", "dDenSuc", "gActEco", "gRespDE",
        },
        ["gActEco"] = new[] { "cActEco", "dDesActEco" },
        ["gDatRec"] = new[]
        {
            "iNatRec", "iTiOpe", "cPaisRec", "dDesPaisRe", "iTiContRec", "dRucRec", "dDVRec", "iTipIDRec",
            "dDTipIDRec", "dNumIDRec", "dNomRec", "dNomFanRec", "dDirRec", "dNumCasRec", "cDepRec", "dDesDepRec",
            "cDisRec", "dDesDisRec", "cCiuRec", "dDesCiuRec", "dTelRec", "dCelRec", "dEmailRec", "dCodCliente",
        },
        ["gDtipDE"] = new[] { "gCamFE", "gCamCond", "gCamItem", "gCamEsp", "gTransp" },
        ["gCamFE"] = new[] { "iIndPres", "dDesIndPres", "dFecEmNR", "gCompPub" },
        ["gCamCond"] = new[] { "iCondOpe", "dDCondOpe", "gPaConEIni", "gPagCred" },
        ["gCamItem"] = new[]
        {
            "dCodInt", "dParAranc", "dNCM", "dDncpG", "dDncpE", "dGtin", "dGtinPq", "dDesProSer", "cUniMed",
            "dDesUniMed", "dCantProSer", "cPaisOrig", "dDesPaisOrig", "dInfItem", "cRelMerc", "dDesRelMerc",
            "dCanQuiMer", "dPorQuiMer", "dCDCAnticipo", "gValorItem", "gCamIVA", "gRasMerc", "gVehNuevo",
        },
        ["gValorItem"] = new[] { "dPUniProSer", "dTotBruOpeItem", "gValorRestaItem" },
        ["gValorRestaItem"] = new[]
        {
            "dDescItem", "dPorcDesIt", "dDescGloItem", "dAntPreUniIt", "dAntGloPreUniIt", "dTotOpeItem",
            "dTotOpeGs",
        },
        ["gCamIVA"] = new[] { "iAfecIVA", "dDesAfecIVA", "dPropIVA", "dTasaIVA", "dBasGravIVA", "dLiqIVAItem", "dBasExe" },
        ["gTotSub"] = new[]
        {
            "dSubExe", "dSubExo", "dSub5", "dSub10", "dTotOpe", "dTotDesc", "dTotDescGlotem", "dTotAntItem",
            "dTotAnt", "dPorcDescTotal", "dDescTotal", "dAnticipo", "dRedon", "dComi", "dTotGralOpe", "dIVA5",
            "dIVA10", "dLiqTotIVA5", "dLiqTotIVA10", "dIVAComi", "dTotIVA", "dBaseGrav5", "dBaseGrav10",
            "dTBasGraIVA", "dTotalGs",
        },
    };

    /// <summary>Obligatorios (1-1) del DE01 minimo dentro de cada grupo (Manual v150; NT-13 agrega dBasExe).</summary>
    public static readonly IReadOnlyDictionary<string, string[]> Required = new Dictionary<string, string[]>
    {
        ["rDE"] = new[] { "dVerFor", "DE", "Signature", "gCamFuFD" },
        ["DE"] = new[] { "dDVId", "dFecFirma", "gOpeDE", "gTimb", "gDatGralOpe", "gDtipDE", "gTotSub" },
        ["gOpeDE"] = new[] { "iTipEmi", "dDesTipEmi", "dCodSeg" },
        ["gTimb"] = new[] { "iTiDE", "dDesTiDE", "dNumTim", "dEst", "dPunExp", "dNumDoc", "dFeIniT" },
        ["gDatGralOpe"] = new[] { "dFeEmiDE", "gOpeCom", "gEmis", "gDatRec" },
        ["gOpeCom"] = new[] { "iTImp", "cMoneOpe", "dDesMoneOpe" },
        ["gCamIVA"] = new[] { "iAfecIVA", "dDesAfecIVA", "dPropIVA", "dTasaIVA", "dBasGravIVA", "dLiqIVAItem", "dBasExe" },
        ["gTotSub"] = new[] { "dSubExe", "dSubExo", "dSub5", "dSub10", "dTotOpe", "dTotDesc", "dTotGralOpe" },
    };

    /// <summary>Elementos retirados por NT (no deben emitirse en el DE01 v150).</summary>
    public static readonly IReadOnlyDictionary<string, string> RemovedByNt = new Dictionary<string, string>
    {
        ["dSisFact"] = "NT-10 (A005 eliminado)",
    };

    /// <summary>Nombres inventados por el generador actual (no existen en el Manual v150).</summary>
    public static readonly string[] InventedByCurrentGenerator = { "gDtipDE/dCodTipoDoc", "dCodTipoDoc" };
}
