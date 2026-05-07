using Microsoft.EntityFrameworkCore;
using SifenInvoicing.Application.Auditing;
using SifenInvoicing.Application.Diagnostics;
using SifenInvoicing.Application.Onboarding;
using SifenInvoicing.Application.Security;
using SifenInvoicing.Domain.Tenants;
using SifenInvoicing.Infrastructure.Persistence;

namespace SifenInvoicing.Infrastructure.Onboarding;

public sealed class EfTenantOnboardingService : ITenantOnboardingService
{
    private readonly SifenDbContext _dbContext;
    private readonly IAuditTrail _auditTrail;
    private readonly ISystemClock _clock;
    private readonly ITenantSecretProvider _secretProvider;
    private readonly ITenantCertificateValidator _certificateValidator;

    public EfTenantOnboardingService(
        SifenDbContext dbContext,
        IAuditTrail auditTrail,
        ISystemClock clock,
        ITenantSecretProvider secretProvider,
        ITenantCertificateValidator certificateValidator)
    {
        _dbContext = dbContext;
        _auditTrail = auditTrail;
        _clock = clock;
        _secretProvider = secretProvider;
        _certificateValidator = certificateValidator;
    }

    public async Task<TenantOnboardingResult> CreateTenantAsync(
        CreateTenantCommand command,
        CancellationToken cancellationToken = default)
    {
        var slug = Normalize(command.Slug);
        var exists = await _dbContext.Tenants.AnyAsync(tenant => tenant.Slug == slug, cancellationToken);
        if (exists)
        {
            throw new InvalidOperationException($"Tenant slug '{slug}' already exists.");
        }

        var tenant = Tenant.CreateSharedDatabaseTenant(
            slug,
            command.DisplayName,
            null,
            command.MaxInvoicesPerMonth,
            command.MaxUsers);
        _dbContext.Tenants.Add(tenant);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await RecordAuditAsync("tenant.created", tenant.Id, tenant.Id, "Created", cancellationToken);

        return new TenantOnboardingResult(
            tenant.Id,
            tenant.Slug,
            tenant.DisplayName,
            tenant.Status.ToString(),
            tenant.IsolationMode.ToString(),
            tenant.MaxInvoicesPerMonth,
            tenant.MaxUsers);
    }

    public async Task RegisterTaxpayerProfileAsync(
        RegisterTaxpayerProfileCommand command,
        CancellationToken cancellationToken = default)
    {
        await EnsureTenantExistsAsync(command.TenantId, cancellationToken);

        var exists = await _dbContext.TaxpayerProfiles.IgnoreQueryFilters().AnyAsync(
            profile => profile.TenantId == command.TenantId &&
                       profile.RucNumber == command.RucNumber &&
                       profile.RucCheckDigit == command.RucCheckDigit,
            cancellationToken);

        if (exists)
        {
            throw new InvalidOperationException("Taxpayer profile already exists for this tenant and RUC.");
        }

        _dbContext.TaxpayerProfiles.Add(TaxpayerProfile.Create(
            command.TenantId,
            command.RucNumber,
            command.RucCheckDigit,
            command.LegalName,
            command.TradeName));

        await _dbContext.SaveChangesAsync(cancellationToken);
        await RecordAuditAsync("tenant.taxpayer_profile.registered", command.TenantId, command.TenantId, "Registered", cancellationToken);
    }

    public async Task RegisterSifenSettingsAsync(
        RegisterSifenSettingsCommand command,
        CancellationToken cancellationToken = default)
    {
        await EnsureTenantExistsAsync(command.TenantId, cancellationToken);

        var exists = await _dbContext.TenantSifenSettings.IgnoreQueryFilters().AnyAsync(
            settings => settings.TenantId == command.TenantId &&
                        settings.Environment == command.Environment,
            cancellationToken);

        if (exists)
        {
            throw new InvalidOperationException("SIFEN settings already exist for this tenant and environment.");
        }

        _dbContext.TenantSifenSettings.Add(TenantSifenSettings.Create(
            command.TenantId,
            command.Environment,
            command.CscIdentifier,
            command.CscSecretReference,
            command.EstablishmentCode,
            command.ExpeditionPointCode,
            command.CurrentDocumentNumber,
            command.StampingNumber,
            command.CertificateSecretReference,
            command.CertificatePasswordSecretReference,
            command.CertificateAlias,
            command.XmlSchemaRootPath,
            command.EndpointUrl,
            command.TransportMode));

        await _dbContext.SaveChangesAsync(cancellationToken);
        await RecordAuditAsync("tenant.sifen_settings.registered", command.TenantId, command.TenantId, "Registered", cancellationToken);
    }

