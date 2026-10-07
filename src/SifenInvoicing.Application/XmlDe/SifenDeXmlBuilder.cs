using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using SifenInvoicing.Application.Cdc;
using SifenInvoicing.Application.Fiscal;
using SifenInvoicing.Application.Invoices;
using SifenInvoicing.Domain.Common;

namespace SifenInvoicing.Application.XmlDe;

/// <summary>
/// Fase 4.2 - construccion del DE tipo 01 (factura electronica) conforme a DE_v150.xsd (orden y nombres), Manual v150 y NT.
/// SOLO transforma: no calcula montos (FiscalCalculationEngine), no genera el CDC (lo recibe ya persistido), no firma,
/// no arma el QR (gCamFuFD) ni transmite. Salida: rDE{dVerFor, DE} (Signature y gCamFuFD se agregan en fases posteriores).
/// Alcance: Test/Produccion, PYG, contado en efectivo, IVA incluido segun el modelo fiscal, sin descuentos ni anticipos.
/// Todo lo fuera de alcance falla de forma explicita en lugar de generar XML parcial.
/// </summary>
public sealed class SifenDeXmlBuilder
{
    public const string Namespace = "http://ekuatia.set.gov.py/sifen/xsd";
    public const string FormatVersion = "150";

    private static readonly XNamespace Ns = Namespace;
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    // Textos de dDes*: son los enumerados del XSD v150 (DE_Types_v150.xsd) / Manual; solo los codigos dentro del alcance.
    private static readonly IReadOnlyDictionary<int, string> TransactionTypes = new Dictionary<int, string>
    {
        [1] = "Venta de mercadería",
        [2] = "Prestación de servicios",
    };

    private static readonly IReadOnlyDictionary<int, string> PresenceIndicators = new Dictionary<int, string>
    {
        [1] = "Operación presencial",
        [2] = "Operación electrónica",
    };

    private static readonly IReadOnlyDictionary<int, string> IdentityDocuments = new Dictionary<int, string>
    {
        [1] = "Cédula paraguaya",
        [2] = "Pasaporte",
        [3] = "Cédula extranjera",
        [4] = "Carnet de residencia",
    };

