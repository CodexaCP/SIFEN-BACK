using System.Globalization;
using System.Xml.Linq;
using SifenInvoicing.Application.Cdc;
using SifenInvoicing.Application.Fiscal;
using SifenInvoicing.Domain.Common;

namespace SifenInvoicing.Application.XmlDe;

/// <summary>
/// Verificacion posterior al builder (Fase 4.3): el XML que se va a persistir usa exactamente el CDC del documento
/// (DE@Id = CDC, dDVId = ultimo caracter del CDC, CDC valido) y sus totales coinciden con los de FiscalCalculationEngine.
/// No recalcula nada: compara.
/// </summary>
public static class SifenDeXmlIntegrity
{
    private static readonly XNamespace Ns = "http://ekuatia.set.gov.py/sifen/xsd";

    public static void Verify(string xml, string expectedCdc, FiscalDocumentModel fiscal)
    {
        ArgumentNullException.ThrowIfNull(fiscal);

        if (!CdcGenerator.ValidateCDC(expectedCdc))
        {
            throw new DomainException("El CDC del documento no es valido.");
        }

        XDocument document;
        try
        {
            document = XDocument.Parse(xml);
        }
        catch (System.Xml.XmlException)
        {
            throw new DomainException("El DE generado no es XML bien formado.");
        }

        var root = document.Root;
        if (root?.Name != Ns + "rDE")
        {
            throw new DomainException("El DE generado debe tener raiz rDE en el namespace oficial de SIFEN.");
        }

        var de = root.Element(Ns + "DE")
            ?? throw new DomainException("El DE generado no contiene el elemento DE.");

        if (!string.Equals(de.Attribute("Id")?.Value, expectedCdc, StringComparison.Ordinal))
        {
            throw new DomainException("DE@Id no coincide con el CDC del documento.");
        }

        if (!string.Equals(de.Element(Ns + "dDVId")?.Value, expectedCdc[^1].ToString(), StringComparison.Ordinal))
        {
            throw new DomainException("dDVId no coincide con el digito verificador del CDC.");
        }

        var totals = de.Element(Ns + "gTotSub")
            ?? throw new DomainException("El DE generado no contiene gTotSub.");
        var t = fiscal.Totals;
        Compare(totals, "dSubExe", t.SubExento, optionalZero: true);
        Compare(totals, "dSub5", t.Sub5, optionalZero: true);
        Compare(totals, "dSub10", t.Sub10, optionalZero: true);
        Compare(totals, "dTotOpe", t.TotalOperacion);
        Compare(totals, "dTotGralOpe", t.TotalGeneral);
        Compare(totals, "dTotIVA", t.TotalIva);
    }

    private static void Compare(XElement totals, string name, decimal expected, bool optionalZero = false)
    {
        var raw = totals.Element(Ns + name)?.Value;
        if (raw is null && optionalZero && expected == 0m)
        {
            return;
        }

        if (!decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var actual))
        {
            throw new DomainException($"El DE generado debe contener el total numerico {name}.");
        }

        if (Math.Round(actual, 2, MidpointRounding.AwayFromZero) != Math.Round(expected, 2, MidpointRounding.AwayFromZero))
        {
            throw new DomainException($"El total {name} del DE no coincide con el calculo fiscal.");
        }
    }
}
