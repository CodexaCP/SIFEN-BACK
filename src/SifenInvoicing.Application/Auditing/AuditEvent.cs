namespace SifenInvoicing.Application.Auditing;

public sealed record AuditEvent
{
    public required string EventName { get; init; }

    public required AuditCategory Category { get; init; }

    public required AuditSeverity Severity { get; init; }

    public required DateTimeOffset OccurredAt { get; init; }

    public string? CorrelationId { get; init; }

    public string? TenantId { get; init; }

    public string? ActorId { get; init; }

    public string? ResourceType { get; init; }

    public string? ResourceId { get; init; }

    public string? Outcome { get; init; }

    public IReadOnlyDictionary<string, string?> Metadata { get; init; } =
        new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
}
