using System.Diagnostics;

namespace SifenInvoicing.Api.Middleware;

public sealed class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-Id";
    public const string ItemName = "CorrelationId";

    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = ResolveCorrelationId(context);
        context.Items[ItemName] = correlationId;
        context.Response.Headers[HeaderName] = correlationId;

        using var activity = Activity.Current;
        await _next(context);
    }

    private static string ResolveCorrelationId(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(HeaderName, out var headerValue))
        {
            var value = headerValue.ToString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");
    }
}
