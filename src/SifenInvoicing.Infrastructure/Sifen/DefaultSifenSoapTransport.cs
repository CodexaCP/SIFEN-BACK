using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Configuration;

namespace SifenInvoicing.Infrastructure.Sifen;

public sealed class DefaultSifenSoapTransport : ISifenSoapTransport
{
    private readonly IConfiguration _configuration;

    public DefaultSifenSoapTransport(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task<SifenSoapTransportResult> SendAsync(
        Uri endpoint,
        string requestXml,
        string? soapAction,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        using var handler = CreateHandler();
        using var client = new HttpClient(handler)
        {
            Timeout = timeout
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(requestXml, System.Text.Encoding.UTF8, "text/xml")
        };

        request.Content.Headers.ContentType = new MediaTypeHeaderValue("text/xml")
        {
            CharSet = "utf-8"
        };

        if (!string.IsNullOrWhiteSpace(soapAction))
        {
            request.Headers.TryAddWithoutValidation("SOAPAction", soapAction);
        }

        using var response = await client.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        return new SifenSoapTransportResult(
            response.IsSuccessStatusCode,
            (int)response.StatusCode,
            responseBody);
    }

    private HttpClientHandler CreateHandler()
    {
        var handler = new HttpClientHandler();
        var certificatePath = _configuration["Sifen:Transport:ClientCertificatePath"];
        var passwordEnvironmentVariable = _configuration["Sifen:Transport:ClientCertificatePasswordEnvironmentVariable"];

        if (string.IsNullOrWhiteSpace(certificatePath))
        {
            return handler;
        }

        var password = string.IsNullOrWhiteSpace(passwordEnvironmentVariable)
            ? null
            : Environment.GetEnvironmentVariable(passwordEnvironmentVariable);

        var certificate = new X509Certificate2(certificatePath, password);
        handler.ClientCertificates.Add(certificate);
        return handler;
    }
}
