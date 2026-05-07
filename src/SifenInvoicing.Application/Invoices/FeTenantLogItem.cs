namespace SifenInvoicing.Application.Invoices;

public sealed record FeTenantLogItem(
    Guid Id,
    Guid TenantId,
    Guid? InvoiceId,
    string? CorrelationId,
    string Level,
    string Source,
    string Message,
    string? TechnicalDetail,
    DateTimeOffset CreatedAt);
