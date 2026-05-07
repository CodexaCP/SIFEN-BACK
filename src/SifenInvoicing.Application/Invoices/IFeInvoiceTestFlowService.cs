namespace SifenInvoicing.Application.Invoices;

public interface IFeInvoiceTestFlowService
{
    Task<PrepareInvoiceTestResult> PrepareInvoiceInTestModeAsync(
        Guid tenantId,
        Guid invoiceId,
        CancellationToken cancellationToken = default);
}
