using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using SifenInvoicing.Application.Diagnostics;
using SifenInvoicing.Application.Sifen;
using SifenInvoicing.Infrastructure.Persistence;
using System.Text;
using System.Xml.Linq;

namespace SifenInvoicing.Infrastructure.Sifen;

public sealed class ConfigurationSifenSubmissionGateway : ISifenSubmissionGateway
{
    private readonly IConfiguration _configuration;
    private readonly ISystemClock _clock;
    private readonly ISifenSoapTransport _transport;
    private readonly SifenDbContext _dbContext;

    public ConfigurationSifenSubmissionGateway(
        IConfiguration configuration,
        ISystemClock clock,
        ISifenSoapTransport transport,
        SifenDbContext dbContext)
    {
        _configuration = configuration;
        _clock = clock;
        _transport = transport;
        _dbContext = dbContext;
    }

    public async Task<SifenSubmissionResult> SendToSifenAsync(
        SendToSifenCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var tenantSettings = await _dbContext.TenantSifenSettings
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(item =>
                item.TenantId == command.TenantId &&
                item.Environment == command.Environment &&
                item.IsActive,
                cancellationToken);

        var baseUrl = _configuration[$"Sifen:Environments:{command.Environment}:BaseUrl"] ?? string.Empty;
        var receivePath = _configuration[$"Sifen:Environments:{command.Environment}:Wsdl:Receive"] ?? string.Empty;
        var endpoint = !string.IsNullOrWhiteSpace(tenantSettings?.EndpointUrl)
            ? tenantSettings.EndpointUrl!
            : $"{baseUrl.TrimEnd('/')}{receivePath.Replace("?wsdl", string.Empty, StringComparison.OrdinalIgnoreCase)}";
        var transportMode = tenantSettings?.TransportMode ?? _configuration["Sifen:Transport:Mode"];
        ValidateSignedXml(command.Cdc, command.SignedXml);
        var requestXml = BuildSoapEnvelope(command);

        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(receivePath))
        {
            return new SifenSubmissionResult(
                false,
                endpoint,
                null,
                "DIAGNOSTIC_TRANSPORT|MISSING_ENDPOINT_CONFIGURATION",
                requestXml,
                "MISSING_ENDPOINT_CONFIGURATION",
                "SIFEN endpoint configuration is incomplete.",
                null,
                true);
        }

        if (IsDiagnosticMode(transportMode))
        {
            return new SifenSubmissionResult(
                false,
                endpoint,
                null,
                "DIAGNOSTIC_TRANSPORT|DIAGNOSTIC_MODE_ENABLED",
                requestXml,
                "DIAGNOSTIC_MODE_ENABLED",
                "Diagnostic mode is enabled. Real SIFEN submission was not attempted.",
                null,
                true);
        }

        if (!HasClientCertificateConfiguration())
        {
            return new SifenSubmissionResult(
                false,
                endpoint,
                null,
                "DIAGNOSTIC_TRANSPORT|MISSING_CLIENT_CERTIFICATE_CONFIGURATION",
                requestXml,
                "MISSING_CLIENT_CERTIFICATE_CONFIGURATION",
                "Client certificate transport configuration is missing.",
                null,
                true);
        }

