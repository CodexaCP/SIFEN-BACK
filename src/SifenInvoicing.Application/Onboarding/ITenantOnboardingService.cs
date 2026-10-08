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

    Task RegisterFiscalProfileAsync(
        RegisterFiscalProfileCommand command,
        CancellationToken cancellationToken = default);

    Task RegisterFiscalStampAsync(
        RegisterFiscalStampCommand command,
        CancellationToken cancellationToken = default);

    Task RegisterNumberingSequenceAsync(
        RegisterNumberingSequenceCommand command,
        CancellationToken cancellationToken = default);

    Task RegisterSifenSettingsAsync(
        RegisterSifenSettingsCommand command,
        CancellationToken cancellationToken = default);

    Task RegisterCertificateMetadataAsync(
        RegisterCertificateMetadataCommand command,
        CancellationToken cancellationToken = default);

    Task<TenantFiscalSetup> GetFiscalSetupAsync(
        Guid tenantId,
        SifenEnvironmentType environment,
        CancellationToken cancellationToken = default);

    Task<TenantReadinessReport> GetReadinessAsync(
        Guid tenantId,
        SifenEnvironmentType environment,
        CancellationToken cancellationToken = default);
}
