namespace SifenInvoicing.Application.Invoices;

public sealed record CreateInvoiceItemCommand(
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    int VatRate);