        return await SendAsync(endpoint, requestXml, command.Environment, cancellationToken);
    }

    private async Task<SifenSubmissionResult> SendAsync(
        string endpoint,
        string requestXml,
        Domain.Tenants.SifenEnvironmentType environment,
        CancellationToken cancellationToken)
    {
        var timeoutSeconds = int.TryParse(_configuration["Sifen:Timeouts:SoapRequestSeconds"], out var configuredTimeoutSeconds)
            ? configuredTimeoutSeconds
            : 60;
        var soapAction = _configuration[$"Sifen:Environments:{environment}:SoapAction:Receive"];

        try
        {
            var transportResult = await _transport.SendAsync(
                new Uri(endpoint, UriKind.Absolute),
                requestXml,
                soapAction,
                TimeSpan.FromSeconds(timeoutSeconds),
                cancellationToken);

            if (transportResult.IsSuccessStatusCode)
            {
                return new SifenSubmissionResult(
                    true,
                    endpoint,
                    null,
                    transportResult.ResponseBody,
                    requestXml,
                    "HTTP_OK",
                    "SIFEN SOAP transport completed.",
                    transportResult.HttpStatusCode,
                    false);
            }

            return new SifenSubmissionResult(
                false,
                endpoint,
                null,
                $"HTTP_ERROR|{transportResult.HttpStatusCode}|{transportResult.ResponseBody}",
                requestXml,
                "HTTP_ERROR",
                "SIFEN SOAP endpoint returned a non-success status code.",
                transportResult.HttpStatusCode,
                false);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new SifenSubmissionResult(
                false,
                endpoint,
                null,
                "SOAP_TIMEOUT|Request timed out.",
                requestXml,
                "SOAP_TIMEOUT",
                "SIFEN SOAP request timed out.",
                null,
                false);
        }
        catch (Exception ex)
        {
            return new SifenSubmissionResult(
                false,
                endpoint,
                null,
                $"SOAP_TRANSPORT_ERROR|{ex.GetType().Name}",
                requestXml,
                "SOAP_TRANSPORT_ERROR",
                $"SIFEN SOAP transport failed with {ex.GetType().Name}.",
                null,
                false);
        }
    }

    private string BuildSoapEnvelope(SendToSifenCommand command)
    {
        XNamespace soap = "http://schemas.xmlsoap.org/soap/envelope/";
        XNamespace sifen = "http://ekuatia.set.gov.py/sifen/xsd";
        var signedDocument = XDocument.Parse(command.SignedXml, LoadOptions.PreserveWhitespace);
        var controlId = ((_clock.UtcNow.ToUnixTimeMilliseconds() % 1_000_000_000_000_000L) + 1).ToString("D15");

        var envelope = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement(soap + "Envelope",
                new XAttribute(XNamespace.Xmlns + "soapenv", soap),
                new XAttribute(XNamespace.Xmlns + "sif", sifen),
                new XElement(soap + "Body",
                    new XElement(sifen + "rEnviDe",
                        new XElement(sifen + "dId", controlId),
                        new XElement(sifen + "xDE", signedDocument.Root)))));

        return envelope.ToString(SaveOptions.DisableFormatting);
    }

    private static bool IsDiagnosticMode(string? transportMode)
    {
        return string.Equals(transportMode, "Diagnostic", StringComparison.OrdinalIgnoreCase);
    }

    private bool HasClientCertificateConfiguration()
    {
        return !string.IsNullOrWhiteSpace(_configuration["Sifen:Transport:ClientCertificatePath"]) &&
               !string.IsNullOrWhiteSpace(_configuration["Sifen:Transport:ClientCertificatePasswordEnvironmentVariable"]);
    }

    private static void ValidateSignedXml(string cdc, string signedXml)
    {
        var document = XDocument.Parse(signedXml, LoadOptions.PreserveWhitespace);
        XNamespace sifen = "http://ekuatia.set.gov.py/sifen/xsd";
        XNamespace ds = "http://www.w3.org/2000/09/xmldsig#";

        var de = document.Root?.Element(sifen + "DE");
        if (de?.Attribute("Id")?.Value != cdc)
        {
            throw new InvalidOperationException("Signed XML does not contain the expected DE Id.");
        }

        var signature = document.Root?.Element(ds + "Signature");
        if (signature is null)
        {
            throw new InvalidOperationException("Signed XML does not contain ds:Signature.");
        }

        var referenceUri = signature
            .Descendants(ds + "Reference")
            .FirstOrDefault()?
            .Attribute("URI")?
            .Value;

        if (!string.Equals(referenceUri, $"#{cdc}", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Signed XML signature reference does not match the CDC.");
        }
    }
}
