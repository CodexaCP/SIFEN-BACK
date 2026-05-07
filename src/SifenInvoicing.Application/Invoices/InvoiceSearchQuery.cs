using SifenInvoicing.Domain.Documents;

namespace SifenInvoicing.Application.Invoices;

public sealed record InvoiceSearchQuery(
    SifenDocumentStatus? Status,
    string? Cdc,
    DateOnly? DateFrom,
    DateOnly? DateTo,
    string? ExternalDocumentNumber);
