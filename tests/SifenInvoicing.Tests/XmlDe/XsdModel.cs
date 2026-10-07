using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;

namespace SifenInvoicing.Tests.XmlDe;

/// <summary>Infraestructura de test (no produccion): ubicacion del paquete XSD, dependencias y lectura del modelo.</summary>
public static class XsdPackage
{
    public const string DirVariable = "SIFEN_XSD_DIR";
    public const string DeRoot = "DE_v150.xsd";
    public const string ReceptionRoot = "siRecepDE_v150.xsd";
    public const string WsReceptionRoot = "WS_SiRecepDE_v150.xsd";
    public const string OfficialBaseUrl = "https://ekuatia.set.gov.py/sifen/xsd/";

    /// <summary>Raices cuyo cierre de dependencias debe estar completo para emitir y transmitir un DE.</summary>
    public static readonly string[] RequiredRoots = { ReceptionRoot, DeRoot, WsReceptionRoot };

    public static string Dir =>
        Environment.GetEnvironmentVariable(DirVariable) is { Length: > 0 } d
            ? d
            : Path.Combine(AppContext.BaseDirectory, "XmlDe", "XsdPackage", "v150");

    public static bool Has(string file) => File.Exists(Path.Combine(Dir, file));

    /// <summary>Referencias xs:import / xs:include / xs:redefine (schemaLocation) de un XSD.</summary>
    public static IReadOnlyList<string> References(string path) =>
        XDocument.Load(path).Descendants()
            .Where(e => e.Name.Namespace == XmlSchema.Namespace && e.Name.LocalName is "import" or "include" or "redefine")
            .Select(e => (string?)e.Attribute("schemaLocation"))
            .Where(l => !string.IsNullOrEmpty(l))
            .Select(l => l!)
            .ToList();

    /// <summary>Cierre transitivo de dependencias. Devuelve (archivo, referenciado_por, existe).</summary>
    public static List<(string File, string? By, bool Exists)> Closure(string dir, string root)
    {
        var result = new List<(string, string?, bool)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<(string File, string? By)>();
        queue.Enqueue((root, null));
        while (queue.Count > 0)
        {
            var (file, by) = queue.Dequeue();
            if (!seen.Add(file)) continue;
            var full = Path.GetFullPath(Path.Combine(dir, file));
            var exists = File.Exists(full);
            result.Add((file, by, exists));
            if (!exists) continue;
            foreach (var r in References(full))
            {
                // las URL absolutas no se resuelven por red: se evalua por nombre de archivo en el paquete
                var rel = r.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? r[(r.LastIndexOf('/') + 1)..] : r;
                var baseDir = Path.GetDirectoryName(full)!;
                queue.Enqueue((Path.GetRelativePath(dir, Path.GetFullPath(Path.Combine(baseDir, rel))), file));
            }
        }
        return result;
    }

    public static XmlSchemaSet Load(string rootFile, List<string>? problems = null)
    {
        var set = new XmlSchemaSet { XmlResolver = new LocalPackageResolver(Dir) };
        set.ValidationEventHandler += (_, e) => problems?.Add(e.Message);
        set.Add(null, Path.Combine(Dir, rootFile));
        set.Compile();
        return set;
    }
}

/// <summary>
/// Resuelve los schemaLocation absolutos del paquete oficial (https://ekuatia.set.gov.py/sifen/xsd/...) contra la
/// copia local descargada. Nunca sale a la red: cualquier otra URL remota falla para no ocultar un faltante.
/// </summary>
public sealed class LocalPackageResolver : XmlUrlResolver
{
    private readonly string _dir;
    public LocalPackageResolver(string dir) => _dir = Path.GetFullPath(dir);

    public override Uri ResolveUri(Uri? baseUri, string? relativeUri)
    {
        if (relativeUri is not null && relativeUri.StartsWith(XsdPackage.OfficialBaseUrl, StringComparison.OrdinalIgnoreCase))
        {
            return new Uri(Path.Combine(_dir, relativeUri[XsdPackage.OfficialBaseUrl.Length..]));
        }

        return base.ResolveUri(baseUri, relativeUri);
    }

