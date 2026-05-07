namespace SifenInvoicing.Application.Invoices;

public sealed record GeneratedFacturaXmlResult(
    string Cdc,
    string Xml,
    decimal TotalGravado10,
    decimal TotalGravado5,
    decimal TotalExento,
    decimal TotalIva,
    decimal TotalGeneral);
