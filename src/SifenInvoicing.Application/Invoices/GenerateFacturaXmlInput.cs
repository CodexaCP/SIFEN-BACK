using SifenInvoicing.Application.Cdc;
using SifenInvoicing.Application.Fiscal;

namespace SifenInvoicing.Application.Invoices;

/// <summary>
/// Entrada del generador XML. Los montos llegan ya calculados en <see cref="Fiscal"/>
/// (FiscalCalculationEngine): el generador no calcula.
/// </summary>
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
    FiscalDocumentModel Fiscal,
    IReadOnlyList<string> ItemDescriptions);
