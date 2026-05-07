using SifenInvoicing.Domain.Tenants;

namespace SifenInvoicing.Application.Security;

public interface ITenantCertificateValidator
{
    Task<CertificateValidationResult> ValidateAsync(
        TenantCertificateMetadata? metadata,
        CancellationToken cancellationToken = default);
}
