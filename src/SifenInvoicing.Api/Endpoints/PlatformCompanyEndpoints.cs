using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using SifenInvoicing.Application.Platform;
using SifenInvoicing.Application.Operations;
using SifenInvoicing.Domain.Common;
using SifenInvoicing.Domain.Tenants;
using SifenInvoicing.Infrastructure.Persistence;

namespace SifenInvoicing.Api.Endpoints;

public static class PlatformCompanyEndpoints
{
    public static IEndpointRouteBuilder MapPlatformCompanyEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/platform/companies")
            .WithTags("Platform Companies")
            .RequireAuthorization();

        group.MapGet("/", async (
            ClaimsPrincipal user,
            IPlatformCompanyService companyService,
            CancellationToken cancellationToken) =>
        {
            if (!IsSuperAdmin(user))
            {
                return Results.Forbid();
            }

            var items = await companyService.GetCompaniesAsync(cancellationToken);
            return Results.Ok(items);
        });

        group.MapGet("/{tenantId:guid}", async (
            Guid tenantId,
            ClaimsPrincipal user,
            IPlatformCompanyService companyService,
            CancellationToken cancellationToken) =>
        {
            EnsureCanAccessTenant(user, tenantId);

            var item = await companyService.GetCompanyByIdAsync(tenantId, cancellationToken);
            return item is null ? Results.NotFound() : Results.Ok(item);
        });

        group.MapPost("/", async (
            CreatePlatformCompanyRequest request,
            ClaimsPrincipal user,
            IPlatformCompanyService companyService,
            CancellationToken cancellationToken) =>
        {
            if (!IsSuperAdmin(user))
            {
                return Results.Forbid();
            }

            var item = await companyService.CreateCompanyAsync(
                new CreatePlatformCompanyCommand(
                    request.Slug,
                    request.DisplayName,
                    request.PlanName ?? "Plan base",
                    request.MaxInvoicesPerMonth,
                    request.MaxUsers,
                    request.AdminFullName,
                    request.AdminEmail,
                    request.AdminPassword),
                cancellationToken);

            return Results.Created($"/api/platform/companies/{item.Id}", item);
        });

        group.MapDelete("/{tenantId:guid}", async (
            Guid tenantId,
            string? confirm,
            ClaimsPrincipal user,
            IPlatformCompanyService companyService,
            CancellationToken cancellationToken) =>
        {
            if (!IsSuperAdmin(user))
            {
                return Results.Forbid();
            }

            Guid? actorTenantId = Guid.TryParse(user.FindFirstValue("tenantId") ?? user.FindFirstValue("companyId"), out var parsed)
                ? parsed
                : null;
            await companyService.DeleteCompanyAsync(new DeletePlatformCompanyCommand(tenantId, confirm, actorTenantId), cancellationToken);
            return Results.NoContent();
        });

        group.MapPut("/{tenantId:guid}/plan", async (
            Guid tenantId,
            UpdatePlatformCompanyPlanRequest request,
            ClaimsPrincipal user,
            IPlatformCompanyService companyService,
            CancellationToken cancellationToken) =>
        {
            if (!IsSuperAdmin(user))
            {
                return Results.Forbid();
            }

            var item = await companyService.UpdatePlanAsync(
                new UpdatePlatformCompanyPlanCommand(
                    tenantId,
                    request.PlanName,
                    request.MaxInvoicesPerMonth ?? request.InvoiceLimitPerMonth,
                    request.MaxUsers ?? request.UserLimit,
                    request.Active),
                cancellationToken);

            return Results.Ok(item);
        });

        group.MapPost("/{tenantId:guid}/admins", async (
            Guid tenantId,
            CreatePlatformCompanyAdminRequest request,
            ClaimsPrincipal user,
            IPlatformCompanyService companyService,
            CancellationToken cancellationToken) =>
        {
            if (!IsSuperAdmin(user))
            {
                return Results.Forbid();
            }

            var item = await companyService.CreateCompanyAdminAsync(
                new CreatePlatformCompanyAdminCommand(
                    tenantId,
                    request.FullName,
                    request.Email,
                    request.Password),
                cancellationToken);

            return Results.Created($"/api/platform/companies/{tenantId}/admins/{item.UserId}", item);
        });

