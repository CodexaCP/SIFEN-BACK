namespace SifenInvoicing.Application.Invoices;

public sealed record FeTenantLogQuery(
    Guid TenantId,
    Guid? InvoiceId,
    string? Level,
    DateTimeOffset? From,
    DateTimeOffset? To);
