using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SifenInvoicing.Application.Invoices;
using SifenInvoicing.Application.XmlValidation;
using SifenInvoicing.Domain.Common;
using SifenInvoicing.Domain.Tenants;
using SifenInvoicing.Infrastructure.Persistence;

namespace SifenInvoicing.Infrastructure.Invoices;

public sealed class FacturaXmlPreSubmissionValidator : IFacturaXmlPreSubmissionValidator
{
    private static readonly XNamespace SifenNamespace = "http://ekuatia.set.gov.py/sifen/xsd";
    private readonly IXmlSchemaValidator _xmlSchemaValidator;
    private readonly IConfiguration _configuration;
    private readonly SifenDbContext _dbContext;

    public FacturaXmlPreSubmissionValidator(
        IXmlSchemaValidator xmlSchemaValidator,
        IConfiguration configuration,
        SifenDbContext dbContext)
    {
        _xmlSchemaValidator = xmlSchemaValidator;
        _configuration = configuration;
        _dbContext = dbContext;
    }

    public async Task ValidateTipoDoc01Async(
        string xml,
        string cdc,
        Guid tenantId,
        SifenEnvironmentType environment,
        CancellationToken cancellationToken = default)
    {
        var document = LoadDocument(xml);
        ValidateMinimumStructure(document, cdc);

        var tenantSettings = await _dbContext.TenantSifenSettings
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(item =>
                item.TenantId == tenantId &&
                item.Environment == environment &&
                item.IsActive,
                cancellationToken);

        var schemaPath = tenantSettings?.XmlSchemaRootPath;
        if (string.IsNullOrWhiteSpace(schemaPath))
        {
            schemaPath = _configuration["Sifen:XmlSchemas:Invoice01RootPath"];
        }

        if (string.IsNullOrWhiteSpace(schemaPath))
        {
            throw new DomainException(
                "TODO: Official XSD path for FE TipoDoc 01 is not configured. XML validation must be completed before signing or sending.");
        }

        XmlSchemaValidationResult validationResult;
        try
        {
            validationResult = await _xmlSchemaValidator.ValidateAsync(xml, schemaPath, cancellationToken);
        }
        catch (FileNotFoundException)
        {
            throw new DomainException(
                $"TODO: Official XSD file for FE TipoDoc 01 was not found at '{schemaPath}'. XML validation must be completed before signing or sending.");
        }

        if (validationResult.IsValid)
        {
            return;
        }

        var details = string.Join("; ", validationResult.Errors
            .Take(3)
            .Select(error => error.Message));

        throw new DomainException(
            $"Generated FE TipoDoc 01 XML failed XSD validation. {details}");
    }

    private static XDocument LoadDocument(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            throw new DomainException("Generated FE TipoDoc 01 XML is empty.");
        }

        try
        {
            return XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        }
        catch (Exception ex) when (ex is not DomainException)
        {
            throw new DomainException("Generated FE TipoDoc 01 XML is not well-formed.");
        }
    }

    private static void ValidateMinimumStructure(XDocument document, string cdc)
    {
        var root = document.Root;
        if (root?.Name != SifenNamespace + "rDE")
        {
            throw new DomainException("Generated FE TipoDoc 01 XML must contain root rDE.");
        }

        if (string.IsNullOrWhiteSpace(root.Element(SifenNamespace + "dVerFor")?.Value))
        {
            throw new DomainException("Generated FE TipoDoc 01 XML must contain dVerFor.");
        }

        var de = root.Element(SifenNamespace + "DE");
        if (de is null)
        {
            throw new DomainException("Generated FE TipoDoc 01 XML must contain DE.");
        }

        if (!string.Equals(de.Attribute("Id")?.Value, cdc, StringComparison.Ordinal))
        {
            throw new DomainException("Generated FE TipoDoc 01 XML must contain DE Id matching the CDC.");
        }

        var tipoDoc = de.Element(SifenNamespace + "gDtipDE")?.Element(SifenNamespace + "dCodTipoDoc")?.Value?.Trim();
        if (!string.Equals(tipoDoc, "01", StringComparison.Ordinal))
        {
            throw new DomainException("Generated FE TipoDoc 01 XML must contain dCodTipoDoc = 01.");
        }
    }
}
