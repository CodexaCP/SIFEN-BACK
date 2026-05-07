namespace SifenInvoicing.Application.Invoices;

public interface IInvoiceService
{
    Task<CreateInvoiceResult> CreateAsync(
        CreateInvoiceCommand command,
        CancellationToken cancellationToken = default);

    Task<InvoiceDetail?> GetByIdAsync(
        Guid invoiceId,
        CancellationToken cancellationToken = default);

    Task<InvoiceDetail?> GetByCdcAsync(
        string cdc,
        CancellationToken cancellationToken = default);

    Task<InvoiceKudePdfResult?> GenerateKudePdfAsync(
        Guid invoiceId,
        CancellationToken cancellationToken = default);

    Task<InvoiceRetryResult> RetryAsync(
        Guid invoiceId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<InvoiceListItem>> SearchAsync(
        InvoiceSearchQuery query,
        CancellationToken cancellationToken = default);
}