        group.MapGet("/{tenantId:guid}/users", async (
            Guid tenantId,
            ClaimsPrincipal user,
            IPlatformCompanyService companyService,
            CancellationToken cancellationToken) =>
        {
            EnsureCanManageTenantUsers(user, tenantId);
            var result = await companyService.GetCompanyUsersAsync(tenantId, cancellationToken);
            return Results.Ok(result);
        });

        group.MapPost("/{tenantId:guid}/users", async (
            Guid tenantId,
            CreatePlatformTenantUserRequest request,
            ClaimsPrincipal user,
            IPlatformCompanyService companyService,
            CancellationToken cancellationToken) =>
        {
            EnsureCanManageTenantUsers(user, tenantId);
            var item = await companyService.CreateCompanyUserAsync(
                new CreatePlatformTenantUserCommand(
                    tenantId,
                    request.FullName,
                    request.Email,
                    request.Password,
                    request.Role,
                    request.IsActive),
                cancellationToken);

            return Results.Created($"/api/platform/companies/{tenantId}/users/{item.UserId}", item);
        });

        group.MapPut("/{tenantId:guid}/users/{userId:guid}", async (
            Guid tenantId,
            Guid userId,
            UpdatePlatformTenantUserRequest request,
            ClaimsPrincipal user,
            IPlatformCompanyService companyService,
            CancellationToken cancellationToken) =>
        {
            EnsureCanManageTenantUsers(user, tenantId);
            var item = await companyService.UpdateCompanyUserAsync(
                new UpdatePlatformTenantUserCommand(
                    tenantId,
                    userId,
                    request.FullName,
                    request.Email,
                    request.Role,
                    request.IsActive),
                cancellationToken);

            return Results.Ok(item);
        });

        group.MapGet("/{tenantId:guid}/sifen-config", async (
            Guid tenantId,
            string? environment,
            ClaimsPrincipal user,
            SifenDbContext dbContext,
            IOperationalReadinessReporter readinessReporter,
            CancellationToken cancellationToken) =>
        {
            EnsureCanAccessTenant(user, tenantId);
            var parsedEnvironment = ParseOptionalEnvironment(environment);

            var tenant = await dbContext.Tenants
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(item => item.Id == tenantId, cancellationToken);

            if (tenant is null)
            {
                return Results.NotFound();
            }

            var taxpayerProfile = await dbContext.TaxpayerProfiles
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(item => item.TenantId == tenantId && item.IsActive)
                .OrderByDescending(item => item.UpdatedAt ?? item.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);

            var sifenSettings = await dbContext.TenantSifenSettings
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(item => item.TenantId == tenantId && item.IsActive && item.Environment == parsedEnvironment)
                .FirstOrDefaultAsync(cancellationToken);

            var diagnostic = await readinessReporter.GetTenantFeDiagnosticAsync(tenantId, parsedEnvironment, cancellationToken);

            return Results.Ok(new SifenConfigResponse(
                tenant.Id,
                taxpayerProfile?.RucNumber,
                taxpayerProfile?.RucCheckDigit,
                taxpayerProfile?.LegalName,
                sifenSettings?.Environment.ToString(),
                sifenSettings?.CscIdentifier,
                sifenSettings?.CscSecretReference,
                sifenSettings?.CertificateSecretReference,
                sifenSettings?.CertificatePasswordSecretReference,
                sifenSettings?.CertificateAlias,
                sifenSettings?.EstablishmentCode,
                sifenSettings?.ExpeditionPointCode,
                sifenSettings?.CurrentDocumentNumber,
                sifenSettings?.StampingNumber,
                sifenSettings?.XmlSchemaRootPath,
                sifenSettings?.EndpointUrl,
                sifenSettings?.TransportMode,
                diagnostic.Summary));
        });

