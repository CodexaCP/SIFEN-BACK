using Microsoft.EntityFrameworkCore;
using SifenInvoicing.Api.Auth;
using SifenInvoicing.Application.Invoices;
using SifenInvoicing.Application.Operations;
using SifenInvoicing.Application.Tenancy;
using SifenInvoicing.Domain.Tenants;
using SifenInvoicing.Infrastructure.Invoices;
using SifenInvoicing.Infrastructure.Persistence;

namespace SifenInvoicing.Api.Endpoints;

public static class InvoiceEndpoints
{
    public const string IdempotencyKeyHeader = "Idempotency-Key";

    public static IEndpointRouteBuilder MapInvoiceEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/fe/invoices", CreateSimpleInvoiceAsync)
            .WithTags("FE")
            .RequireAuthorization(ApiAuthorization.InvoicesIssuePolicy);
        app.MapGet("/api/fe/invoices/status/{cdc}", GetSimpleInvoiceStatusAsync)
            .WithTags("FE")
            .RequireAuthorization(ApiAuthorization.InvoicesReadPolicy);
        app.MapGet("/api/fe/invoices/{id:guid}", GetInvoiceDetailAsync)
            .WithTags("FE")
            .RequireAuthorization(ApiAuthorization.InvoicesReadPolicy);
        app.MapGet("/api/fe/invoices/{id:guid}/xml", DownloadSimpleInvoiceXmlAsync)
            .WithTags("FE")
            .RequireAuthorization(ApiAuthorization.InvoicesReadPolicy);
        app.MapGet("/api/fe/invoices/{id:guid}/kude", DownloadSimpleInvoiceKudeAsync)
            .WithTags("FE")
            .RequireAuthorization(ApiAuthorization.InvoicesReadPolicy);
        app.MapGet("/api/fe/invoices/{id:guid}/events", GetInvoiceEventsAsync)
            .WithTags("FE")
            .RequireAuthorization(ApiAuthorization.InvoicesReadPolicy);
        app.MapPost("/api/fe/invoices/{id:guid}/prepare-test", PrepareInvoiceInTestModeAsync)
            .WithTags("FE")
            .RequireAuthorization(ApiAuthorization.InvoicesIssuePolicy);
        app.MapGet("/api/fe/tenants/{tenantId:guid}/diagnostic", GetTenantDiagnosticAsync)
            .WithTags("FE")
            .RequireAuthorization(ApiAuthorization.InvoicesReadPolicy);
        app.MapGet("/api/fe/tenants/{tenantId:guid}/invoices", GetTenantInvoicesAsync)
            .WithTags("FE")
            .RequireAuthorization(ApiAuthorization.InvoicesReadPolicy);
        app.MapGet("/api/fe/tenants/{tenantId:guid}/logs", GetTenantLogsAsync)
            .WithTags("FE")
            .RequireAuthorization(ApiAuthorization.InvoicesReadPolicy);
        app.MapPost("/api/fe/invoices/kude/preview", GenerateKudePreviewHtmlAsync)
            .WithTags("FE")
            .RequireAuthorization(ApiAuthorization.InvoicesReadPolicy);
        app.MapPost("/api/fe/invoices/kude/preview/pdf", GenerateKudePreviewPdfAsync)
            .WithTags("FE")
            .RequireAuthorization(ApiAuthorization.InvoicesReadPolicy);

        var group = app.MapGroup("/invoice")
            .WithTags("Invoices")
            .RequireAuthorization(ApiAuthorization.InvoicesReadPolicy);

        group.MapGet("/", async (
            string? status,
            string? cdc,
            DateOnly? dateFrom,
            DateOnly? dateTo,
            string? number,
            IInvoiceService invoiceService,
            CancellationToken cancellationToken) =>
        {
            Domain.Documents.SifenDocumentStatus? parsedStatus = null;
            if (!string.IsNullOrWhiteSpace(status) &&
                Enum.TryParse<Domain.Documents.SifenDocumentStatus>(status, true, out var statusValue))
            {
                parsedStatus = statusValue;
            }

            var result = await invoiceService.SearchAsync(
                new InvoiceSearchQuery(parsedStatus, cdc, dateFrom, dateTo, number),
                cancellationToken);

            return Results.Ok(result);
        });

