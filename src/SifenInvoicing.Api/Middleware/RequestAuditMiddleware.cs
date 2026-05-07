using System.Diagnostics;
using Serilog.Context;
using SifenInvoicing.Application.Auditing;
using SifenInvoicing.Application.Diagnostics;
using SifenInvoicing.Application.Tenancy;

namespace SifenInvoicing.Api.Middleware;

public sealed class RequestAuditMiddleware
{
    private readonly RequestDelegate _next;

    public RequestAuditMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        IAuditTrail auditTrail,
        ISystemClock clock,
        ITenantContextAccessor tenantContextAccessor)
    {
        var startedAt = clock.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        var correlationId = context.Items[CorrelationIdMiddleware.ItemName]?.ToString();
        var tenantContext = tenantContextAccessor.Current;

        using (LogContext.PushProperty("CorrelationId", correlationId))
        using (LogContext.PushProperty("TenantId", tenantContext.TenantId))
        using (LogContext.PushProperty("ClientId", tenantContext.ClientId))
        using (LogContext.PushProperty("TaxpayerRuc", tenantContext.TaxpayerRuc))
        {
            try
            {
                await _next(context);
                stopwatch.Stop();

                await auditTrail.RecordAsync(BuildAuditEvent(
                    context,
                    startedAt,
                    stopwatch.Elapsed,
                    correlationId,
                    tenantContext,
                    context.Response.StatusCode >= StatusCodes.Status500InternalServerError
                        ? AuditSeverity.Error
                        : AuditSeverity.Information,
                    "Completed"));
            }
            catch (Exception)
            {
                stopwatch.Stop();

                await auditTrail.RecordAsync(BuildAuditEvent(
                    context,
                    startedAt,
                    stopwatch.Elapsed,
                    correlationId,
                    tenantContext,
                    AuditSeverity.Critical,
                    "UnhandledException"));

                throw;
            }
        }
    }

    private static AuditEvent BuildAuditEvent(
        HttpContext context,
        DateTimeOffset startedAt,
        TimeSpan elapsed,
        string? correlationId,
        TenantContext tenantContext,
        AuditSeverity severity,
        string outcome)
    {
        return new AuditEvent
        {
            EventName = "api.request",
            Category = AuditCategory.ApiRequest,
            Severity = severity,
            OccurredAt = startedAt,
            CorrelationId = correlationId,
            TenantId = tenantContext.TenantId,
            ActorId = tenantContext.ClientId,
            ResourceType = "HttpRequest",
            ResourceId = context.TraceIdentifier,
            Outcome = outcome,
            Metadata = new Dictionary<string, string?>
            {
                ["http.method"] = context.Request.Method,
                ["http.path"] = context.Request.Path,
                ["http.status_code"] = context.Response.StatusCode.ToString(),
                ["duration_ms"] = elapsed.TotalMilliseconds.ToString("F2"),
                ["remote_ip"] = context.Connection.RemoteIpAddress?.ToString(),
                ["tenant.resolved"] = tenantContext.IsResolved.ToString(),
                ["taxpayer.ruc.present"] = (!string.IsNullOrWhiteSpace(tenantContext.TaxpayerRuc)).ToString()
            }
        };
    }
}
