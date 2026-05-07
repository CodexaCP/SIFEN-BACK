namespace SifenInvoicing.Application.Operations;

public sealed record TenantFeOperationalDiagnostic(
    Guid TenantId,
    string Environment,
    string TransportMode,
    bool ReadyForInternalValidation,
    bool ReadyForSifenTestAttempt,
    string Summary,
    IReadOnlyCollection<string> Missing,
    IReadOnlyCollection<string> Warnings,
    IReadOnlyCollection<TenantFeOperationalCheck> Checks,
    TenantFeLastSubmission? LastSubmission,
    TenantFeLastIssue? LastError);

public sealed record TenantFeOperationalCheck(
    string Code,
    string Status,
    string Message,
    bool IsReady);

public sealed record TenantFeLastSubmission(
    string Cdc,
    string Status,
    DateTimeOffset? SubmittedAt,
    string? Endpoint);

public sealed record TenantFeLastIssue(
    string Cdc,
    string Status,
    string? StatusCode,
    string? StatusMessage,
    DateTimeOffset? OccurredAt);
