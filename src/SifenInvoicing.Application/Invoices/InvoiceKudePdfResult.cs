namespace SifenInvoicing.Application.Invoices;

public sealed record InvoiceKudePdfResult(
    string FileName,
    byte[] Content,
    string QrPayload,
    bool HasQr);
