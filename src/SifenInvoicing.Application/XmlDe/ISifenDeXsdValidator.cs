namespace SifenInvoicing.Application.XmlDe;

/// <summary>
/// Valida el DE (rDE sin firma) contra el paquete XSD oficial v150 incluido en el despliegue. Sin Internet, sin XSD
/// configurables por tenant. Lanza <see cref="Domain.Common.DomainException"/> con el detalle si el XML no valida.
/// </summary>
public interface ISifenDeXsdValidator
{
    Task EnsureValidAsync(string unsignedDeXml, CancellationToken cancellationToken = default);
}