        group.MapPost("/", async (
            CreateInvoiceRequest request,
            [Microsoft.AspNetCore.Mvc.FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKey,
            IInvoiceService invoiceService,
            CancellationToken cancellationToken) =>
        {
            var result = await invoiceService.CreateAsync(
                BuildCreateInvoiceCommand(request, idempotencyKey),
                cancellationToken);

            return Results.Created($"/invoice/{result.Id}", result);
        }).RequireAuthorization(ApiAuthorization.InvoicesIssuePolicy);

        group.MapGet("/{id:guid}", async (
            Guid id,
            IInvoiceService invoiceService,
            CancellationToken cancellationToken) =>
        {
            var invoice = await invoiceService.GetByIdAsync(id, cancellationToken);
            return invoice is null ? Results.NotFound() : Results.Ok(invoice);
        });

        group.MapGet("/status/{cdc}", async (
            string cdc,
            IInvoiceService invoiceService,
            CancellationToken cancellationToken) =>
        {
            var invoice = await invoiceService.GetByCdcAsync(cdc, cancellationToken);
            return invoice is null
                ? Results.NotFound()
                : Results.Ok(new
                {
                    invoice.Id,
                    invoice.Cdc,
                    invoice.Status,
                    invoice.StatusCode,
                    invoice.StatusMessage,
                    invoice.ErrorCode,
                    invoice.ErrorCategory,
                    invoice.UserMessage,
                    invoice.SuggestedAction,
                    invoice.IsRetryable,
                    invoice.CorrelationId,
                    invoice.SifenTrackingId,
                    invoice.SubmittedAt,
                    invoice.FinalizedAt
                });
        });

        group.MapGet("/app", (IWebHostEnvironment environment) =>
        {
            var path = Path.Combine(environment.WebRootPath, "index.html");
            return File.Exists(path)
                ? Results.File(path, "text/html; charset=utf-8")
                : Results.NotFound();
        }).AllowAnonymous();

        group.MapPost("/{id:guid}/retry", async (
            Guid id,
            IInvoiceService invoiceService,
            CancellationToken cancellationToken) =>
        {
            var result = await invoiceService.RetryAsync(id, cancellationToken);
            return Results.Ok(result);
        }).RequireAuthorization(ApiAuthorization.InvoicesIssuePolicy);

        group.MapGet("/{id:guid}/kude", DownloadKudeAsync);
        group.MapGet("/{id:guid}/xml", DownloadXmlAsync);

        return app;
    }

