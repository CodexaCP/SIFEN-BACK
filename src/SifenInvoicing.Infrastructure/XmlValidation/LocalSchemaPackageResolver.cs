using System.Xml;

namespace SifenInvoicing.Infrastructure.XmlValidation;

/// <summary>
/// Resuelve los schemaLocation absolutos del paquete oficial (https://ekuatia.set.gov.py/sifen/xsd/...) contra la copia
/// local del paquete XSD v150. Nunca sale a la red: cualquier otra referencia remota falla para no ocultar un faltante.
/// </summary>
public sealed class LocalSchemaPackageResolver : XmlUrlResolver
{
    public const string OfficialBaseUrl = "https://ekuatia.set.gov.py/sifen/xsd/";

    private readonly string _directory;

    public LocalSchemaPackageResolver(string directory)
    {
        _directory = Path.GetFullPath(directory);
    }

    public override Uri ResolveUri(Uri? baseUri, string? relativeUri)
    {
        if (relativeUri is not null && relativeUri.StartsWith(OfficialBaseUrl, StringComparison.OrdinalIgnoreCase))
        {
            var fileName = relativeUri[OfficialBaseUrl.Length..];
            if (fileName.Contains('/') || fileName.Contains('\\') || fileName.Contains(".."))
            {
                throw new XmlException($"Referencia XSD no permitida en el paquete local: {relativeUri}");
            }

            return new Uri(Path.Combine(_directory, fileName));
        }

        return base.ResolveUri(baseUri, relativeUri);
    }

    public override object? GetEntity(Uri absoluteUri, string? role, Type? ofObjectToReturn)
    {
        if (!absoluteUri.IsFile)
        {
            throw new XmlException($"Referencia remota no disponible en el paquete local (sin acceso a Internet): {absoluteUri}");
        }

        return base.GetEntity(absoluteUri, role, ofObjectToReturn);
    }
}
