using Microsoft.EntityFrameworkCore;
using SifenInvoicing.Application.Diagnostics;
using SifenInvoicing.Application.Invoices;
using SifenInvoicing.Domain.Common;
using SifenInvoicing.Domain.Documents;
using SifenInvoicing.Infrastructure.Persistence;

namespace SifenInvoicing.Infrastructure.Invoices;

public sealed class EfFeTraceService : IFeTraceService
{
    private readonly SifenDbContext _dbContext;
    private readonly ISystemClock _clock;

    public EfFeTraceService(
        SifenDbContext dbContext,
        ISystemClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    public async Task AddInvoiceEventAsync(
        Guid tenantId,
        Guid invoiceId,
        string correlationId,
        string? previousStatus,
        string newStatus,
        string eventType,
        string message,
        string? technicalDetail,
        CancellationToken cancellationToken = default)
    {
        _dbContext.FeInvoiceEvents.Add(FeInvoiceEvent.Create(
            tenantId,
            invoiceId,
            correlationId,
            previousStatus,
            newStatus,
            eventType,
            message,
            SanitizeTechnicalDetail(technicalDetail),
            _clock.UtcNow));

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task AddTenantLogAsync(
        Guid tenantId,
        Guid? invoiceId,
        string? correlationId,
        FeTenantLogLevel level,
        string source,
        string message,
        string? technicalDetail,
        CancellationToken cancellationToken = default)
    {
        _dbContext.FeTenantLogs.Add(FeTenantLog.Create(
            tenantId,
            invoiceId,
            NormalizeOptional(correlationId),
            level,
            source,
            message,
            SanitizeTechnicalDetail(technicalDetail),
            _clock.UtcNow));

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<string> ChangeInvoiceStatusAsync(
        Guid tenantId,
        Guid invoiceId,
        FeInvoiceInternalStatus newStatus,
        string eventType,
        string message,
        string? technicalDetail = null,
        string? lastErrorCode = null,
        string? lastErrorMessage = null,
        bool isRetryable = false,
        CancellationToken cancellationToken = default)
    {
        var document = await _dbContext.Documents
            .FirstOrDefaultAsync(item => item.Id == invoiceId && item.TenantId == tenantId, cancellationToken);

        if (document is null)
        {
            throw new DomainException("Invoice was not found for the current tenant.");
        }

        var previousStatus = document.InternalStatus.ToString();
        var correlationId = document.EnsureCorrelationId();
        document.SetInternalStatus(newStatus, lastErrorCode, lastErrorMessage, isRetryable);

        _dbContext.FeInvoiceEvents.Add(FeInvoiceEvent.Create(
            tenantId,
            invoiceId,
            correlationId,
            previousStatus,
            newStatus.ToString(),
            eventType,
            message,
            SanitizeTechnicalDetail(technicalDetail),
            _clock.UtcNow));

        _dbContext.FeTenantLogs.Add(FeTenantLog.Create(
            tenantId,
            invoiceId,
            correlationId,
            newStatus is FeInvoiceInternalStatus.TEST_ERROR or FeInvoiceInternalStatus.BLOCKED_BY_CONFIG
                ? FeTenantLogLevel.WARNING
                : FeTenantLogLevel.INFO,
            "fe.test.flow",
            message,
            SanitizeTechnicalDetail(technicalDetail),
            _clock.UtcNow));

        await _dbContext.SaveChangesAsync(cancellationToken);
        return correlationId;
    }

    public async Task<IReadOnlyCollection<FeInvoiceEventItem>> GetInvoiceEventsAsync(
        Guid tenantId,
        Guid invoiceId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.FeInvoiceEvents
            .AsNoTracking()
            .Where(item => item.TenantId == tenantId && item.InvoiceId == invoiceId)
            .OrderBy(item => item.CreatedAt)
            .Select(item => new FeInvoiceEventItem(
                item.Id,
                item.InvoiceId,
                item.CorrelationId,
                item.PreviousStatus,
                item.NewStatus,
                item.EventType,
                item.Message,
                item.TechnicalDetail,
                item.CreatedAt))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<FeTenantLogItem>> GetTenantLogsAsync(
        FeTenantLogQuery query,
        CancellationToken cancellationToken = default)
    {
        IQueryable<FeTenantLog> logs = _dbContext.FeTenantLogs
            .AsNoTracking()
            .Where(item => item.TenantId == query.TenantId);

        if (query.InvoiceId.HasValue)
        {
            logs = logs.Where(item => item.InvoiceId == query.InvoiceId.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Level) &&
            Enum.TryParse<FeTenantLogLevel>(query.Level.Trim(), true, out var parsedLevel))
        {
            logs = logs.Where(item => item.Level == parsedLevel);
        }

        if (query.From.HasValue)
        {
            logs = logs.Where(item => item.CreatedAt >= query.From.Value);
        }

        if (query.To.HasValue)
        {
            logs = logs.Where(item => item.CreatedAt <= query.To.Value);
        }

        return await logs
            .OrderByDescending(item => item.CreatedAt)
            .Take(200)
            .Select(item => new FeTenantLogItem(
                item.Id,
                item.TenantId,
                item.InvoiceId,
                item.CorrelationId,
                item.Level.ToString(),
                item.Source,
                item.Message,
                item.TechnicalDetail,
                item.CreatedAt))
            .ToArrayAsync(cancellationToken);
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? SanitizeTechnicalDetail(string? technicalDetail)
    {
        if (string.IsNullOrWhiteSpace(technicalDetail))
        {
            return null;
        }

        var normalized = technicalDetail.Trim();
        var lowered = normalized.ToLowerInvariant();
        var sensitiveTokens = new[] { "password", "secret", "certificate", "csc" };
        if (sensitiveTokens.Any(lowered.Contains))
        {
            return "Sensitive configuration detail was suppressed.";
        }

        return normalized.Length <= 4000 ? normalized : normalized[..4000];
    }
}
