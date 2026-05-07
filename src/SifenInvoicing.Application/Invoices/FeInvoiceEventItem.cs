namespace SifenInvoicing.Application.Invoices;

public sealed record FeInvoiceEventItem(
    Guid Id,
    Guid InvoiceId,
    string CorrelationId,
    string? PreviousStatus,
    string NewStatus,
    string EventType,
    string Message,
    string? TechnicalDetail,
    DateTimeOffset CreatedAt);
