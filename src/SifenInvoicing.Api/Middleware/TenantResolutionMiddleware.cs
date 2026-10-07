using SifenInvoicing.Api.Auth;
using SifenInvoicing.Application.Tenancy;
using SifenInvoicing.Domain.PlatformAuth;

namespace SifenInvoicing.Api.Middleware;

/// <summary>
/// Deriva el tenant de las credenciales autenticadas (claim "tenantId"), nunca de cabeceras libres.
/// Debe ejecutarse DESPUES de UseAuthentication.
/// - Sin autenticacion: el contexto queda sin tenant (los datos con filtro por tenant no son visibles).
/// - Usuario de tenant: X-Tenant-Id solo puede coincidir con el claim; si difiere -> 403.
/// - Usuario con invoices.issue.any-tenant y sin claim: puede indicar el tenant con X-Tenant-Id (queda en el log).
/// X-Client-Id y X-Taxpayer-Ruc son informativos (auditoria), no otorgan acceso.
/// </summary>
public sealed class TenantResolutionMiddleware
{
    public const string TenantHeaderName = "X-Tenant-Id";
    public const string ClientHeaderName = "X-Client-Id";
    public const string TaxpayerRucHeaderName = "X-Taxpayer-Ruc";

    private readonly RequestDelegate _next;

    public TenantResolutionMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        ITenantContextAccessor tenantContextAccessor,
        ILogger<TenantResolutionMiddleware> logger)
    {
        var user = context.User;
        var headerTenant = ReadHeader(context, TenantHeaderName);
        Guid? tenantId = null;

        if (user.Identity?.IsAuthenticated == true)
        {
            var claimTenant = user.FindFirst(ApiAuthorization.TenantClaimType)?.Value;

            if (!string.IsNullOrWhiteSpace(claimTenant))
            {
                if (!Guid.TryParse(claimTenant, out var parsedClaim))
                {
                    await WriteForbiddenAsync(context, "Invalid tenant credential.");
                    return;
                }

                if (headerTenant is not null &&
                    (!Guid.TryParse(headerTenant, out var parsedHeader) || parsedHeader != parsedClaim))
                {
                    logger.LogWarning(
                        "Rejected request: {Header} does not match the authenticated tenant {TenantId}.",
                        TenantHeaderName,
                        parsedClaim);
                    await WriteForbiddenAsync(context, "Tenant header does not match the authenticated tenant.");
                    return;
                }

                tenantId = parsedClaim;
            }
            else if (headerTenant is not null &&
                     ApiAuthorization.HasPermission(user, PlatformPermissions.InvoicesIssueAnyTenant) &&
                     Guid.TryParse(headerTenant, out var platformTenant))
            {
                logger.LogInformation(
                    "Platform user {User} acting on tenant {TenantId}.",
                    user.FindFirst("sub")?.Value,
                    platformTenant);
                tenantId = platformTenant;
            }
        }

        tenantContextAccessor.SetCurrent(new TenantContext
        {
            TenantId = tenantId?.ToString(),
            ResolvedTenantId = tenantId,
            ClientId = ReadHeader(context, ClientHeaderName),
            TaxpayerRuc = ReadHeader(context, TaxpayerRucHeaderName),
            IsResolved = tenantId.HasValue
        });

        try
        {
            await _next(context);
        }
        finally
        {
            tenantContextAccessor.Clear();
        }
    }

    private static Task WriteForbiddenAsync(HttpContext context, string message)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return context.Response.WriteAsJsonAsync(new { error = message });
    }

    private static string? ReadHeader(HttpContext context, string headerName)
    {
        if (!context.Request.Headers.TryGetValue(headerName, out var value))
        {
            return null;
        }

        var normalized = value.ToString().Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}
