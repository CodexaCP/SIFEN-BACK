namespace SifenInvoicing.Application.Operations;

public sealed record OperationalDependencyStatus
{
    public required string Name { get; init; }

    public required DependencyKind Kind { get; init; }

    public required DependencyState State { get; init; }

    public required DateTimeOffset CheckedAt { get; init; }

    public string? TenantId { get; init; }

    public string? Summary { get; init; }

    public string? DiagnosticHint { get; init; }

    public IReadOnlyDictionary<string, string?> Data { get; init; } =
        new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
}
