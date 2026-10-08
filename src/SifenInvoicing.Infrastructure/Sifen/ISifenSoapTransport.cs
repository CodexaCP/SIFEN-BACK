using System.Security.Cryptography.X509Certificates;

namespace SifenInvoicing.Infrastructure.Sifen;

/// <param name="SoapAction">Solo se envia si esta confirmada (parametro action del Content-Type SOAP 1.2). Null = no se envia.</param>
/// <param name="ClientCertificate">Certificado de transporte (mTLS) del tenant. El llamador es dueno de su ciclo de vida.</param>
public sealed record SifenSoapRequest(
    Uri Endpoint,
    string EnvelopeXml,
    string? SoapAction,
    TimeSpan Timeout,
    X509Certificate2? ClientCertificate);

public interface ISifenSoapTransport
{
    Task<SifenSoapTransportResult> SendAsync(
        SifenSoapRequest request,
        CancellationToken cancellationToken = default);
}
