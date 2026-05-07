using Microsoft.EntityFrameworkCore;
using SifenInvoicing.Application.Invoices;
using SifenInvoicing.Application.Operations;
using SifenInvoicing.Domain.Common;
using SifenInvoicing.Domain.Documents;
using SifenInvoicing.Domain.Tenants;
using SifenInvoicing.Infrastructure.Persistence;

namespace SifenInvoicing.Infrastructure.Invoices;

public sealed class EfFeInvoiceTestFlowService : IFeInvoiceTestFlowService
{
    private static readonly HashSet<string> CriticalDiagnosticKeys =
    [
        "tenant-active",
        "plan-active",
        "monthly-limit",
        "ruc-configured",
        "establishment-configured",
        "expedition-point-configured",
        "numbering-configured"
    ];

    private readonly SifenDbContext _dbContext;
    private readonly IFeTraceService _traceService;
    private readonly IFeTenantDiagnosticService _diagnosticService;

    public EfFeInvoiceTestFlowService(
        SifenDbContext dbContext,
        IFeTraceService traceService,
        IFeTenantDiagnosticService diagnosticService)
    {
        _dbContext = dbContext;
        _traceService = traceService;
        _diagnosticService = diagnosticService;
    }

    public async Task<PrepareInvoiceTestResult> PrepareInvoiceInTestModeAsync(
        Guid tenantId,
        Guid invoiceId,
        CancellationToken cancellationToken = default)
    {
        var invoice = await _dbContext.Documents
            .FirstOrDefaultAsync(item => item.Id == invoiceId && item.TenantId == tenantId, cancellationToken);

        if (invoice is null)
        {
            throw new DomainException("Invoice was not found for the current tenant.");
        }

        var diagnostic = await _diagnosticService.GetDiagnosticAsync(tenantId, cancellationToken);
        var criticalErrors = diagnostic.Checks
            .Where(item => CriticalDiagnosticKeys.Contains(item.Key) && item.Status == "ERROR")
            .Select(item => item.Message)
            .ToArray();

        var correlationId = invoice.EnsureCorrelationId();
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _traceService.ChangeInvoiceStatusAsync(
            tenantId,
            invoiceId,
            FeInvoiceInternalStatus.GENERATED,
            "test.prepare.started",
            "La factura entro al flujo interno TEST.",
            "Modo TEST interno: no se firma ni se envia a SIFEN.",
            cancellationToken: cancellationToken);

        if (criticalErrors.Length > 0)
        {
            var blockedMessage = criticalErrors[0];
            correlationId = await _traceService.ChangeInvoiceStatusAsync(
                tenantId,
                invoiceId,
                FeInvoiceInternalStatus.BLOCKED_BY_CONFIG,
                "test.prepare.blocked",
                "La factura no puede prepararse en TEST porque falta configuracion critica.",
                blockedMessage,
                "TEST_BLOCKED_BY_CONFIG",
                blockedMessage,
                cancellationToken: cancellationToken);

            await _traceService.AddTenantLogAsync(
                tenantId,
                invoiceId,
                correlationId,
                FeTenantLogLevel.WARNING,
                "fe.diagnostic",
                "La preparacion TEST quedo bloqueada por configuracion critica.",
                blockedMessage,
                cancellationToken);

            return new PrepareInvoiceTestResult(
                invoiceId,
                tenantId,
                correlationId,
                FeInvoiceInternalStatus.BLOCKED_BY_CONFIG,
                false,
                blockedMessage);
        }

        if (string.IsNullOrWhiteSpace(invoice.XmlPayload))
        {
            correlationId = await _traceService.ChangeInvoiceStatusAsync(
                tenantId,
                invoiceId,
                FeInvoiceInternalStatus.TEST_ERROR,
                "test.prepare.error",
                "La factura no tiene XML base para validar en TEST.",
                "El XML persistido esta vacio.",
                "TEST_XML_MISSING",
                "Falta el XML base de la factura.",
                cancellationToken: cancellationToken);

            await _traceService.AddTenantLogAsync(
                tenantId,
                invoiceId,
                correlationId,
                FeTenantLogLevel.ERROR,
                "fe.test.flow",
                "La preparacion TEST termino con error interno.",
                "El XML persistido esta vacio.",
                cancellationToken);

            return new PrepareInvoiceTestResult(
                invoiceId,
                tenantId,
                correlationId,
                FeInvoiceInternalStatus.TEST_ERROR,
                false,
                "La factura no tiene XML base para validacion interna.");
        }

        correlationId = await _traceService.ChangeInvoiceStatusAsync(
            tenantId,
            invoiceId,
            FeInvoiceInternalStatus.VALIDATED_TEST,
            "test.prepare.validated",
            "La factura quedo validada en modo TEST interno.",
            "No se llamo a SIFEN ni se uso certificado real.",
            cancellationToken: cancellationToken);

        await _traceService.AddTenantLogAsync(
            tenantId,
            invoiceId,
            correlationId,
            FeTenantLogLevel.INFO,
            "fe.test.flow",
            "La factura esta lista para continuar con pruebas internas.",
            null,
            cancellationToken);

        return new PrepareInvoiceTestResult(
            invoiceId,
            tenantId,
            correlationId,
            FeInvoiceInternalStatus.VALIDATED_TEST,
            true,
            "La factura quedo preparada en modo TEST.");
    }
}
