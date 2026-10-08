using SifenInvoicing.Domain.Tenants;

namespace SifenInvoicing.Application.Qr;

/// <summary>
/// Fase 4.5. Agrega gCamFuFD/dCarQR (QR oficial, Manual v150 13.8) al rDE YA FIRMADO, usando el DigestValue de su
/// ds:Signature definitiva y el CSC del tenant efectivo (Test/Produccion segun el tenant). No toca DE ni Signature.
/// Lanza <see cref="Domain.Common.DomainException"/> (sin incluir nunca el CSC) si el QR no puede generarse.
/// </summary>
public interface ISifenDeQrAttacher
{
    Task<string> AttachAsync(
        Guid tenantId,
        SifenEnvironmentType environment,
        string signedDeXml,
        CancellationToken cancellationToken = default);
}
