using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Authentication;
using System.Text;

namespace SifenInvoicing.Infrastructure.Sifen;

/// <summary>SOAP 1.2 sobre HTTPS con TLS 1.2 y autenticacion mutua usando el certificado del tenant (Manual Tecnico v150).</summary>
public sealed class DefaultSifenSoapTransport : ISifenSoapTransport
{
    public async Task<SifenSoapTransportResult> SendAsync(
        SifenSoapRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var handler = CreateHandler(request);
        using var client = new HttpClient(handler)
        {
            Timeout = request.Timeout
        };
        using var message = BuildHttpRequest(request);
        using var response = await client.SendAsync(message, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        return new SifenSoapTransportResult(
            response.IsSuccessStatusCode,
            (int)response.StatusCode,
            responseBody);
    }

    /// <summary>POST SOAP 1.2: Content-Type application/soap+xml (con action solo si esta confirmada) y sin cabecera SOAPAction.</summary>
    public static HttpRequestMessage BuildHttpRequest(SifenSoapRequest request)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, request.Endpoint)
        {
            Content = new StringContent(
                request.EnvelopeXml,
                new UTF8Encoding(false),
                MediaTypeHeaderValue.Parse(SifenSoapEnvelopeBuilder.BuildContentType(request.SoapAction)))
        };
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/soap+xml"));
        return message;
    }

    private static SocketsHttpHandler CreateHandler(SifenSoapRequest request)
    {
        var handler = new SocketsHttpHandler
        {
            SslOptions = new SslClientAuthenticationOptions
            {
                EnabledSslProtocols = SslProtocols.Tls12
            }
        };

        if (request.ClientCertificate is not null)
        {
            handler.SslOptions.ClientCertificates = [request.ClientCertificate];
        }

        return handler;
    }
}