        group.MapGet("/{tenantId:guid}/kude-template", async (
            Guid tenantId,
            ClaimsPrincipal user,
            SifenDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            EnsureCanAccessTenant(user, tenantId);

            var settings = await dbContext.TenantKudeTemplateSettings
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(item => item.TenantId == tenantId, cancellationToken);

            settings ??= TenantKudeTemplateSettings.CreateDefault(tenantId);

            return Results.Ok(new KudeTemplateResponse(
                tenantId,
                settings.TemplateCode,
                settings.LogoUrl,
                settings.PrimaryColor,
                settings.SecondaryColor,
                settings.FooterText,
                settings.ShowPhone,
                settings.ShowEmail));
        });

        group.MapPut("/{tenantId:guid}/kude-template", async (
            Guid tenantId,
            UpdateKudeTemplateRequest request,
            ClaimsPrincipal user,
            SifenDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            EnsureCanAccessTenant(user, tenantId);

            var tenant = await dbContext.Tenants
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(item => item.Id == tenantId, cancellationToken);

            if (tenant is null)
            {
                return Results.NotFound();
            }

            var settings = await dbContext.TenantKudeTemplateSettings
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(item => item.TenantId == tenantId, cancellationToken);

            if (settings is null)
            {
                settings = TenantKudeTemplateSettings.Create(
                    tenantId,
                    request.TemplateCode,
                    request.LogoUrl,
                    request.PrimaryColor,
                    request.SecondaryColor,
                    request.FooterText,
                    request.ShowPhone,
                    request.ShowEmail);
                dbContext.TenantKudeTemplateSettings.Add(settings);
            }
            else
            {
                settings.Update(
                    request.TemplateCode,
                    request.LogoUrl,
                    request.PrimaryColor,
                    request.SecondaryColor,
                    request.FooterText,
                    request.ShowPhone,
                    request.ShowEmail);
            }

            await dbContext.SaveChangesAsync(cancellationToken);

            return Results.Ok(new KudeTemplateResponse(
                tenantId,
                settings.TemplateCode,
                settings.LogoUrl,
                settings.PrimaryColor,
                settings.SecondaryColor,
                settings.FooterText,
                settings.ShowPhone,
                settings.ShowEmail));
        });

        group.MapPut("/{tenantId:guid}/sifen-config", async (
            Guid tenantId,
            UpdateSifenConfigRequest request,
            ClaimsPrincipal user,
            SifenDbContext dbContext,
            IOperationalReadinessReporter readinessReporter,
            CancellationToken cancellationToken) =>
        {
            EnsureCanAccessTenant(user, tenantId);

            var tenant = await dbContext.Tenants
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(item => item.Id == tenantId, cancellationToken);

            if (tenant is null)
            {
                return Results.NotFound();
            }

            if (tenant.Status != TenantStatus.Active)
            {
                throw new DomainException("Tenant must be active to configure SIFEN.");
            }

            var normalized = NormalizeRequest(request);
            var environment = ParseEnvironment(normalized.Environment);

            var taxpayerProfile = await dbContext.TaxpayerProfiles
                .IgnoreQueryFilters()
                .Where(item => item.TenantId == tenantId && item.IsActive)
                .OrderByDescending(item => item.UpdatedAt ?? item.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);

            if (taxpayerProfile is null)
            {
                dbContext.TaxpayerProfiles.Add(TaxpayerProfile.Create(
                    tenantId,
                    normalized.Ruc,
                    normalized.RucCheckDigit,
                    normalized.LegalName,
                    null));
            }
            else
            {
                taxpayerProfile.Update(
                    normalized.Ruc,
                    normalized.RucCheckDigit,
                    normalized.LegalName,
                    null);
            }

            var settings = await dbContext.TenantSifenSettings
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(item => item.TenantId == tenantId && item.Environment == environment, cancellationToken);

            if (settings is null)
            {
                dbContext.TenantSifenSettings.Add(TenantSifenSettings.Create(
                    tenantId,
                    environment,
                    normalized.CscIdentifier,
                    normalized.CscSecretReference,
                    normalized.Establishment,
                    normalized.ExpeditionPoint,
                    normalized.CurrentNumber,
                    normalized.StampingNumber,
                    normalized.CertificateSecretReference,
                    normalized.CertificatePasswordSecretReference,
                    normalized.CertificateAlias,
                    normalized.XmlSchemaRootPath,
                    normalized.EndpointUrl,
                    normalized.TransportMode));
            }
            else
            {
                settings.Update(
                    environment,
                    normalized.CscIdentifier,
                    normalized.CscSecretReference,
                    normalized.Establishment,
                    normalized.ExpeditionPoint,
                    normalized.CurrentNumber,
                    normalized.StampingNumber,
                    normalized.CertificateSecretReference,
                    normalized.CertificatePasswordSecretReference,
                    normalized.CertificateAlias,
                    normalized.XmlSchemaRootPath,
                    normalized.EndpointUrl,
                    normalized.TransportMode);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            var diagnostic = await readinessReporter.GetTenantFeDiagnosticAsync(tenantId, environment, cancellationToken);

            return Results.Ok(new SifenConfigResponse(
                tenantId,
                normalized.Ruc,
                normalized.RucCheckDigit,
                normalized.LegalName,
                environment.ToString(),
                normalized.CscIdentifier,
                normalized.CscSecretReference,
                normalized.CertificateSecretReference,
                normalized.CertificatePasswordSecretReference,
                normalized.CertificateAlias,
                normalized.Establishment,
                normalized.ExpeditionPoint,
                normalized.CurrentNumber,
                normalized.StampingNumber,
                normalized.XmlSchemaRootPath,
                normalized.EndpointUrl,
                normalized.TransportMode,
                diagnostic.Summary));
        });

        return app;
    }

