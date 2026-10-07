using System.Collections.Concurrent;
using System.Xml;
using System.Xml.Schema;
using SifenInvoicing.Application.XmlValidation;

namespace SifenInvoicing.Infrastructure.XmlValidation;

/// <summary>
/// Valida un XML contra un XSD raiz y sus dependencias, resueltas SOLO desde el directorio del XSD raiz (paquete local;
/// los schemaLocation absolutos de ekuatia.set.gov.py se mapean a ese directorio, sin Internet). Las advertencias de
/// validacion se tratan como errores: sin ellas un elemento sin declaracion (p. ej. namespace incorrecto) se omite en
/// silencio y el documento "valida" vacio (comprobado en Fase 4.1).
/// </summary>
public sealed class XmlSchemaValidator : IXmlSchemaValidator
{
    private static readonly ConcurrentDictionary<string, XmlSchemaSet> CompiledSets = new(StringComparer.Ordinal);

    public Task<XmlSchemaValidationResult> ValidateAsync(
        string xml,
        string rootSchemaPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            throw new InvalidOperationException("XML content is required.");
        }

        if (string.IsNullOrWhiteSpace(rootSchemaPath))
        {
            throw new InvalidOperationException("Root schema path is required.");
        }

        if (!File.Exists(rootSchemaPath))
        {
            throw new FileNotFoundException("Root schema file was not found.", rootSchemaPath);
        }

        var schemaSet = GetCompiledSet(rootSchemaPath);

        var errors = new List<XmlSchemaValidationError>();
        var settings = new XmlReaderSettings
        {
            ValidationType = ValidationType.Schema,
            Schemas = schemaSet,
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            ValidationFlags = XmlSchemaValidationFlags.ReportValidationWarnings,
        };
        settings.ValidationEventHandler += (_, args) =>
        {
            var exception = args.Exception;
            errors.Add(new XmlSchemaValidationError(
                $"{args.Severity}: {args.Message}",
                exception?.LineNumber.ToString(),
                exception?.LinePosition.ToString()));
        };

        using var stringReader = new StringReader(xml);
        using var reader = XmlReader.Create(stringReader, settings);

        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
        }

        return Task.FromResult(new XmlSchemaValidationResult(errors.Count == 0, errors));
    }

    private static XmlSchemaSet GetCompiledSet(string rootSchemaPath)
    {
        var fullPath = Path.GetFullPath(rootSchemaPath);
        var key = fullPath + "|" + File.GetLastWriteTimeUtc(fullPath).Ticks;

        return CompiledSets.GetOrAdd(key, _ =>
        {
            var set = new XmlSchemaSet
            {
                XmlResolver = new LocalSchemaPackageResolver(Path.GetDirectoryName(fullPath)!),
            };
            set.Add(null, fullPath);
            set.Compile();
            return set;
        });
    }
}
