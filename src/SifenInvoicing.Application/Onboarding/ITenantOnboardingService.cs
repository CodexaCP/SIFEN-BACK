using SifenInvoicing.Domain.Tenants;

namespace SifenInvoicing.Application.Onboarding;

public interface ITenantOnboardingService
{
    Task<TenantOnboardingResult> CreateTenantAsync(
        CreateTenantCommand command,
        CancellationToken cancellationToken = default);

    Task RegisterTaxpayerProfileAsync(
        RegisterTaxpayerProfileCommand command,
        CancellationToken cancellationToken = default);

    Task RegisterSifenSettingsAsync(
        RegisterSifenSettingsCommand command,
        CancellationToken cancellationToken = default);

    Task RegisterCertificateMetadataAsync(
        RegisterCertificateMetadataCommand command,
        CancellationToken cancellationToken = default);

    Task<TenantReadinessReport> GetReadinessAsync(
        Guid tenantId,
        SifenEnvironmentType environment,
        CancellationToken cancellationToken = default);
}