    private static bool IsSuperAdmin(ClaimsPrincipal user)
    {
        return user.IsInRole("SuperAdmin");
    }

    private static bool IsTenantAdmin(ClaimsPrincipal user)
    {
        return user.IsInRole("TenantAdmin");
    }

    private static void EnsureCanAccessTenant(ClaimsPrincipal user, Guid tenantId)
    {
        if (IsSuperAdmin(user))
        {
            return;
        }

        if (!IsTenantAdmin(user))
        {
            throw new DomainException("User is not allowed to manage this tenant.");
        }

        var claimValue = user.FindFirstValue("tenantId") ?? user.FindFirstValue("companyId");
        if (!Guid.TryParse(claimValue, out var userTenantId) || userTenantId != tenantId)
        {
            throw new DomainException("TenantAdmin cannot modify another tenant.");
        }
    }

    private static void EnsureCanManageTenantUsers(ClaimsPrincipal user, Guid tenantId)
    {
        EnsureCanAccessTenant(user, tenantId);

        if (IsSuperAdmin(user) || IsTenantAdmin(user))
        {
            return;
        }

        throw new DomainException("User is not allowed to manage this tenant.");
    }

    private static SifenEnvironmentType ParseEnvironment(string value)
    {
        if (Enum.TryParse<SifenEnvironmentType>(value, ignoreCase: true, out var environment))
        {
            return environment;
        }

        throw new DomainException("Environment is required and must be Test or Production.");
    }

    private static SifenEnvironmentType ParseOptionalEnvironment(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? SifenEnvironmentType.Test
            : ParseEnvironment(value);
    }

    private static NormalizedSifenConfigRequest NormalizeRequest(UpdateSifenConfigRequest request)
    {
        var normalizedRuc = NormalizeRequired(request.Ruc, "RUC").Replace(".", string.Empty).Replace(" ", string.Empty);
        var normalizedCheckDigit = NormalizeRequired(request.RucCheckDigit, "RUC check digit");
        var legalName = NormalizeRequired(request.LegalName, "Legal name");
        var establishment = NormalizeDigits(request.Establishment, 3, "Establishment");
        var expeditionPoint = NormalizeDigits(request.ExpeditionPoint, 3, "Expedition point");
        var currentNumber = NormalizeDigits(request.CurrentNumber, 7, "Current number");
        var stampingNumber = NormalizeOptionalDigits(request.StampingNumber, "Stamping number");

        if (string.IsNullOrWhiteSpace(stampingNumber))
        {
            throw new DomainException("Stamping number is required.");
        }

        return new NormalizedSifenConfigRequest(
            normalizedRuc,
            normalizedCheckDigit,
            legalName,
            NormalizeRequired(request.Environment, "Environment"),
            NormalizeOptional(request.CscIdentifier),
            NormalizeRequired(request.CscSecretReference, "CSC secret reference"),
            NormalizeOptional(request.CertificateSecretReference),
            NormalizeOptional(request.CertificatePasswordSecretReference),
            NormalizeOptional(request.CertificateAlias),
            establishment,
            expeditionPoint,
            currentNumber,
            stampingNumber,
            NormalizeOptional(request.XmlSchemaRootPath),
            NormalizeOptional(request.EndpointUrl),
            NormalizeOptional(request.TransportMode));
    }

