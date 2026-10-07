using System.Security.Claims;
using SifenInvoicing.Api.Auth;
using SifenInvoicing.Application.Onboarding;
using SifenInvoicing.Domain.Tenants;

namespace SifenInvoicing.Api.Endpoints;

public static class OnboardingEndpoints
{
    public static IEndpointRouteBuilder MapOnboardingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/internal/onboarding")
            .WithTags("Internal Onboarding")
            .RequireAuthorization(ApiAuthorization.SifenConfigurePolicy)
            .AddEndpointFilter(async (context, next) =>
            {
                // Rutas con {tenantId}: solo el propio tenant o plataforma (companies.create).
                var user = context.HttpContext.User;
                if (context.HttpContext.Request.RouteValues.TryGetValue("tenantId", out var raw) &&
                    Guid.TryParse(raw?.ToString(), out var routeTenant) &&
                    !ApiAuthorization.CanAccessTenant(user, routeTenant))
                {
                    return Results.Forbid();
                }

                return await next(context);
            });

        group.MapPost("/tenants", async (
            CreateTenantRequest request,
            ITenantOnboardingService onboardingService,
            CancellationToken cancellationToken) =>
        {
            var result = await onboardingService.CreateTenantAsync(
                new CreateTenantCommand(
                    request.Slug,
                    request.DisplayName,
                    request.MaxInvoicesPerMonth,
                    request.MaxUsers),
                cancellationToken);

            return Results.Created($"/internal/onboarding/tenants/{result.Id}", result);
        }).RequireAuthorization(ApiAuthorization.CompaniesCreatePolicy);

        group.MapPost("/tenants/{tenantId:guid}/taxpayer-profile", async (
            Guid tenantId,
            RegisterTaxpayerProfileRequest request,
            ITenantOnboardingService onboardingService,
            CancellationToken cancellationToken) =>
        {
            await onboardingService.RegisterTaxpayerProfileAsync(
                new RegisterTaxpayerProfileCommand(
                    tenantId,
                    request.RucNumber,
                    request.RucCheckDigit,
                    request.LegalName,
                    request.TradeName),
                cancellationToken);

            return Results.NoContent();
        });

        group.MapPost("/tenants/{tenantId:guid}/fiscal-profile", async (
            Guid tenantId,
            RegisterFiscalProfileRequest request,
            ITenantOnboardingService onboardingService,
            CancellationToken cancellationToken) =>
        {
            await onboardingService.RegisterFiscalProfileAsync(
                new RegisterFiscalProfileCommand(
                    tenantId,
                    request.TaxpayerType,
                    request.Address,
                    request.HouseNumber,
                    request.DepartmentCode,
                    request.DepartmentDescription,
                    request.DistrictCode,
                    request.DistrictDescription,
                    request.CityCode,
                    request.CityDescription,
                    request.Phone,
                    request.Email),
                cancellationToken);

            return Results.NoContent();
        });

        group.MapPost("/tenants/{tenantId:guid}/fiscal-stamps", async (
            Guid tenantId,
            RegisterFiscalStampRequest request,
            ITenantOnboardingService onboardingService,
            CancellationToken cancellationToken) =>
        {
            if (!TryParseEnvironment(request.Environment, out var environment))
            {
                return Results.BadRequest(new { error = "Invalid SIFEN environment." });
            }

            await onboardingService.RegisterFiscalStampAsync(
                new RegisterFiscalStampCommand(tenantId, environment, request.StampingNumber, request.ValidFrom, request.ValidTo),
                cancellationToken);

            return Results.NoContent();
        });

        group.MapPost("/tenants/{tenantId:guid}/numbering-sequences", async (
            Guid tenantId,
            RegisterNumberingSequenceRequest request,
            ITenantOnboardingService onboardingService,
            CancellationToken cancellationToken) =>
        {
            if (!TryParseEnvironment(request.Environment, out var environment))
            {
                return Results.BadRequest(new { error = "Invalid SIFEN environment." });
            }

            await onboardingService.RegisterNumberingSequenceAsync(
                new RegisterNumberingSequenceCommand(
                    tenantId,
                    environment,
                    request.StampingNumber,
                    request.DocumentTypeCode ?? "01",
                    request.EstablishmentCode,
                    request.ExpeditionPointCode,
                    request.Series,
                    request.FirstNumber ?? 1),
                cancellationToken);

            return Results.NoContent();
        });

