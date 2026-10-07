namespace SifenInvoicing.Application.XmlDe;

/// <summary>
/// Manual Tecnico v150, Tabla 5 (Codificacion de unidades de medida): cUniMed = Codigo, dDesUniMed = Representacion
/// ("Utilizar el atributo Codigo", E710). Transcripcion literal; no se agregan unidades que la tabla no contenga.
/// </summary>
public static class SifenDeUnitsOfMeasure
{
    private static readonly Dictionary<int, string> Table = new()
    {
        [87] = "m", [2366] = "CPM", [2329] = "UI", [110] = "M3", [77] = "UNI", [86] = "g", [89] = "LT", [90] = "MG",
        [91] = "CM", [92] = "CM2", [93] = "CM3", [94] = "PUL", [96] = "MM2", [79] = "kg/m²", [97] = "AA", [98] = "ME",
        [99] = "TN", [100] = "Hs", [101] = "Mi", [104] = "DET", [103] = "Ya", [108] = "MT", [109] = "M2", [95] = "MM",
        [666] = "Se", [102] = "Di", [83] = "kg", [88] = "ML", [625] = "Km", [660] = "ml", [885] = "GL", [891] = "pm",
        [869] = "ha", [569] = "ración",
    };

    public static bool TryGetRepresentation(int code, out string representation)
    {
        var found = Table.TryGetValue(code, out var value);
        representation = value ?? string.Empty;
        return found;
    }
}