    private static string NormalizeRequired(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException($"{fieldName} is required.");
        }

        return value.Trim();
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string NormalizeDigits(string? value, int length, string fieldName)
    {
        var digits = new string((value ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digits.Length == 0)
        {
            throw new DomainException($"{fieldName} is required.");
        }

        if (digits.Length > length)
        {
            throw new DomainException($"{fieldName} must have {length} digits.");
        }

        return digits.PadLeft(length, '0');
    }

    private static string? NormalizeOptionalDigits(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var digits = new string(value.Where(char.IsDigit).ToArray());
        if (digits.Length == 0)
        {
            throw new DomainException($"{fieldName} must contain digits.");
        }

        return digits;
    }

    public sealed record CreatePlatformCompanyRequest(
        string Slug,
        string DisplayName,
        string? PlanName,
        int? MaxInvoicesPerMonth,
        int? MaxUsers,
        string? AdminFullName,
        string? AdminEmail,
        string? AdminPassword);

    public sealed record UpdatePlatformCompanyPlanRequest(
        string? PlanName,
        int? MaxInvoicesPerMonth,
        int? MaxUsers,
        bool? Active,
        int? InvoiceLimitPerMonth = null,
        int? UserLimit = null);

    public sealed record CreatePlatformCompanyAdminRequest(
        string FullName,
        string Email,
        string Password);

    public sealed record CreatePlatformTenantUserRequest(
        string FullName,
        string Email,
        string Password,
        string Role,
        bool IsActive);

    public sealed record UpdatePlatformTenantUserRequest(
        string FullName,
        string Email,
        string Role,
        bool IsActive);

    public sealed record UpdateSifenConfigRequest(
        string Ruc,
        string RucCheckDigit,
        string LegalName,
        string Environment,
        string? CscIdentifier,
        string CscSecretReference,
        string? CertificateSecretReference,
        string? CertificatePasswordSecretReference,
        string? CertificateAlias,
        string Establishment,
        string ExpeditionPoint,
        string CurrentNumber,
        string? StampingNumber,
        string? XmlSchemaRootPath,
        string? EndpointUrl,
        string? TransportMode);

    public sealed record SifenConfigResponse(
        Guid TenantId,
        string? Ruc,
        string? RucCheckDigit,
        string? LegalName,
        string? Environment,
        string? CscIdentifier,
        string? CscSecretReference,
        string? CertificateSecretReference,
        string? CertificatePasswordSecretReference,
        string? CertificateAlias,
        string? Establishment,
        string? ExpeditionPoint,
        string? CurrentNumber,
        string? StampingNumber,
        string? XmlSchemaRootPath,
        string? EndpointUrl,
        string? TransportMode,
        string ReadySummary);

    public sealed record UpdateKudeTemplateRequest(
        string TemplateCode,
        string? LogoUrl,
        string? PrimaryColor,
        string? SecondaryColor,
        string? FooterText,
        bool ShowPhone,
        bool ShowEmail);

    public sealed record KudeTemplateResponse(
        Guid TenantId,
        string TemplateCode,
        string? LogoUrl,
        string PrimaryColor,
        string SecondaryColor,
        string FooterText,
        bool ShowPhone,
        bool ShowEmail);

    private sealed record NormalizedSifenConfigRequest(
        string Ruc,
        string RucCheckDigit,
        string LegalName,
        string Environment,
        string? CscIdentifier,
        string CscSecretReference,
        string? CertificateSecretReference,
        string? CertificatePasswordSecretReference,
        string? CertificateAlias,
        string Establishment,
        string ExpeditionPoint,
        string CurrentNumber,
        string? StampingNumber,
        string? XmlSchemaRootPath,
        string? EndpointUrl,
        string? TransportMode);
}
