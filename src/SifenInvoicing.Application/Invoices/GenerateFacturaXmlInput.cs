using SifenInvoicing.Application.Cdc;

namespace SifenInvoicing.Application.Invoices;

public sealed record GenerateFacturaXmlInput(
    GenerateCdcInput Cdc,
    DateTimeOffset FechaFirma,
    int SistemaFacturacion,
    string EmisorNombre,
    string EmisorDireccion,
    string ReceptorNombre,
    InvoiceReceiverDocumentType ReceptorTipoDocumento,
    string ReceptorDocumento,
    InvoiceCurrency Currency,
    InvoiceSaleCondition SaleCondition,
    IReadOnlyCollection<GenerateFacturaXmlItemInput> Items);
