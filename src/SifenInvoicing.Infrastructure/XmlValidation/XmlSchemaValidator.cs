using System.Xml;
using System.Xml.Schema;
using SifenInvoicing.Application.XmlValidation;

namespace SifenInvoicing.Infrastructure.XmlValidation;

public sealed class XmlSchemaValidator : IXmlSchemaValidator
{
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

        var schemaSet = new XmlSchemaSet();
        schemaSet.Add(null, rootSchemaPath);
        schemaSet.Compile();

        var errors = new List<XmlSchemaValidationError>();
        var settings = new XmlReaderSettings
        {
            ValidationType = ValidationType.Schema,
            Schemas = schemaSet,
            DtdProcessing = DtdProcessing.Prohibit
        };
        settings.ValidationEventHandler += (_, args) =>
        {
            var exception = args.Exception;
            errors.Add(new XmlSchemaValidationError(
                args.Message,
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
}