    public override object? GetEntity(Uri absoluteUri, string? role, Type? ofObjectToReturn)
    {
        if (!absoluteUri.IsFile) throw new XmlException($"Referencia remota no disponible en el paquete local: {absoluteUri}");
        return base.GetEntity(absoluteUri, role, ofObjectToReturn);
    }
}

/// <summary>Particula hija: nombre, minOccurs, maxOccurs y si proviene de un xs:choice.</summary>
public sealed record XsdChild(string Name, decimal Min, decimal Max, bool InChoice);

public static class XsdModel
{
    /// <summary>Busca (global o local, primera coincidencia) la declaracion de un elemento por nombre.</summary>
    public static XmlSchemaElement? FindElement(XmlSchemaSet set, string name)
    {
        foreach (XmlSchemaElement g in set.GlobalElements.Values)
        {
            if (g.Name == name) return g;
        }

        var visited = new HashSet<XmlSchemaType>();
        foreach (XmlSchemaElement g in set.GlobalElements.Values)
        {
            var found = Search(g, name, visited);
            if (found is not null) return found;
        }

        return null;
    }

    private static XmlSchemaElement? Search(XmlSchemaElement element, string name, HashSet<XmlSchemaType> visited)
    {
        if (element.ElementSchemaType is not XmlSchemaComplexType ct || !visited.Add(ct)) return null;
        foreach (var child in Particles(ct.ContentTypeParticle, false))
        {
            if (child.Element.QualifiedName.Name == name) return child.Element;
            var found = Search(child.Element, name, visited);
            if (found is not null) return found;
        }

        return null;
    }

    /// <summary>Hijos en el orden impuesto por el XSD (aplana sequence/choice/all).</summary>
    public static List<XsdChild> Children(XmlSchemaElement element) =>
        element.ElementSchemaType is XmlSchemaComplexType ct
            ? Particles(ct.ContentTypeParticle, false)
                .Select(p => new XsdChild(p.Element.QualifiedName.Name, p.Min, p.Max, p.InChoice)).ToList()
            : new List<XsdChild>();

    private static IEnumerable<(XmlSchemaElement Element, decimal Min, decimal Max, bool InChoice)> Particles(
        XmlSchemaParticle? particle, bool inChoice, decimal min = 1, decimal max = 1)
    {
        switch (particle)
        {
            case XmlSchemaElement e:
                yield return (e, min * e.MinOccurs, Mul(max, e.MaxOccurs), inChoice);
                break;
            case XmlSchemaGroupBase g:
                var choice = inChoice || g is XmlSchemaChoice;
                foreach (var item in g.Items.OfType<XmlSchemaParticle>())
                {
                    foreach (var r in Particles(item, choice, min * g.MinOccurs, Mul(max, g.MaxOccurs))) yield return r;
                }

                break;
        }
    }

    private static decimal Mul(decimal a, decimal b) => a == decimal.MaxValue || b == decimal.MaxValue ? decimal.MaxValue : a * b;

    /// <summary>Facetas del tipo simple (longitud, patron, enumeracion, digitos) para documentar restricciones reales.</summary>
    public static Dictionary<string, List<string>> Facets(XmlSchemaElement element)
    {
        var result = new Dictionary<string, List<string>>();
        var type = element.ElementSchemaType;
        var st = type as XmlSchemaSimpleType;
        for (var t = st; t is not null; t = t.BaseXmlSchemaType as XmlSchemaSimpleType)
        {
            if (t.Content is XmlSchemaSimpleTypeRestriction r)
            {
                foreach (var f in r.Facets.OfType<XmlSchemaFacet>())
                {
                    var key = f.GetType().Name.Replace("XmlSchema", "");
                    if (!result.TryGetValue(key, out var list)) result[key] = list = new List<string>();
                    list.Add(f.Value ?? "");
                }
            }
        }

        result["Base"] = new List<string> { element.ElementSchemaType?.QualifiedName.Name is { Length: > 0 } n ? n : "(anonimo)" };
        return result;
    }
}
