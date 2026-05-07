using SifenInvoicing.Domain.Documents;

namespace SifenInvoicing.Application.Invoices;

public sealed record PrepareInvoiceTestResult(
    Guid InvoiceId,
    Guid TenantId,
    string CorrelationId,
    FeInvoiceInternalStatus InternalStatus,
    bool ReadyForTest,
    string Message);
