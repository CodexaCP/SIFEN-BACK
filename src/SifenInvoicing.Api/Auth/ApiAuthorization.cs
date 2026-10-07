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

    public static bool HasPermission(System.Security.Claims.ClaimsPrincipal user, string permission) =>
        user.HasClaim(PermissionClaimType, permission);

    private static bool HasAnyPermission(System.Security.Claims.ClaimsPrincipal user, params string[] permissions) =>
        permissions.Any(permission => HasPermission(user, permission));
}