        group.MapPost("/tenants/{tenantId:guid}/sifen-settings", async (
            Guid tenantId,
            RegisterSifenSettingsRequest request,
            ITenantOnboardingService onboardingService,
            CancellationToken cancellationToken) =>
        {
            if (!TryParseEnvironment(request.Environment, out var environment))
            {
                return Results.BadRequest(new { error = "Invalid SIFEN environment." });
            }

            await onboardingService.RegisterSifenSettingsAsync(
                new RegisterSifenSettingsCommand(
                    tenantId,
                    environment,
                    request.CscIdentifier,
                    request.CscSecretReference,
                    request.EstablishmentCode,
                    request.ExpeditionPointCode,
                    request.CurrentDocumentNumber,
                    request.StampingNumber,
                    request.CertificateSecretReference,
                    request.CertificatePasswordSecretReference,
                    request.CertificateAlias,
                    request.XmlSchemaRootPath,
                    request.EndpointUrl,
                    request.TransportMode),
                cancellationToken);

            return Results.NoContent();
        });

        group.MapPost("/tenants/{tenantId:guid}/certificates", async (
            Guid tenantId,
            RegisterCertificateMetadataRequest request,
            ITenantOnboardingService onboardingService,
            CancellationToken cancellationToken) =>
        {
            if (!TryParseEnvironment(request.Environment, out var environment))
            {
                return Results.BadRequest(new { error = "Invalid SIFEN environment." });
            }

            if (!Enum.TryParse<CertificatePurpose>(request.Purpose, ignoreCase: true, out var purpose))
            {
                return Results.BadRequest(new { error = "Invalid certificate purpose." });
            }

            await onboardingService.RegisterCertificateMetadataAsync(
                new RegisterCertificateMetadataCommand(
                    tenantId,
                    environment,
                    purpose,
                    request.Alias,
                    request.Subject,
                    request.FingerprintSha256,
                    request.SerialNumber,
                    request.CertificateSecretReference,
                    request.CertificatePasswordSecretReference,
                    request.ValidFrom,
                    request.ValidTo),
                cancellationToken);

            return Results.NoContent();
        });

        group.MapGet("/tenants/{tenantId:guid}/readiness", async (
            Guid tenantId,
            string? environment,
            ITenantOnboardingService onboardingService,
            CancellationToken cancellationToken) =>
        {
            if (!TryParseEnvironment(environment ?? "Test", out var parsedEnvironment))
            {
                return Results.BadRequest(new { error = "Invalid SIFEN environment." });
            }

            var report = await onboardingService.GetReadinessAsync(
                tenantId,
                parsedEnvironment,
                cancellationToken);

            return Results.Ok(report);
        });

        return app;
    }

    private static bool TryParseEnvironment(string value, out SifenEnvironmentType environment)
    {
        return Enum.TryParse(value, ignoreCase: true, out environment);
    }

    public sealed record CreateTenantRequest(
        string Slug,
        string DisplayName,
        int? MaxInvoicesPerMonth,
        int? MaxUsers);

    public sealed record RegisterTaxpayerProfileRequest(
        string RucNumber,
        string RucCheckDigit,
        string LegalName,
        string? TradeName);

    public sealed record RegisterFiscalProfileRequest(
        int TaxpayerType,
        string Address,
        string? HouseNumber,
        string? DepartmentCode,
        string? DepartmentDescription,
        string? DistrictCode,
        string? DistrictDescription,
        string? CityCode,
        string? CityDescription,
        string? Phone,
        string? Email);

    public sealed record RegisterFiscalStampRequest(
        string Environment,
        string StampingNumber,
        DateOnly ValidFrom,
        DateOnly? ValidTo);

    public sealed record RegisterNumberingSequenceRequest(
        string Environment,
        string StampingNumber,
        string? DocumentTypeCode,
        string EstablishmentCode,
        string ExpeditionPointCode,
        string? Series,
        long? FirstNumber);

    public sealed record RegisterSifenSettingsRequest(
        string Environment,
        string? CscIdentifier,
        string CscSecretReference,
        string EstablishmentCode,
        string ExpeditionPointCode,
        string CurrentDocumentNumber,
        string? StampingNumber,
        string? CertificateSecretReference,
        string? CertificatePasswordSecretReference,
        string? CertificateAlias,
        string? XmlSchemaRootPath,
        string? EndpointUrl,
        string? TransportMode);

    public sealed record RegisterCertificateMetadataRequest(
        string Environment,
        string Purpose,
        string Alias,
        string Subject,
        string FingerprintSha256,
        string SerialNumber,
        string CertificateSecretReference,
        string CertificatePasswordSecretReference,
        DateTimeOffset ValidFrom,
        DateTimeOffset ValidTo);
}
