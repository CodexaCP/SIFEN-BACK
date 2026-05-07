using SifenInvoicing.Domain.Documents;

namespace SifenInvoicing.Application.Invoices;

public interface IFeTraceService
{
    Task AddInvoiceEventAsync(
        Guid tenantId,
        Guid invoiceId,
        string correlationId,
        string? previousStatus,
        string newStatus,
        string eventType,
        string message,
        string? technicalDetail,
        CancellationToken cancellationToken = default);

    Task AddTenantLogAsync(
        Guid tenantId,
        Guid? invoiceId,
        string? correlationId,
        FeTenantLogLevel level,
        string source,
        string message,
        string? technicalDetail,
        CancellationToken cancellationToken = default);

    Task<string> ChangeInvoiceStatusAsync(
        Guid tenantId,
        Guid invoiceId,
        FeInvoiceInternalStatus newStatus,
        string eventType,
        string message,
        string? technicalDetail = null,
        string? lastErrorCode = null,
        string? lastErrorMessage = null,
        bool isRetryable = false,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<FeInvoiceEventItem>> GetInvoiceEventsAsync(
        Guid tenantId,
        Guid invoiceId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<FeTenantLogItem>> GetTenantLogsAsync(
        FeTenantLogQuery query,
        CancellationToken cancellationToken = default);
}
