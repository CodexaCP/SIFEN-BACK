using System.Security.Cryptography.X509Certificates;
using Microsoft.EntityFrameworkCore;
using SifenInvoicing.Application.Security;
using SifenInvoicing.Domain.Tenants;
using SifenInvoicing.Infrastructure.Persistence;

namespace SifenInvoicing.Infrastructure.Sifen;

/// <summary>Entrega el certificado de transporte (mTLS) de un tenant y ambiente. El llamador debe disponer el certificado.</summary>
public interface ISifenClientCertificateProvider
{
    Task<X509Certificate2?> GetAsync(
        Guid tenantId,
        SifenEnvironmentType environment,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Resuelve el certificado desde TenantCertificateMetadata + ITenantSecretProvider (nunca desde configuracion global).
/// Prefiere el certificado con proposito MutualTls; si no existe usa el de XmlSignature, porque el Manual Tecnico v150 (7.5)
/// indica que el mismo certificado cualificado se usa para firmar y para autenticarse.
/// </summary>
public sealed class TenantSifenClientCertificateProvider : ISifenClientCertificateProvider
{
    private readonly SifenDbContext _dbContext;
    private readonly ITenantSecretProvider _secretProvider;

    public TenantSifenClientCertificateProvider(SifenDbContext dbContext, ITenantSecretProvider secretProvider)
    {
        _dbContext = dbContext;
        _secretProvider = secretProvider;
    }

    public async Task<X509Certificate2?> GetAsync(
        Guid tenantId,
        SifenEnvironmentType environment,
        CancellationToken cancellationToken = default)
    {
        var candidates = await _dbContext.TenantCertificateMetadata
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(item => item.TenantId == tenantId &&
                           item.Environment == environment &&
                           item.IsActive &&
                           (item.Purpose == CertificatePurpose.MutualTls || item.Purpose == CertificatePurpose.XmlSignature))
            .ToListAsync(cancellationToken);

        var metadata = candidates
            .OrderByDescending(item => item.Purpose == CertificatePurpose.MutualTls)
            .ThenByDescending(item => item.ValidFrom)
            .FirstOrDefault();

        if (metadata is null)
        {
            return null;
        }

        var pfxBytes = await _secretProvider.GetBinarySecretAsync(metadata.CertificateSecretReference, cancellationToken);
        var password = await _secretProvider.GetStringSecretAsync(metadata.CertificatePasswordSecretReference, cancellationToken);

        // Sin EphemeralKeySet: SChannel (Windows) no soporta claves efimeras para autenticacion cliente TLS.
        var certificate = new X509Certificate2(pfxBytes, password, X509KeyStorageFlags.DefaultKeySet);
        if (!certificate.HasPrivateKey)
        {
            certificate.Dispose();
            throw new InvalidOperationException("The transport certificate does not contain a private key.");
        }

        return certificate;
    }
}