    public async Task RegisterCertificateMetadataAsync(
        RegisterCertificateMetadataCommand command,
        CancellationToken cancellationToken = default)
    {
        await EnsureTenantExistsAsync(command.TenantId, cancellationToken);

        var exists = await _dbContext.TenantCertificateMetadata.IgnoreQueryFilters().AnyAsync(
            certificate => certificate.TenantId == command.TenantId &&
                           certificate.Environment == command.Environment &&
                           certificate.Purpose == command.Purpose &&
                           certificate.Alias == command.Alias,
            cancellationToken);

        if (exists)
        {
            throw new InvalidOperationException("Certificate metadata already exists for this tenant, environment, purpose and alias.");
        }

        _dbContext.TenantCertificateMetadata.Add(TenantCertificateMetadata.Create(
            command.TenantId,
            command.Environment,
            command.Purpose,
            command.Alias,
            command.Subject,
            command.FingerprintSha256,
            command.SerialNumber,
            command.CertificateSecretReference,
            command.CertificatePasswordSecretReference,
            command.ValidFrom,
            command.ValidTo));

        await _dbContext.SaveChangesAsync(cancellationToken);
        await RecordAuditAsync("tenant.certificate_metadata.registered", command.TenantId, command.TenantId, "Registered", cancellationToken);
    }

    public async Task<TenantReadinessReport> GetReadinessAsync(
        Guid tenantId,
        SifenEnvironmentType environment,
        CancellationToken cancellationToken = default)
    {
        var tenant = await _dbContext.Tenants.FindAsync([tenantId], cancellationToken);
        var taxpayerProfile = await _dbContext.TaxpayerProfiles.IgnoreQueryFilters()
            .AnyAsync(profile => profile.TenantId == tenantId && profile.IsActive, cancellationToken);
        var sifenSettings = await _dbContext.TenantSifenSettings.IgnoreQueryFilters()
            .SingleOrDefaultAsync(settings => settings.TenantId == tenantId && settings.Environment == environment && settings.IsActive, cancellationToken);
        var now = _clock.UtcNow;
        var certificates = await _dbContext.TenantCertificateMetadata.IgnoreQueryFilters()
            .Where(certificate => certificate.TenantId == tenantId &&
                                  certificate.Environment == environment &&
                                  certificate.IsActive &&
                                  certificate.ValidFrom <= now &&
                                  certificate.ValidTo > now)
            .ToListAsync(cancellationToken);
        var xmlSignatureCertificate = certificates.FirstOrDefault(certificate => certificate.Purpose == CertificatePurpose.XmlSignature);
        var mutualTlsCertificate = certificates.FirstOrDefault(certificate => certificate.Purpose == CertificatePurpose.MutualTls);
        var cscSecretCheck = sifenSettings is null
            ? new SecretCheckResult("csc.secret", SecretStatus.Missing, "SIFEN settings are missing.")
            : await _secretProvider.CheckStringSecretAsync(sifenSettings.CscSecretReference, "csc.secret", cancellationToken);
        var xmlSignatureCertificateValidation = await _certificateValidator.ValidateAsync(xmlSignatureCertificate, cancellationToken);
        var mutualTlsCertificateValidation = await _certificateValidator.ValidateAsync(mutualTlsCertificate, cancellationToken);

        var checks = new List<TenantReadinessCheck>
        {
            new("tenant.exists", tenant is not null, tenant is null ? "Tenant does not exist." : "Tenant exists."),
            new("tenant.active", tenant?.Status == TenantStatus.Active, tenant?.Status == TenantStatus.Active ? "Tenant is active." : "Tenant is not active."),
            new("taxpayer_profile.active", taxpayerProfile, taxpayerProfile ? "Active taxpayer profile exists." : "Active taxpayer profile is missing."),
            new("sifen_settings.active", sifenSettings is not null, sifenSettings is null ? "SIFEN settings are missing for this environment." : "SIFEN settings exist for this environment."),
            new("csc.secret", cscSecretCheck.IsReady, cscSecretCheck.Summary),
            new("certificate.xml_signature", xmlSignatureCertificateValidation.IsReady, xmlSignatureCertificateValidation.Summary),
            new("certificate.mutual_tls", mutualTlsCertificateValidation.IsReady, mutualTlsCertificateValidation.Summary)
        };

        return new TenantReadinessReport(
            tenantId,
            environment,
            checks.All(check => check.IsReady),
            checks);
    }

    private async Task EnsureTenantExistsAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var exists = await _dbContext.Tenants.AnyAsync(tenant => tenant.Id == tenantId, cancellationToken);
        if (!exists)
        {
            throw new InvalidOperationException($"Tenant '{tenantId}' does not exist.");
        }
    }

    private Task RecordAuditAsync(
        string eventName,
        Guid tenantId,
        Guid resourceId,
        string outcome,
        CancellationToken cancellationToken)
    {
        return _auditTrail.RecordAsync(new AuditEvent
        {
            EventName = eventName,
            Category = AuditCategory.TenantOperation,
            Severity = AuditSeverity.Information,
            OccurredAt = _clock.UtcNow,
            TenantId = tenantId.ToString(),
            ResourceType = "Tenant",
            ResourceId = resourceId.ToString(),
            Outcome = outcome
        }, cancellationToken);
    }

    private static string Normalize(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException("Value is required.")
            : value.Trim().ToLowerInvariant();
    }
}
