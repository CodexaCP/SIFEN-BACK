namespace SifenInvoicing.Infrastructure.Sifen;

public interface ISifenSoapTransport
{
    Task<SifenSoapTransportResult> SendAsync(
        Uri endpoint,
        string requestXml,
        string? soapAction,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}
