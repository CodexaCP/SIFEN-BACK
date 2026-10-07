namespace SifenInvoicing.Application.Invoices;

/// <summary>
/// Item comercial. Code (dCodInt, E701) y UnitCode (cUniMed, E709, Tabla 5 del Manual v150) son obligatorios para el DE01
/// y no se inventan: si faltan, la emision se rechaza antes de reservar numero.
/// </summary>
public sealed record CreateInvoiceItemCommand(
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    int VatRate,
    string? Code = null,
    int? UnitCode = null);
