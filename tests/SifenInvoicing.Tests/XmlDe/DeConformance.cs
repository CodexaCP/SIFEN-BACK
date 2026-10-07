using System.Xml.Linq;

namespace SifenInvoicing.Tests.XmlDe;

/// <summary>Verificador de forma contra <see cref="DeReferenceStructure"/>. Solo pruebas. No sustituye al XSD oficial.</summary>
public static class DeConformance
{
    public static List<string> Check(XDocument doc, bool allowRemovedByNt = false, bool signed = false)
    {
        var errors = new List<string>();
        var root = doc.Root;
        if (root is null) { errors.Add("sin raiz"); return errors; }
        Walk(root, errors, allowRemovedByNt, signed);
        return errors;
    }

    private static void Walk(XElement element, List<string> errors, bool allowRemoved, bool signed)
    {
        var name = element.Name.LocalName;
        if (name == "Signature") return;

        if (!allowRemoved && DeReferenceStructure.RemovedByNt.TryGetValue(name, out var nt))
        {
            errors.Add($"{name} retirado por {nt}");
        }

        if (!DeReferenceStructure.Order.TryGetValue(name, out var order)) return;

        var children = element.Elements().Select(c => c.Name.LocalName).ToList();
        var lastIndex = -1;
        foreach (var child in children)
        {
            var idx = Array.IndexOf(order, child);
            if (idx < 0)
            {
                if (!(allowRemoved && DeReferenceStructure.RemovedByNt.ContainsKey(child)))
                {
                    errors.Add($"{name}: hijo no permitido '{child}'");
                }
                continue;
            }

            if (idx < lastIndex) errors.Add($"{name}: '{child}' fuera de orden");
            lastIndex = Math.Max(lastIndex, idx);
        }

        if (DeReferenceStructure.Required.TryGetValue(name, out var required))
        {
            foreach (var r in required.Where(r => !children.Contains(r) && (signed || name != "rDE" || r is "dVerFor" or "DE")))
            {
                errors.Add($"{name}: falta obligatorio '{r}'");
            }
        }

        foreach (var child in element.Elements()) Walk(child, errors, allowRemoved, signed);
    }
}
