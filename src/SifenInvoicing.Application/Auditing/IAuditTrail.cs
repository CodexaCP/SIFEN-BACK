namespace SifenInvoicing.Application.Auditing;

public interface IAuditTrail
{
    Task RecordAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default);
}
