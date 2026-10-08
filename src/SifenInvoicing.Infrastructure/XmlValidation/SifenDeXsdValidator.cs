using System.Xml.Linq;
using Microsoft.Extensions.Configuration;
using SifenInvoicing.Application.XmlDe;
using SifenInvoicing.Application.XmlValidation;
using SifenInvoicing.Domain.Common;

namespace SifenInvoicing.Infrastructure.XmlValidation;

/// <summary>
/// Validacion XSD local del DE01 (Fase 4.3). Raiz: siRecepDE_v150.xsd (declara rDE y arrastra DE_v150.xsd). El builder
/// emite rDE{dVerFor, DE} sin firma, y el XSD exige Signature y gCamFuFD (1..1): para validar se agrega a una COPIA
/// una Signature y un gCamFuFD de relleno, estructuralmente validos y SIN valor criptografico ni fiscal. La copia nunca se
/// persiste ni se envia. La firma y el QR reales son de fases posteriores.
/// </summary>
public sealed class SifenDeXsdValidator : ISifenDeXsdValidator
{
    public const string RootSchemaFile = "siRecepDE_v150.xsd";
    public const string ConfigurationKey = "Sifen:XmlSchemas:De01PackageDirectory";

    private static readonly XNamespace Sifen = "http://ekuatia.set.gov.py/sifen/xsd";
    private static readonly XNamespace Ds = "http://www.w3.org/2000/09/xmldsig#";

    private readonly IXmlSchemaValidator _validator;
    private readonly string _packageDirectory;

    public SifenDeXsdValidator(IXmlSchemaValidator validator, IConfiguration configuration)
    {
        _validator = validator;
        var configured = configuration[ConfigurationKey];
        _packageDirectory = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(AppContext.BaseDirectory, "Schemas", "sifen", "v150")
            : configured;
    }

    public Task EnsureValidAsync(string unsignedDeXml, CancellationToken cancellationToken = default)
        => ValidateAsync(WithPlaceholders(unsignedDeXml), "El DE01 generado", cancellationToken);

    public Task EnsureSignedValidAsync(string signedDeXml, CancellationToken cancellationToken = default)
        => ValidateAsync(WithQrPlaceholder(signedDeXml), "El DE01 firmado", cancellationToken);

    public Task EnsureFinalValidAsync(string finalDeXml, CancellationToken cancellationToken = default)
        => ValidateAsync(finalDeXml, "El DE01 final (firmado, con QR)", cancellationToken);

    private async Task ValidateAsync(string xml, string subject, CancellationToken cancellationToken)
    {
        var rootPath = Path.Combine(_packageDirectory, RootSchemaFile);
        if (!File.Exists(rootPath))
        {
            throw new DomainException(
                $"El paquete XSD oficial v150 no esta desplegado en '{_packageDirectory}'. El DE no puede validarse ni emitirse.");
        }

        XmlSchemaValidationResult result;
        try
        {
            result = await _validator.ValidateAsync(xml, rootPath, cancellationToken);
        }
        catch (System.Xml.XmlException ex)
        {
            throw new DomainException($"{subject} no es XML bien formado o el paquete XSD no pudo cargarse: {ex.Message}");
        }

        if (result.IsValid)
        {
            return;
        }

        var details = string.Join("; ", result.Errors.Take(5).Select(error =>
            $"{error.Message} (linea {error.LineNumber}, pos {error.LinePosition})"));
        throw new DomainException($"{subject} no valida contra el XSD oficial v150 ({result.Errors.Count} errores): {details}");
    }

    public async Task<string?> CheckPackageAsync(CancellationToken cancellationToken = default)
    {
        var rootPath = Path.Combine(_packageDirectory, RootSchemaFile);
        if (!File.Exists(rootPath))
        {
            return $"El paquete XSD oficial v150 no esta desplegado en '{_packageDirectory}'.";
        }

        try
        {
            // Solo fuerza la carga/compilacion del paquete; el resultado de validar un documento vacio no se usa.
            await _validator.ValidateAsync("<readiness/>", rootPath, cancellationToken);
            return null;
        }
        catch (Exception ex) when (ex is System.Xml.XmlException or System.Xml.Schema.XmlSchemaException or IOException)
        {
            return $"El paquete XSD oficial v150 no pudo compilarse: {ex.Message}";
        }
    }

    /// <summary>Copia con Signature y gCamFuFD de relleno (solo para validar estructura).</summary>
    internal static string WithPlaceholders(string unsignedDeXml)
    {
        var document = XDocument.Parse(unsignedDeXml);
        var de = document.Root?.Element(Sifen + "DE");
        var cdc = de?.Attribute("Id")?.Value;
        if (de is null || string.IsNullOrEmpty(cdc))
        {
            // Sin DE/Id el XSD lo rechazara con detalle: se valida tal cual.
            return unsignedDeXml;
        }

        var signature = new XElement(Ds + "Signature",
            new XElement(Ds + "SignedInfo",
                new XElement(Ds + "CanonicalizationMethod", new XAttribute("Algorithm", "http://www.w3.org/2001/10/xml-exc-c14n#")),
                new XElement(Ds + "SignatureMethod", new XAttribute("Algorithm", "http://www.w3.org/2001/04/xmldsig-more#rsa-sha256")),
                new XElement(Ds + "Reference", new XAttribute("URI", "#" + cdc),
                    new XElement(Ds + "Transforms", new XElement(Ds + "Transform", new XAttribute("Algorithm", "http://www.w3.org/2000/09/xmldsig#enveloped-signature"))),
                    new XElement(Ds + "DigestMethod", new XAttribute("Algorithm", "http://www.w3.org/2001/04/xmlenc#sha256")),
                    new XElement(Ds + "DigestValue", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA="))),
            new XElement(Ds + "SignatureValue", "AAAA"),
            new XElement(Ds + "KeyInfo", new XElement(Ds + "X509Data", new XElement(Ds + "X509Certificate", "AAAA"))));
        de.AddAfterSelf(signature);
        AddQrPlaceholder(signature, cdc);
        return document.ToString(SaveOptions.DisableFormatting);
    }

    /// <summary>Copia del rDE firmado con un gCamFuFD de relleno tras la Signature real (solo para validar estructura).</summary>
    internal static string WithQrPlaceholder(string signedDeXml)
    {
        var document = XDocument.Parse(signedDeXml);
        var cdc = document.Root?.Element(Sifen + "DE")?.Attribute("Id")?.Value;
        var signatures = document.Root?.Elements(Ds + "Signature").ToList();
        if (string.IsNullOrEmpty(cdc) || signatures is not { Count: 1 })
        {
            throw new DomainException("El DE01 firmado debe contener DE@Id y exactamente una ds:Signature hija de rDE.");
        }

        AddQrPlaceholder(signatures[0], cdc);
        return document.ToString(SaveOptions.DisableFormatting);
    }

    private static void AddQrPlaceholder(XElement signature, string cdc)
    {
        // dCarQR de relleno: el XSD exige 100-600 caracteres. NO es un QR valido (CSC y DigestValue reales: fase posterior).
        var qr = "https://ekuatia.set.gov.py/consultas-test/qr?nVersion=150&Id=" + cdc + "&dFeEmiDE=PLACEHOLDER&cHashQR=PLACEHOLDER";
        signature.AddAfterSelf(new XElement(Sifen + "gCamFuFD", new XElement(Sifen + "dCarQR", qr)));
    }
}
