using SifenInvoicing.Domain.PlatformAuth;

namespace SifenInvoicing.Api.Auth;

/// <summary>Politicas de autorizacion de la API fiscal. Todas exigen usuario autenticado.</summary>
public static class ApiAuthorization
{
    public const string TenantClaimType = "tenantId";
    public const string PermissionClaimType = "permission";

    /// <summary>Emitir/reintentar: invoices.issue.own-tenant o invoices.issue.any-tenant.</summary>
    public const string InvoicesIssuePolicy = "invoices.issue";

    /// <summary>Leer: invoices.read.own-tenant o invoices.issue.any-tenant.</summary>
    public const string InvoicesReadPolicy = "invoices.read";

    /// <summary>Alta de empresas/tenants: companies.create (solo plataforma).</summary>
    public const string CompaniesCreatePolicy = "companies.create";

    /// <summary>Configuracion fiscal/SIFEN: sifen.configure (ademas se exige acceso al tenant objetivo).</summary>
    public const string SifenConfigurePolicy = "sifen.configure";

    public static IServiceCollection AddApiAuthorization(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            options.AddPolicy(InvoicesIssuePolicy, policy => policy
                .RequireAuthenticatedUser()
                .RequireAssertion(context => HasAnyPermission(
                    context.User,
                    PlatformPermissions.InvoicesIssueOwnTenant,
                    PlatformPermissions.InvoicesIssueAnyTenant)));

            options.AddPolicy(CompaniesCreatePolicy, policy => policy
                .RequireAuthenticatedUser()
                .RequireAssertion(context => HasPermission(context.User, PlatformPermissions.CompaniesCreate)));

            options.AddPolicy(SifenConfigurePolicy, policy => policy
                .RequireAuthenticatedUser()
                .RequireAssertion(context => HasPermission(context.User, PlatformPermissions.SifenConfigure)));

            options.AddPolicy(InvoicesReadPolicy, policy => policy
                .RequireAuthenticatedUser()
                .RequireAssertion(context => HasAnyPermission(
                    context.User,
                    PlatformPermissions.InvoicesReadOwnTenant,
                    PlatformPermissions.InvoicesIssueOwnTenant,
                    PlatformPermissions.InvoicesIssueAnyTenant)));
        });

        return services;
    }

    /// <summary>
    /// Acceso a un tenant concreto: el claim tenantId debe coincidir; unica excepcion explicita:
    /// usuarios de plataforma con companies.create (superadmin), que operan sobre cualquier tenant.
    /// </summary>
    public static bool CanAccessTenant(System.Security.Claims.ClaimsPrincipal user, Guid tenantId)
    {
        if (Guid.TryParse(user.FindFirst(TenantClaimType)?.Value, out var claimTenant))
        {
            return claimTenant == tenantId;
        }

        return HasPermission(user, PlatformPermissions.CompaniesCreate);
    }

    public static bool HasPermission(System.Security.Claims.ClaimsPrincipal user, string permission) =>
        user.HasClaim(PermissionClaimType, permission);

    private static bool HasAnyPermission(System.Security.Claims.ClaimsPrincipal user, params string[] permissions) =>
        permissions.Any(permission => HasPermission(user, permission));
}
