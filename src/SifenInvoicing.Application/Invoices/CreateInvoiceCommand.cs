using SifenInvoicing.Domain.Tenants;

namespace SifenInvoicing.Application.Invoices;

public sealed record CreateInvoiceCommand(
    SifenEnvironmentType Environment,
    string DocumentType,
    string EstablishmentCode,
    string ExpeditionPointCode,
    string DocumentNumber,
    string SecurityCode,
    DateOnly IssueDate,
    string EmisorDireccion,
    string? Notes,
    string ReceptorNombre,
    InvoiceReceiverDocumentType ReceptorTipoDocumento,
    string ReceptorDocumento,
    string? ReceptorDireccion,
    string? ReceptorEmail,
    string? ReceptorPhone,
    InvoiceCurrency Currency,
    InvoiceSaleCondition SaleCondition,
    int SistemaFacturacion,
    string TipoContribuyente,
    string TipoEmision,
    IReadOnlyCollection<CreateInvoiceItemCommand> Items);