    public static async Task<IResult> CreateSimpleInvoiceAsync(
        CreateSimpleInvoiceRequest request,
        [Microsoft.AspNetCore.Mvc.FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKey,
        IInvoiceService invoiceService,
        CancellationToken cancellationToken)
    {
        ValidateSimpleRequest(request);

        var result = await invoiceService.CreateAsync(
            BuildCreateInvoiceCommand(request, idempotencyKey),
            cancellationToken);

        return Results.Created($"/invoice/{result.Id}", result);
    }

    public static async Task<IResult> GetSimpleInvoiceStatusAsync(
        string cdc,
        IInvoiceService invoiceService,
        CancellationToken cancellationToken)
    {
        var invoice = await invoiceService.GetByCdcAsync(cdc, cancellationToken);
        return invoice is null
            ? Results.NotFound()
            : Results.Ok(new
            {
                cdc = invoice.Cdc,
                status = MapSimpleStatus(invoice.Status),
                statusCode = invoice.StatusCode,
                statusMessage = invoice.StatusMessage,
                errorCode = invoice.ErrorCode,
                category = invoice.ErrorCategory,
                userMessage = invoice.UserMessage,
                suggestedAction = invoice.SuggestedAction,
                isRetryable = invoice.IsRetryable,
                correlationId = invoice.CorrelationId,
                transmissionState = invoice.TransmissionState,
                fiscalState = invoice.FiscalState,
                sifenTrackingId = invoice.SifenTrackingId
            });
    }

    public static Task<IResult> DownloadSimpleInvoiceXmlAsync(
        Guid id,
        IInvoiceService invoiceService,
        CancellationToken cancellationToken)
    {
        return DownloadXmlAsync(id, invoiceService, cancellationToken);
    }

    public static async Task<IResult> DownloadSimpleInvoiceKudeAsync(
        Guid id,
        IInvoiceService invoiceService,
        CancellationToken cancellationToken)
    {
        var invoice = await invoiceService.GetByIdAsync(id, cancellationToken);
        if (invoice is null)
        {
            return Results.NotFound();
        }

        try
        {
            var pdf = await invoiceService.GenerateKudePdfAsync(id, cancellationToken);
            return pdf is null
                ? Results.Ok(new KudePlaceholderResponse(
                    false,
                    "KuDE aun no disponible para esta factura.",
                    $"/api/fe/invoices/{id}/kude"))
                : Results.File(pdf.Content, "application/pdf", pdf.FileName);
        }
        catch (Exception ex) when (ex is Domain.Common.DomainException or InvalidOperationException)
        {
            return Results.Ok(new KudePlaceholderResponse(
                false,
                "KuDE aun no disponible para esta factura.",
                $"/api/fe/invoices/{id}/kude"));
        }
    }

    public static async Task<IResult> GetInvoiceDetailAsync(
        Guid id,
        ITenantContextAccessor tenantContextAccessor,
        IInvoiceService invoiceService,
        CancellationToken cancellationToken)
    {
        var tenantId = ResolveTenantIdOrFail(tenantContextAccessor);
        if (!tenantId.HasValue)
        {
            return Results.BadRequest(new { error = "Tenant context is required." });
        }

        var invoice = await invoiceService.GetByIdAsync(id, cancellationToken);
        if (invoice is null || invoice.TenantId != tenantId.Value)
        {
            return Results.NotFound();
        }

        return Results.Ok(invoice);
    }

    public static async Task<IResult> GetInvoiceEventsAsync(
        Guid id,
        ITenantContextAccessor tenantContextAccessor,
        IFeTraceService traceService,
        CancellationToken cancellationToken)
    {
        var tenantId = ResolveTenantIdOrFail(tenantContextAccessor);
        if (!tenantId.HasValue)
        {
            return Results.BadRequest(new { error = "Tenant context is required." });
        }

        var events = await traceService.GetInvoiceEventsAsync(tenantId.Value, id, cancellationToken);
        return Results.Ok(events);
    }

    public static async Task<IResult> PrepareInvoiceInTestModeAsync(
        Guid id,
        ITenantContextAccessor tenantContextAccessor,
        IFeInvoiceTestFlowService testFlowService,
        CancellationToken cancellationToken)
    {
        var tenantId = ResolveTenantIdOrFail(tenantContextAccessor);
        if (!tenantId.HasValue)
        {
            return Results.BadRequest(new { error = "Tenant context is required." });
        }

        var result = await testFlowService.PrepareInvoiceInTestModeAsync(tenantId.Value, id, cancellationToken);
        return Results.Ok(result);
    }

    public static async Task<IResult> GetTenantDiagnosticAsync(
        Guid tenantId,
        ITenantContextAccessor tenantContextAccessor,
        IFeTenantDiagnosticService diagnosticService,
        CancellationToken cancellationToken)
    {
        var tenantGuard = EnsureTenantRouteAccess(tenantId, tenantContextAccessor);
        if (tenantGuard is not null)
        {
            return tenantGuard;
        }

        var diagnostic = await diagnosticService.GetDiagnosticAsync(tenantId, cancellationToken);
        return Results.Ok(diagnostic);
    }

    public static async Task<IResult> GetTenantInvoicesAsync(
        Guid tenantId,
        string? status,
        string? customerName,
        DateOnly? from,
        DateOnly? to,
        int? page,
        int? pageSize,
        ITenantContextAccessor tenantContextAccessor,
        SifenDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var tenantGuard = EnsureTenantRouteAccess(tenantId, tenantContextAccessor);
        if (tenantGuard is not null)
        {
            return tenantGuard;
        }

        var resolvedPage = Math.Max(page ?? 1, 1);
        var resolvedPageSize = Math.Clamp(pageSize ?? 20, 1, 100);

        IQueryable<Domain.Documents.SifenDocument> invoices = dbContext.Documents
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(item => item.TenantId == tenantId && item.Kind == Domain.Documents.SifenDocumentKind.Invoice);

        if (!string.IsNullOrWhiteSpace(status))
        {
            var normalizedStatus = status.Trim().ToUpperInvariant();
            invoices = invoices.Where(item => item.InternalStatus.ToString() == normalizedStatus);
        }

        if (!string.IsNullOrWhiteSpace(customerName))
        {
            var normalizedCustomerName = customerName.Trim();
            invoices = invoices.Where(item => item.ReceiverName.Contains(normalizedCustomerName));
        }

        if (from.HasValue)
        {
            var fromDate = new DateTimeOffset(from.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
            invoices = invoices.Where(item => item.CreatedAt >= fromDate);
        }

        if (to.HasValue)
        {
            var toDateExclusive = new DateTimeOffset(to.Value.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
            invoices = invoices.Where(item => item.CreatedAt < toDateExclusive);
        }

        var totalCount = await invoices.CountAsync(cancellationToken);
        var items = await invoices
            .OrderByDescending(item => item.CreatedAt)
            .Skip((resolvedPage - 1) * resolvedPageSize)
            .Take(resolvedPageSize)
            .Select(item => new FeTenantInvoiceListItem(
                item.Id,
                item.TenantId,
                item.ExternalDocumentNumber,
                item.ReceiverName,
                item.TotalAmount,
                item.CurrencyCode,
                item.InternalStatus.ToString(),
                item.CorrelationId,
                item.RetryCount,
                item.IsRetryable,
                item.LastErrorMessage,
                item.CreatedAt,
                item.UpdatedAt,
                item.Cdc,
                item.TransmissionState,
                item.FiscalState))
            .ToArrayAsync(cancellationToken);

        var totalPages = totalCount == 0
            ? 0
            : (int)Math.Ceiling(totalCount / (double)resolvedPageSize);

        return Results.Ok(new FeTenantInvoicePage(items, totalCount, resolvedPage, resolvedPageSize, totalPages));
    }

    public static async Task<IResult> GetTenantLogsAsync(
        Guid tenantId,
        Guid? invoiceId,
        string? level,
        DateTimeOffset? from,
        DateTimeOffset? to,
        ITenantContextAccessor tenantContextAccessor,
        IFeTraceService traceService,
        CancellationToken cancellationToken)
    {
        var tenantGuard = EnsureTenantRouteAccess(tenantId, tenantContextAccessor);
        if (tenantGuard is not null)
        {
            return tenantGuard;
        }

        var logs = await traceService.GetTenantLogsAsync(
            new FeTenantLogQuery(tenantId, invoiceId, level, from, to),
            cancellationToken);

        return Results.Ok(logs);
    }

    public static async Task<IResult> GenerateKudePreviewHtmlAsync(
        KudePreviewRequest? request,
        ITenantContextAccessor tenantContextAccessor,
        SifenDbContext dbContext,
        IInvoiceKudePdfRenderer kudeRenderer,
        CancellationToken cancellationToken)
    {
        var settings = await LoadTenantKudeSettingsAsync(tenantContextAccessor, dbContext, request, cancellationToken);
        var html = kudeRenderer.RenderPreviewHtml(settings);
        return Results.Content(html, "text/html; charset=utf-8");
    }

    public static async Task<IResult> GenerateKudePreviewPdfAsync(
        KudePreviewRequest? request,
        ITenantContextAccessor tenantContextAccessor,
        SifenDbContext dbContext,
        IInvoiceKudePdfRenderer kudeRenderer,
        CancellationToken cancellationToken)
    {
        var settings = await LoadTenantKudeSettingsAsync(tenantContextAccessor, dbContext, request, cancellationToken);
        var pdf = kudeRenderer.RenderPreviewPdf(settings);
        return Results.File(pdf.Content, "application/pdf", pdf.FileName);
    }

    public static async Task<IResult> DownloadKudeAsync(
        Guid id,
        IInvoiceService invoiceService,
        CancellationToken cancellationToken)
    {
        var pdf = await invoiceService.GenerateKudePdfAsync(id, cancellationToken);
        return pdf is null
            ? Results.NotFound()
            : Results.File(pdf.Content, "application/pdf", pdf.FileName);
    }

    public static async Task<IResult> DownloadXmlAsync(
        Guid id,
        IInvoiceService invoiceService,
        CancellationToken cancellationToken)
    {
        var invoice = await invoiceService.GetByIdAsync(id, cancellationToken);
        return invoice is null
            ? Results.NotFound()
            : Results.File(
                System.Text.Encoding.UTF8.GetBytes(invoice.XmlPayload),
                "application/xml",
                $"fe-{invoice.ExternalDocumentNumber}.xml");
    }

    /// <summary>
    /// Solo datos comerciales. Los campos fiscales (ambiente, timbrado, establecimiento, punto, numero,
    /// codigo de seguridad, CDC, tipo de contribuyente/emision, fecha, emisor) los decide el backend;
    /// si el cliente los envia se ignoran.
    /// </summary>
    public class CreateInvoiceRequest
    {
        public string? Notes { get; init; }
        public string? ReceiverName { get; init; }
        public string? ReceiverDocument { get; init; }
        public string? ReceiverAddress { get; init; }
        public string? ReceiverEmail { get; init; }
        public string? ReceiverPhone { get; init; }
        public string? ReceptorNombre { get; init; }
        public InvoiceReceiverDocumentType? ReceptorTipoDocumento { get; init; }
        public string? ReceptorDocumento { get; init; }
        public string? CurrencyCode { get; init; }
        public InvoiceCurrency? Currency { get; init; }
        public string? SaleCondition { get; init; }
        public InvoiceSaleCondition? LegacySaleCondition { get; init; }
        public SimpleInvoiceCustomerRequest? Customer { get; init; }
        /// <summary>iTipTra (D011). Si se omite se usa la configuracion Sifen:De:DefaultTransactionType.</summary>
        public int? TransactionType { get; init; }
        /// <summary>iIndPres (E011). Si se omite se usa la configuracion Sifen:De:DefaultPresenceIndicator.</summary>
        public int? PresenceIndicator { get; init; }
        /// <summary>iTiContRec (D205): 1 = persona fisica, 2 = persona juridica. Obligatorio si el receptor es contribuyente (RUC).</summary>
        public int? ReceptorTaxpayerKind { get; init; }
        public IReadOnlyCollection<CreateInvoiceItemRequest> Items { get; init; } = [];
    }

    public sealed class CreateInvoiceItemRequest
    {
        public string? Description { get; init; }
        public decimal Quantity { get; init; }
        public decimal UnitPrice { get; init; }
        public int? VatRate { get; init; }
        public InvoiceVatType? VatType { get; init; }
        /// <summary>dCodInt (E701).</summary>
        public string? Code { get; init; }
        /// <summary>cUniMed (E709), Tabla 5 del Manual v150.</summary>
        public int? UnitCode { get; init; }
    }

    public sealed class CreateSimpleInvoiceRequest : CreateInvoiceRequest
    {
    }

    public sealed record SimpleInvoiceCustomerRequest(
        string Name,
        InvoiceReceiverDocumentType DocumentType,
        string DocumentNumber);

    public sealed record KudePlaceholderResponse(
        bool Available,
        string Message,
        string FuturePath);

    public sealed record KudePreviewRequest(
        string? TemplateCode,
        string? LogoUrl,
        string? PrimaryColor,
        string? SecondaryColor,
        string? FooterText,
        bool? ShowPhone,
        bool? ShowEmail);

    private static void ValidateSimpleRequest(CreateSimpleInvoiceRequest request)
    {
        var receiverName = request.ReceiverName ?? request.Customer?.Name ?? request.ReceptorNombre;
        var receiverDocument = request.ReceiverDocument ?? request.Customer?.DocumentNumber ?? request.ReceptorDocumento;

        if (string.IsNullOrWhiteSpace(receiverName) ||
            string.IsNullOrWhiteSpace(receiverDocument))
        {
            throw new Domain.Common.DomainException("Customer is required.");
        }

        if (request.Items is null || request.Items.Count == 0)
        {
            throw new Domain.Common.DomainException("At least one item is required.");
        }
    }

    private static CreateInvoiceCommand BuildCreateInvoiceCommand(CreateInvoiceRequest request, string? idempotencyKey)
    {
        var receiverName = request.ReceiverName ?? request.Customer?.Name ?? request.ReceptorNombre ?? string.Empty;
        var receiverDocument = request.ReceiverDocument ?? request.Customer?.DocumentNumber ?? request.ReceptorDocumento ?? string.Empty;
        var receiverType = request.ReceptorTipoDocumento
            ?? request.Customer?.DocumentType
            ?? InferReceiverDocumentType(receiverDocument);

        return new CreateInvoiceCommand(
            idempotencyKey?.Trim() ?? string.Empty,
            request.Notes,
            receiverName,
            receiverType,
            receiverDocument,
            request.ReceiverAddress,
            request.ReceiverEmail,
            request.ReceiverPhone,
            ParseCurrency(request.CurrencyCode, request.Currency),
            ParseSaleCondition(request.SaleCondition, request.LegacySaleCondition),
            request.Items.Select(BuildCreateInvoiceItemCommand).ToArray(),
            request.TransactionType,
            request.PresenceIndicator,
            request.ReceptorTaxpayerKind);
    }

    private static CreateInvoiceItemCommand BuildCreateInvoiceItemCommand(CreateInvoiceItemRequest item)
    {
        return new CreateInvoiceItemCommand(
            item.Description ?? string.Empty,
            item.Quantity,
            item.UnitPrice,
            ResolveVatRate(item),
            item.Code,
            item.UnitCode);
    }

    private static int ResolveVatRate(CreateInvoiceItemRequest item)
    {
        if (item.VatRate.HasValue)
        {
            return item.VatRate.Value;
        }

        return item.VatType switch
        {
            InvoiceVatType.Vat10 => 10,
            InvoiceVatType.Vat5 => 5,
            InvoiceVatType.Exempt => 0,
            _ => 10
        };
    }

    private static InvoiceCurrency ParseCurrency(string? currencyCode, InvoiceCurrency? legacyCurrency)
    {
        if (!string.IsNullOrWhiteSpace(currencyCode) &&
            Enum.TryParse<InvoiceCurrency>(currencyCode.Trim(), true, out var parsedCurrency))
        {
            return parsedCurrency;
        }

        return legacyCurrency ?? InvoiceCurrency.PYG;
    }

    private static InvoiceSaleCondition ParseSaleCondition(string? saleCondition, InvoiceSaleCondition? legacySaleCondition)
    {
        if (!string.IsNullOrWhiteSpace(saleCondition))
        {
            var normalized = saleCondition.Trim().ToUpperInvariant();
            if (normalized is "CREDITO" or "CRÉDITO" or "CREDIT")
            {
                return InvoiceSaleCondition.Credit;
            }
        }

        return legacySaleCondition ?? InvoiceSaleCondition.Cash;
    }

    private static InvoiceReceiverDocumentType InferReceiverDocumentType(string? document)
    {
        return !string.IsNullOrWhiteSpace(document) && document.Contains('-', StringComparison.Ordinal)
            ? InvoiceReceiverDocumentType.Ruc
            : InvoiceReceiverDocumentType.Ci;
    }

    private static Guid? ResolveTenantIdOrFail(ITenantContextAccessor tenantContextAccessor)
        => tenantContextAccessor.Current.ResolvedTenantId;

    private static IResult? EnsureTenantRouteAccess(Guid tenantId, ITenantContextAccessor tenantContextAccessor)
    {
        var resolvedTenantId = tenantContextAccessor.Current.ResolvedTenantId;
        if (!resolvedTenantId.HasValue || resolvedTenantId.Value != tenantId)
        {
            return Results.Forbid();
        }

        return null;
    }

    private static async Task<TenantKudeTemplateSettings?> LoadTenantKudeSettingsAsync(
        ITenantContextAccessor tenantContextAccessor,
        SifenDbContext dbContext,
        KudePreviewRequest? request,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantContextAccessor.Current.ResolvedTenantId;
        if (!tenantId.HasValue && request is null)
        {
            return null;
        }

        var resolvedTenantId = tenantId ?? Guid.NewGuid();
        var settings = tenantId.HasValue
            ? await dbContext.TenantKudeTemplateSettings
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(item => item.TenantId == tenantId.Value, cancellationToken)
            : null;

        if (request is null)
        {
            return settings;
        }

        if (settings is null)
        {
            return TenantKudeTemplateSettings.Create(
                resolvedTenantId,
                request.TemplateCode ?? "codexa-standard",
                request.LogoUrl,
                request.PrimaryColor,
                request.SecondaryColor,
                request.FooterText,
                request.ShowPhone ?? true,
                request.ShowEmail ?? true);
        }

        settings.Update(
            request.TemplateCode ?? settings.TemplateCode,
            request.LogoUrl,
            request.PrimaryColor,
            request.SecondaryColor,
            request.FooterText,
            request.ShowPhone ?? settings.ShowPhone,
            request.ShowEmail ?? settings.ShowEmail);
        return settings;
    }

    private static string MapSimpleStatus(Domain.Documents.SifenDocumentStatus status)
    {
        return status switch
        {
            Domain.Documents.SifenDocumentStatus.InternalValidation => "validacion-interna",
            Domain.Documents.SifenDocumentStatus.InternalValidationFailed => "validacion-interna-fallida",
            Domain.Documents.SifenDocumentStatus.DraftValidatedWithoutSignature => "borrador-validado-sin-firma",
            Domain.Documents.SifenDocumentStatus.Accepted => "aprobado",
            Domain.Documents.SifenDocumentStatus.Rejected => "rechazado",
            Domain.Documents.SifenDocumentStatus.Failed => "error",
            _ => "pendiente"
        };
    }
}
