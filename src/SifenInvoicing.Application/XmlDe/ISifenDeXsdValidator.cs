namespace SifenInvoicing.Application.XmlDe;

/// <summary>
/// Valida el DE (rDE sin firma) contra el paquete XSD oficial v150 incluido en el despliegue. Sin Internet, sin XSD
/// configurables por tenant. Lanza <see cref="Domain.Common.DomainException"/> con el detalle si el XML no valida.
/// </summary>
public interface ISifenDeXsdValidator
{
    Task EnsureValidAsync(string unsignedDeXml, CancellationToken cancellationToken = default);

    /// <summary>
    /// Valida el rDE ya FIRMADO (rDE{dVerFor, DE, Signature}): conserva la Signature real y solo agrega un gCamFuFD de
    /// relleno a una copia (el QR/CSC reales son de la Fase 4.5). Exige exactamente una ds:Signature.
    /// </summary>
    Task EnsureSignedValidAsync(string signedDeXml, CancellationToken cancellationToken = default);

    /// <summary>
    /// Valida el rDE FINAL (rDE{dVerFor, DE, Signature, gCamFuFD}) tal como se persistira, sin ningun relleno.
    /// </summary>
    Task EnsureFinalValidAsync(string finalDeXml, CancellationToken cancellationToken = default);

    /// <summary>
    /// Comprobacion de preparacion sin documento: el paquete XSD v150 esta desplegado y compila. Devuelve null si esta
    /// disponible o el motivo en caso contrario. No valida ni genera ningun DE.
    /// </summary>
    Task<string?> CheckPackageAsync(CancellationToken cancellationToken = default);
}
