namespace SifenInvoicing.Application.Invoices;

public sealed record GenerateFacturaXmlItemInput(
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    InvoiceVatType VatType);
