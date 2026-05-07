using Microsoft.Extensions.Logging;
using SifenInvoicing.Application.Auditing;

namespace SifenInvoicing.Infrastructure.Auditing;

public sealed class LoggerAuditTrail : IAuditTrail
{
    private readonly ILogger<LoggerAuditTrail> _logger;

    public LoggerAuditTrail(ILogger<LoggerAuditTrail> logger)
    {
        _logger = logger;
    }

    public Task RecordAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);

        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["AuditEventName"] = auditEvent.EventName,
            ["AuditCategory"] = auditEvent.Category.ToString(),
            ["CorrelationId"] = auditEvent.CorrelationId,
            ["TenantId"] = auditEvent.TenantId,
            ["ActorId"] = auditEvent.ActorId,
            ["ResourceType"] = auditEvent.ResourceType,
            ["ResourceId"] = auditEvent.ResourceId,
            ["Outcome"] = auditEvent.Outcome
        });

        var message = "Audit event {AuditEventName} for {AuditCategory} ended with {Outcome}";

        switch (auditEvent.Severity)
        {
            case AuditSeverity.Trace:
                _logger.LogTrace(message, auditEvent.EventName, auditEvent.Category, auditEvent.Outcome);
                break;
            case AuditSeverity.Information:
                _logger.LogInformation(message, auditEvent.EventName, auditEvent.Category, auditEvent.Outcome);
                break;
            case AuditSeverity.Warning:
                _logger.LogWarning(message, auditEvent.EventName, auditEvent.Category, auditEvent.Outcome);
                break;
            case AuditSeverity.Error:
                _logger.LogError(message, auditEvent.EventName, auditEvent.Category, auditEvent.Outcome);
                break;
            case AuditSeverity.Critical:
                _logger.LogCritical(message, auditEvent.EventName, auditEvent.Category, auditEvent.Outcome);
                break;
            default:
                _logger.LogInformation(message, auditEvent.EventName, auditEvent.Category, auditEvent.Outcome);
                break;
        }

        if (auditEvent.Metadata.Count > 0)
        {
            _logger.LogInformation("Audit metadata: {@AuditMetadata}", auditEvent.Metadata);
        }

        return Task.CompletedTask;
    }
}
