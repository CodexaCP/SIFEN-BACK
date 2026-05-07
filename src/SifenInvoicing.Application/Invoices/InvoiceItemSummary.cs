namespace SifenInvoicing.Application.Invoices;

public sealed record InvoiceItemSummary(
    int LineNumber,
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    int VatRate,
    decimal VatAmount,
    decimal ExemptAmount,
    decimal SubtotalAmount,
    decimal TotalAmount);