    private static readonly Regex RucPattern = new("^[1-9][0-9]{2,7}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex DigitsPattern = new("^[0-9]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex IdentityPattern = new("^[0-9A-Za-z\\-]{1,20}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SeriesPattern = new("^[A-Z]{2}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly SifenDeBuilderOptions _options;

    public SifenDeXmlBuilder(SifenDeBuilderOptions? options = null)
    {
        _options = options ?? SifenDeBuilderOptions.Default;
    }

    public SifenDeBuildResult Build(SifenDeBuildInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        Validate(input);

        var cdc = input.Cdc;
        var fiscal = input.Fiscal;
        var totals = fiscal.Totals;

        var de = new XElement(Ns + "DE",
            new XAttribute("Id", cdc),
            new XElement(Ns + "dDVId", cdc[^1].ToString()),
            new XElement(Ns + "dFecFirma", FormatDateTime(input.FechaFirma)));

        if (_options.DSisFact == DSisFactMode.EmitContributorSystem)
        {
            de.Add(new XElement(Ns + "dSisFact", "1"));
        }

        de.Add(
            BuildGOpeDE(cdc),
            BuildGTimb(input.Timbrado),
            BuildGDatGralOpe(input),
            BuildGDtipDE(input),
            BuildGTotSub(totals));

        var root = new XElement(Ns + "rDE", new XElement(Ns + "dVerFor", FormatVersion), de);
        var xml = new XDocument(root).ToString(SaveOptions.DisableFormatting);
        return new SifenDeBuildResult(cdc, xml);
    }

    private static XElement BuildGOpeDE(string cdc) =>
        new(Ns + "gOpeDE",
            new XElement(Ns + "iTipEmi", cdc[33].ToString()),
            new XElement(Ns + "dDesTipEmi", cdc[33] == '1' ? "Normal" : throw new DomainException("Only normal emission (iTipEmi=1) is supported; contingency is out of scope.")),
            new XElement(Ns + "dCodSeg", cdc.Substring(34, 9)));

    private static XElement BuildGTimb(SifenDeStamp stamp)
    {
        var gTimb = new XElement(Ns + "gTimb",
            new XElement(Ns + "iTiDE", "1"),
            new XElement(Ns + "dDesTiDE", "Factura electrónica"),
            new XElement(Ns + "dNumTim", stamp.StampingNumber),
            new XElement(Ns + "dEst", stamp.Establishment),
            new XElement(Ns + "dPunExp", stamp.ExpeditionPoint),
            new XElement(Ns + "dNumDoc", stamp.DocumentNumber));
        if (stamp.Series is not null)
        {
            gTimb.Add(new XElement(Ns + "dSerieNum", stamp.Series));
        }

        gTimb.Add(new XElement(Ns + "dFeIniT", stamp.ValidFrom.ToString("yyyy-MM-dd", Invariant)));
        return gTimb;
    }

    private XElement BuildGDatGralOpe(SifenDeBuildInput input) =>
        new(Ns + "gDatGralOpe",
            new XElement(Ns + "dFeEmiDE", FormatDateTime(input.FechaEmision)),
            new XElement(Ns + "gOpeCom",
                new XElement(Ns + "iTipTra", input.Operacion.TransactionType.ToString(Invariant)),
                new XElement(Ns + "dDesTipTra", TransactionTypes[input.Operacion.TransactionType]),
                new XElement(Ns + "iTImp", "1"),
                new XElement(Ns + "dDesTImp", "IVA"),
                new XElement(Ns + "cMoneOpe", input.Operacion.CurrencyCode),
                new XElement(Ns + "dDesMoneOpe", input.Operacion.CurrencyDescription)),
            BuildGEmis(input.Emisor, input.Ambiente),
            BuildGDatRec(input.Receptor));

    private XElement BuildGEmis(SifenDeEmitter e, SifenDeEnvironment environment)
    {
        var name = environment == SifenDeEnvironment.Test
            ? (_options.TestLegend ?? TestEnvironmentLegend.Default).EmitterNameLiteral
            : e.LegalName.Trim();

        var g = new XElement(Ns + "gEmis",
            new XElement(Ns + "dRucEm", e.Ruc),
            new XElement(Ns + "dDVEmi", e.RucCheckDigit),
            new XElement(Ns + "iTipCont", e.TaxpayerType.ToString(Invariant)),
            new XElement(Ns + "dNomEmi", name),
            new XElement(Ns + "dDirEmi", e.Address.Trim()),
            new XElement(Ns + "dNumCas", e.HouseNumber!.Value.ToString(Invariant)),
            new XElement(Ns + "cDepEmi", e.DepartmentCode!.Value.ToString(Invariant)),
            new XElement(Ns + "dDesDepEmi", e.DepartmentDescription!.Trim()));
        if (e.DistrictCode.HasValue)
        {
            g.Add(
                new XElement(Ns + "cDisEmi", e.DistrictCode.Value.ToString(Invariant)),
                new XElement(Ns + "dDesDisEmi", e.DistrictDescription!.Trim()));
        }

        g.Add(
            new XElement(Ns + "cCiuEmi", e.CityCode!.Value.ToString(Invariant)),
            new XElement(Ns + "dDesCiuEmi", e.CityDescription!.Trim()),
            new XElement(Ns + "dTelEmi", e.Phone!.Trim()),
            new XElement(Ns + "dEmailE", e.Email!.Trim()));
        foreach (var activity in e.EconomicActivities)
        {
            g.Add(new XElement(Ns + "gActEco",
                new XElement(Ns + "cActEco", activity.Code.Trim()),
                new XElement(Ns + "dDesActEco", activity.Description.Trim())));
        }

        return g;
    }

    private static XElement BuildGDatRec(SifenDeReceiver r)
    {
        var g = new XElement(Ns + "gDatRec",
            new XElement(Ns + "iNatRec", ((int)r.Nature).ToString(Invariant)),
            new XElement(Ns + "iTiOpe", ((int)r.OperationType).ToString(Invariant)),
            new XElement(Ns + "cPaisRec", r.CountryCode),
            new XElement(Ns + "dDesPaisRe", r.CountryDescription));

        if (r.Nature == SifenDeReceiverNature.Taxpayer)
        {
            g.Add(
                new XElement(Ns + "iTiContRec", r.TaxpayerKind!.Value.ToString(Invariant)),
                new XElement(Ns + "dRucRec", r.Ruc),
                new XElement(Ns + "dDVRec", r.RucCheckDigit));
        }
        else
        {
            g.Add(
                new XElement(Ns + "iTipIDRec", r.IdentityDocumentType!.Value.ToString(Invariant)),
                new XElement(Ns + "dDTipIDRec", IdentityDocuments[r.IdentityDocumentType.Value]),
                new XElement(Ns + "dNumIDRec", r.IdentityDocumentNumber));
        }

        g.Add(new XElement(Ns + "dNomRec", r.Name.Trim()));
        return g;
    }

    private XElement BuildGDtipDE(SifenDeBuildInput input)
    {
        var totals = input.Fiscal.Totals;
        var legend = (_options.TestLegend ?? TestEnvironmentLegend.Default).FirstItemDescriptionLiteral;
        var g = new XElement(Ns + "gDtipDE",
            new XElement(Ns + "gCamFE",
                new XElement(Ns + "iIndPres", input.Operacion.PresenceIndicator.ToString(Invariant)),
                new XElement(Ns + "dDesIndPres", PresenceIndicators[input.Operacion.PresenceIndicator])),
            new XElement(Ns + "gCamCond",
                new XElement(Ns + "iCondOpe", "1"),
                new XElement(Ns + "dDCondOpe", "Contado"),
                new XElement(Ns + "gPaConEIni",
                    new XElement(Ns + "iTiPago", "1"),
                    new XElement(Ns + "dDesTiPag", "Efectivo"),
                    new XElement(Ns + "dMonTiPag", FormatAmount(totals.TotalGeneral)),
                    new XElement(Ns + "cMoneTiPag", input.Operacion.CurrencyCode),
                    new XElement(Ns + "dDMoneTiPag", input.Operacion.CurrencyDescription))));

        for (var i = 0; i < input.Items.Count; i++)
        {
            var description = i == 0 && input.Ambiente == SifenDeEnvironment.Test && legend is not null
                ? legend
                : input.Items[i].Description.Trim();
            g.Add(BuildGCamItem(input.Items[i], description, input.Fiscal.Lines[i]));
        }

        return g;
    }

    private static XElement BuildGCamItem(SifenDeItem item, string description, FiscalLine line) =>
        new(Ns + "gCamItem",
            new XElement(Ns + "dCodInt", item.Code.Trim()),
            new XElement(Ns + "dDesProSer", description),
            new XElement(Ns + "cUniMed", item.UnitCode.ToString(Invariant)),
            new XElement(Ns + "dDesUniMed", item.UnitDescription.Trim()),
            new XElement(Ns + "dCantProSer", FormatAmount(line.Quantity)),
            new XElement(Ns + "gValorItem",
                new XElement(Ns + "dPUniProSer", FormatAmount(line.UnitPrice)),
                new XElement(Ns + "dTotBruOpeItem", FormatAmount(line.TotalBruto)),
                new XElement(Ns + "gValorRestaItem",
                    // Manual EA002/EA004/EA006/EA007: "si no hay descuento/anticipo completar con 0 (cero)".
                    // dPorcDesIt (EA003) solo debe existir si dDescItem > 0.
                    new XElement(Ns + "dDescItem", "0"),
                    new XElement(Ns + "dDescGloItem", "0"),
                    new XElement(Ns + "dAntPreUniIt", "0"),
                    new XElement(Ns + "dAntGloPreUniIt", "0"),
                    new XElement(Ns + "dTotOpeItem", FormatAmount(line.TotalOperacion)))),
            BuildGCamIva(line));

    private static XElement BuildGCamIva(FiscalLine line) =>
        new(Ns + "gCamIVA",
            new XElement(Ns + "iAfecIVA", line.AfectacionIva.ToString(Invariant)),
            new XElement(Ns + "dDesAfecIVA", line.AfectacionIva == 1 ? "Gravado IVA" : "Exento"),
            new XElement(Ns + "dPropIVA", FormatAmount(line.ProporcionIva)),
            new XElement(Ns + "dTasaIVA", ((int)line.TasaIva).ToString(Invariant)),
            new XElement(Ns + "dBasGravIVA", FormatAmount(line.BaseGravadaIva)),
            new XElement(Ns + "dLiqIVAItem", FormatAmount(line.LiquidacionIva)),
            new XElement(Ns + "dBasExe", FormatAmount(line.BaseExenta)));

    private XElement BuildGTotSub(FiscalTotals t)
    {
        var g = new XElement(Ns + "gTotSub");
        void PerRate(string name, decimal value)
        {
            if (value != 0m || _options.ZeroPolicy == OptionalZeroPolicy.Emit)
            {
                g.Add(new XElement(Ns + name, FormatAmount(value)));
            }
        }

        PerRate("dSubExe", t.SubExento);
        PerRate("dSub5", t.Sub5);
        PerRate("dSub10", t.Sub10);
        g.Add(
            new XElement(Ns + "dTotOpe", FormatAmount(t.TotalOperacion)),
            new XElement(Ns + "dTotDesc", FormatAmount(t.TotalDescuento)),
            // NT-001: obligatorios 1-1. El motor fiscal no modela descuentos globales ni anticipos: cero por alcance.
            new XElement(Ns + "dTotDescGlotem", "0"),
            new XElement(Ns + "dTotAntItem", "0"),
            new XElement(Ns + "dTotAnt", "0"),
            new XElement(Ns + "dPorcDescTotal", "0"),
            new XElement(Ns + "dDescTotal", FormatAmount(t.TotalDescuento)),
            new XElement(Ns + "dAnticipo", "0"),
            new XElement(Ns + "dRedon", FormatAmount(t.Redondeo)),
            new XElement(Ns + "dTotGralOpe", FormatAmount(t.TotalGeneral)));
        PerRate("dIVA5", t.Iva5);
        PerRate("dIVA10", t.Iva10);
        g.Add(new XElement(Ns + "dTotIVA", FormatAmount(t.TotalIva)));
        PerRate("dBaseGrav5", t.BaseGravada5);
        PerRate("dBaseGrav10", t.BaseGravada10);
        g.Add(new XElement(Ns + "dTBasGraIVA", FormatAmount(t.TotalBaseGravada)));
        // dTotalGs: NT-008 / F023 "no debe existir si D015=PYG".
        return g;
    }

    // ---------- validaciones de entrada (fallan antes de producir XML) ----------

    private static void Validate(SifenDeBuildInput input)
    {
        ValidateCdc(input);
        ValidateStamp(input.Timbrado, input.Cdc);
        ValidateEmitter(input.Emisor, input.Cdc);
        ValidateReceiver(input.Receptor);
        ValidateOperationAndFiscal(input);

        if (input.FechaFirma == default || input.FechaEmision == default)
        {
            throw new DomainException("FechaEmision and FechaFirma are required.");
        }

        if (input.FechaEmision.ToString("yyyyMMdd", Invariant) != input.Cdc.Substring(25, 8))
        {
            throw new DomainException("FechaEmision does not match the CDC date.");
        }
    }

    private static void ValidateCdc(SifenDeBuildInput input)
    {
        if (!CdcGenerator.ValidateCDC(input.Cdc))
        {
            throw new DomainException("CDC is invalid (44 digits with a valid modulo 11 check digit are required).");
        }

        if (input.Cdc[..2] != "01")
        {
            throw new DomainException("Only document type 01 (factura electronica) is supported.");
        }
    }

    private static void ValidateStamp(SifenDeStamp s, string cdc)
    {
        Require(s.StampingNumber is { Length: 8 } n && DigitsPattern.IsMatch(n), "dNumTim must contain exactly 8 digits.");
        Require(s.Establishment is { Length: 3 } e && DigitsPattern.IsMatch(e), "dEst must contain exactly 3 digits.");
        Require(s.ExpeditionPoint is { Length: 3 } p && DigitsPattern.IsMatch(p), "dPunExp must contain exactly 3 digits.");
        Require(s.DocumentNumber is { Length: 7 } d && DigitsPattern.IsMatch(d) && d.Any(c => c != '0'), "dNumDoc must contain 7 digits and be greater than zero.");
        Require(s.Series is null || SeriesPattern.IsMatch(s.Series), "dSerieNum must be two uppercase letters when informed.");
        Require(s.ValidFrom >= new DateOnly(2018, 5, 1), "dFeIniT cannot be earlier than 2018-05-01.");
        Require(s.Establishment == cdc.Substring(11, 3), "dEst does not match the CDC.");
        Require(s.ExpeditionPoint == cdc.Substring(14, 3), "dPunExp does not match the CDC.");
        Require(s.DocumentNumber == cdc.Substring(17, 7), "dNumDoc does not match the CDC.");
    }

    private static void ValidateEmitter(SifenDeEmitter e, string cdc)
    {
        ValidateRuc(e.Ruc, e.RucCheckDigit, "dRucEm/dDVEmi");
        Require(e.Ruc.PadLeft(8, '0') == cdc.Substring(2, 8) && e.RucCheckDigit == cdc.Substring(10, 1), "Emitter RUC/DV does not match the CDC.");
        Require(e.TaxpayerType is 1 or 2, "iTipCont must be 1 (persona fisica) or 2 (persona juridica).");
        Require(e.TaxpayerType.ToString(Invariant) == cdc.Substring(24, 1), "iTipCont does not match the CDC.");
        Require(!string.IsNullOrWhiteSpace(e.LegalName) && e.LegalName.Trim().Length is >= 4 and <= 255, "dNomEmi requires 4-255 characters.");
        Require(!string.IsNullOrWhiteSpace(e.Address) && e.Address.Trim().Length <= 255, "dDirEmi requires 1-255 characters.");
        Require(e.HouseNumber is >= 0 and <= 999999, "dNumCas is required (0 when there is no number, up to 6 digits): incomplete taxpayer profile.");
        Require(e.DepartmentCode is >= 1 && !string.IsNullOrWhiteSpace(e.DepartmentDescription), "cDepEmi/dDesDepEmi are required: incomplete taxpayer profile.");
        Require(e.DistrictCode.HasValue == !string.IsNullOrWhiteSpace(e.DistrictDescription), "cDisEmi and dDesDisEmi must be informed together.");
        Require(e.CityCode is >= 1 and <= 99999 && !string.IsNullOrWhiteSpace(e.CityDescription), "cCiuEmi/dDesCiuEmi are required: incomplete taxpayer profile.");
        Require(e.Phone is { } phone && phone.Trim().Length is >= 6 and <= 15, "dTelEmi (6-15 characters) is required: incomplete taxpayer profile.");
        Require(!string.IsNullOrWhiteSpace(e.Email), "dEmailE is required: incomplete taxpayer profile.");
        Require(e.EconomicActivities is { Count: >= 1 and <= 9 }, "gActEco (1-9 economic activities) is required: incomplete taxpayer profile.");
        foreach (var a in e.EconomicActivities)
        {
            Require(!string.IsNullOrWhiteSpace(a.Code) && !string.IsNullOrWhiteSpace(a.Description), "Each economic activity requires code and description.");
        }
    }

    private static void ValidateReceiver(SifenDeReceiver r)
    {
        Require(!string.IsNullOrWhiteSpace(r.Name) && r.Name.Trim().Length is >= 4 and <= 255, "dNomRec requires 4-255 characters.");
        Require(r.CountryCode == "PRY" && r.CountryDescription == "Paraguay", "Only receivers in PRY are supported (B2F is out of scope).");

        if (r.Nature == SifenDeReceiverNature.Taxpayer)
        {
            Require(r.OperationType == SifenDeOperationType.B2B, "A taxpayer receiver requires operation type B2B (B2G is out of scope).");
            Require(r.TaxpayerKind is 1 or 2, "iTiContRec (1 or 2) is required for a taxpayer receiver.");
            ValidateRuc(r.Ruc, r.RucCheckDigit, "dRucRec/dDVRec");
            Require(r.IdentityDocumentType is null && r.IdentityDocumentNumber is null, "A taxpayer receiver must not inform identity document fields.");
        }
        else
        {
            Require(r.OperationType == SifenDeOperationType.B2C, "A non-taxpayer receiver requires operation type B2C.");
            Require(r.TaxpayerKind is null && r.Ruc is null && r.RucCheckDigit is null, "A non-taxpayer receiver must not inform RUC fields.");
            Require(r.IdentityDocumentType is { } t && IdentityDocuments.ContainsKey(t), "iTipIDRec must be 1, 2, 3 or 4 (other types are out of scope).");
            Require(r.IdentityDocumentNumber is { } num && IdentityPattern.IsMatch(num), "dNumIDRec requires 1-20 alphanumeric characters or hyphens.");
        }
    }

    private static void ValidateRuc(string? ruc, string? dv, string field)
    {
        Require(ruc is not null && RucPattern.IsMatch(ruc), $"{field}: RUC must be 3-8 digits without leading zero.");
        Require(dv is { Length: 1 } && char.IsAsciiDigit(dv[0]), $"{field}: check digit must be a single digit.");
        Require(CdcGenerator.CalculateModulo11(ruc!).ToString(Invariant) == dv, $"{field}: check digit does not match modulo 11.");
    }

    private static void ValidateOperationAndFiscal(SifenDeBuildInput input)
    {
        var op = input.Operacion;
        Require(TransactionTypes.ContainsKey(op.TransactionType), "iTipTra must be 1 or 2 (other transaction types are out of scope).");
        Require(PresenceIndicators.ContainsKey(op.PresenceIndicator), "iIndPres must be 1 or 2 (other indicators are out of scope).");
        Require(op.CurrencyCode == "PYG" && op.CurrencyDescription == "Guarani", "Only PYG is supported.");

        var fiscal = input.Fiscal ?? throw new DomainException("Fiscal model is required.");
        Require(fiscal.CurrencyCode == "PYG", "Fiscal model currency must be PYG.");
        Require(fiscal.Lines is { Count: >= 1 }, "At least one item is required.");
        Require(input.Items is { Count: >= 1 } && input.Items.Count == fiscal.Lines.Count, "Each fiscal line requires its item data.");

        for (var i = 0; i < input.Items.Count; i++)
        {
            var item = input.Items[i];
            var line = fiscal.Lines[i];
            Require(!string.IsNullOrWhiteSpace(item.Code) && item.Code.Trim().Length <= 50, $"Item {i + 1}: dCodInt requires 1-50 characters.");
            Require(!string.IsNullOrWhiteSpace(item.Description) && item.Description.Trim().Length <= 2000, $"Item {i + 1}: dDesProSer requires 1-2000 characters.");
            Require(item.UnitCode > 0 && !string.IsNullOrWhiteSpace(item.UnitDescription), $"Item {i + 1}: cUniMed/dDesUniMed are required.");
            Require(line.Quantity > 0, $"Item {i + 1}: quantity must be greater than zero.");
            Require(line.UnitPrice >= 0 && line.TotalOperacion >= 0, $"Item {i + 1}: amounts cannot be negative.");
            Require(line.AfectacionIva is 1 or 3, $"Item {i + 1}: iAfecIVA must be 1 or 3 (other values are out of scope).");
            Require(line.TasaIva == 10m || line.TasaIva == 5m || (line.AfectacionIva == 3 && line.TasaIva == 0m), $"Item {i + 1}: invalid VAT rate.");
            Require(line.AfectacionIva != 1 || line.ProporcionIva == 100m, $"Item {i + 1}: only dPropIVA=100 is supported.");
        }

        var t = fiscal.Totals;
        Require(t.TotalDescuento == 0m, "Discounts are out of scope for the minimal DE01.");
        Require(t.TotalGeneral >= 0, "Total cannot be negative.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new DomainException(message);
        }
    }

    private static string FormatDateTime(DateTime value) => value.ToString("yyyy-MM-ddTHH:mm:ss", Invariant);

    private static string FormatAmount(decimal value) => value.ToString("0.########", Invariant);
}
