namespace SifenInvoicing.Application.Tenancy;

public sealed record TenantContext
{
    public static readonly TenantContext Empty = new()
    {
        TenantId = null,
        ClientId = null,
        TaxpayerRuc = null,
        IsResolved = false
    };

    public string? TenantId { get; init; }

    public Guid? ResolvedTenantId { get; init; }

    public string? ClientId { get; init; }

    public string? TaxpayerRuc { get; init; }

    public bool IsResolved { get; init; }
}
