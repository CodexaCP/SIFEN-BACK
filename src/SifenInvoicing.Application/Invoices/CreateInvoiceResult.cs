using SifenInvoicing.Domain.Documents;

namespace SifenInvoicing.Application.Invoices;

public sealed record CreateInvoiceResult(
    Guid Id,
    string Cdc,
    SifenDocumentStatus Status,
    decimal TotalAmount,
    string XmlPayload,
    string? SignedXmlPayload,
    string? StatusCode,
    string? StatusMessage,
    string InternalStatus,
    string CorrelationId,
    string Message)
{
    public Guid InvoiceId => Id;
}
