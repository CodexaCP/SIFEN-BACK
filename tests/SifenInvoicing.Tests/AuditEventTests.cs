using SifenInvoicing.Application.Auditing;

namespace SifenInvoicing.Tests;

public sealed class AuditEventTests
{
    [Fact]
    public void AuditEvent_ShouldCarryCorrelationAndTenantContext()
    {
        var auditEvent = new AuditEvent
        {
            EventName = "api.request",
            Category = AuditCategory.ApiRequest,
            Severity = AuditSeverity.Information,
            OccurredAt = DateTimeOffset.UtcNow,
            CorrelationId = "corr-123",
            TenantId = "tenant-abc",
            ActorId = "client-001",
            ResourceType = "HttpRequest",
            ResourceId = "trace-001",
            Outcome = "Completed",
            Metadata = new Dictionary<string, string?>
            {
                ["http.status_code"] = "200"
            }
        };

        Assert.Equal("corr-123", auditEvent.CorrelationId);
        Assert.Equal("tenant-abc", auditEvent.TenantId);
        Assert.Equal("client-001", auditEvent.ActorId);
        Assert.Equal("200", auditEvent.Metadata["http.status_code"]);
    }
}
