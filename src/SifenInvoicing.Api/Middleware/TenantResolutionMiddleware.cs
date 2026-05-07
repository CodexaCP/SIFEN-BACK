using SifenInvoicing.Application.Tenancy;

namespace SifenInvoicing.Api.Middleware;

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

    public async Task InvokeAsync(HttpContext context, ITenantContextAccessor tenantContextAccessor)
    {
        var tenantContext = new TenantContext
        {
            TenantId = ReadHeader(context, TenantHeaderName),
            ResolvedTenantId = TryParseTenantId(ReadHeader(context, TenantHeaderName)),
            ClientId = ReadHeader(context, ClientHeaderName),
            TaxpayerRuc = ReadHeader(context, TaxpayerRucHeaderName),
            IsResolved = HasAnyTenantSignal(context)
        };

        tenantContextAccessor.SetCurrent(tenantContext);

        try
        {
            await _next(context);
        }
        finally
        {
            tenantContextAccessor.Clear();
        }
    }

    private static bool HasAnyTenantSignal(HttpContext context)
    {
        return context.Request.Headers.ContainsKey(TenantHeaderName)
            || context.Request.Headers.ContainsKey(ClientHeaderName)
            || context.Request.Headers.ContainsKey(TaxpayerRucHeaderName);
    }

    private static string? ReadHeader(HttpContext context, string headerName)
    {
        return context.Request.Headers.TryGetValue(headerName, out var value)
            ? NormalizeHeaderValue(value.ToString())
            : null;
    }

    private static string? NormalizeHeaderValue(string value)
    {
        var normalized = value.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static Guid? TryParseTenantId(string? tenantId)
    {
        return Guid.TryParse(tenantId, out var parsed) ? parsed : null;
    }
}
