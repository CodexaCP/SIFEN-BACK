using System.Text.Json;
using System.Security.Cryptography;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SifenInvoicing.Application.Auditing;
using SifenInvoicing.Application.Diagnostics;
using SifenInvoicing.Application.Invoices;
using SifenInvoicing.Application.Operations;
using SifenInvoicing.Application.Security;
using SifenInvoicing.Application.Sifen;
using SifenInvoicing.Application.Tenancy;
using SifenInvoicing.Application.XmlSigning;
using SifenInvoicing.Domain.Common;
using SifenInvoicing.Domain.Documents;
using SifenInvoicing.Domain.Tenants;
using SifenInvoicing.Infrastructure.Persistence;

namespace SifenInvoicing.Infrastructure.Invoices;

public sealed class EfInvoiceService : IInvoiceService
{
    private const string UnsignedLocalValidationMessage = "XML generated locally. Certificate/XSD/CSC are missing for full validation.";

    private sealed record InvoiceLineCalculation(
        int LineNumber,
        string Description,
        decimal Quantity,
        decimal UnitPrice,
        int VatRate,
        decimal VatAmount,
        decimal ExemptAmount,
        decimal SubtotalAmount,
        decimal TotalAmount);

    private readonly SifenDbContext _dbContext;
    private readonly ITenantContextAccessor _tenantContextAccessor;
    private readonly IFacturaXmlGenerator _xmlGenerator;
    private readonly IFacturaXmlPreSubmissionValidator _xmlPreSubmissionValidator;
    private readonly IInvoiceKudePdfRenderer _invoiceKudePdfRenderer;
    private readonly ITenantCertificateValidator _tenantCertificateValidator;
    private readonly IXmlDocumentSigner _xmlDocumentSigner;
    private readonly IOperationalReadinessReporter _operationalReadinessReporter;
    private readonly ISifenSubmissionGateway _submissionGateway;
    private readonly ISifenResponseParser _responseParser;
    private readonly IConfiguration _configuration;
    private readonly IAuditTrail _auditTrail;
    private readonly ISystemClock _clock;

    public EfInvoiceService(
        SifenDbContext dbContext,
        ITenantContextAccessor tenantContextAccessor,
        IFacturaXmlGenerator xmlGenerator,
        IFacturaXmlPreSubmissionValidator xmlPreSubmissionValidator,
        IInvoiceKudePdfRenderer invoiceKudePdfRenderer,
        ITenantCertificateValidator tenantCertificateValidator,
        IXmlDocumentSigner xmlDocumentSigner,
        IOperationalReadinessReporter operationalReadinessReporter,
        ISifenSubmissionGateway submissionGateway,
        ISifenResponseParser responseParser,
        IConfiguration configuration,
        IAuditTrail auditTrail,
        ISystemClock clock)
    {
        _dbContext = dbContext;
        _tenantContextAccessor = tenantContextAccessor;
        _xmlGenerator = xmlGenerator;
        _xmlPreSubmissionValidator = xmlPreSubmissionValidator;
        _invoiceKudePdfRenderer = invoiceKudePdfRenderer;
        _tenantCertificateValidator = tenantCertificateValidator;
        _xmlDocumentSigner = xmlDocumentSigner;
        _operationalReadinessReporter = operationalReadinessReporter;
        _submissionGateway = submissionGateway;
        _responseParser = responseParser;
        _configuration = configuration;
        _auditTrail = auditTrail;
        _clock = clock;
    }

    public async Task<CreateInvoiceResult> CreateAsync(
        CreateInvoiceCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var tenantId = RequireTenant();
        var tenant = await RequireTenantForEmissionAsync(tenantId, cancellationToken);
        var tenantSettings = await GetTenantSifenSettingsAsync(tenantId, command.Environment, cancellationToken);
        ValidateCommand(command);
        await EnsureTenantInvoicePlanAllowsEmissionAsync(tenant, cancellationToken);

        var taxpayer = await _dbContext.TaxpayerProfiles
            .OrderByDescending(profile => profile.CreatedAt)
            .FirstOrDefaultAsync(profile => profile.IsActive, cancellationToken);

        if (taxpayer is null)
        {
            throw new DomainException("Active taxpayer profile was not found for the current tenant.");
        }

        var diagnostic = await EnsureSubmissionIsAllowedAsync(tenantId, command.Environment, cancellationToken);
        var allowUnsignedLocalValidationOverride = IsUnsignedLocalValidationOverrideEnabled(diagnostic.TransportMode);
        var requiresUnsignedLocalDraft = RequiresUnsignedLocalDraft(diagnostic, allowUnsignedLocalValidationOverride);
        var canSignLocally = await CanSignXmlLocallyAsync(
            tenantId,
            command.Environment,
            allowUnsignedLocalValidationOverride,
            cancellationToken);

        var effectiveCommand = ApplyTenantSettings(command, tenantSettings);
        var lineCalculations = BuildLineCalculations(effectiveCommand.Items);
        var subtotalAmount = lineCalculations.Sum(item => item.SubtotalAmount);
        var vat5Amount = lineCalculations.Sum(item => item.VatRate == 5 ? item.VatAmount : 0m);
        var vat10Amount = lineCalculations.Sum(item => item.VatRate == 10 ? item.VatAmount : 0m);
        var exemptAmount = lineCalculations.Sum(item => item.ExemptAmount);
        var totalVatAmount = vat5Amount + vat10Amount;
        var totalAmount = lineCalculations.Sum(item => item.TotalAmount);
        var xmlInput = BuildXmlInput(effectiveCommand, taxpayer);
        var generated = _xmlGenerator.GenerateFacturaXML(xmlInput);
        ValidateGeneratedFacturaResult(generated);
        var document = SifenDocument.CreateInvoice(
            tenantId,
            effectiveCommand.Environment,
            generated.Cdc,
            effectiveCommand.DocumentType,
            effectiveCommand.DocumentNumber.PadLeft(7, '0'),
            effectiveCommand.EstablishmentCode.PadLeft(3, '0'),
            effectiveCommand.ExpeditionPointCode.PadLeft(3, '0'),
            effectiveCommand.Currency.ToString(),
            MapSaleCondition(effectiveCommand.SaleCondition),
            effectiveCommand.ReceptorNombre,
            effectiveCommand.ReceptorDocumento,
            effectiveCommand.ReceptorDireccion,
            effectiveCommand.ReceptorEmail,
            effectiveCommand.ReceptorPhone,
            effectiveCommand.Notes,
            subtotalAmount,
            vat5Amount,
            vat10Amount,
            exemptAmount,
            totalVatAmount,
            totalAmount,
            generated.Xml,
            BuildTestCdc(generated.Cdc),
            BuildTestQrText(effectiveCommand.DocumentNumber, effectiveCommand.ReceptorNombre),
            false,
            _clock.UtcNow);
        var correlationId = document.EnsureCorrelationId();
        document.SetInternalStatus(FeInvoiceInternalStatus.DRAFT);

        _dbContext.Documents.Add(document);
        _dbContext.DocumentLines.AddRange(lineCalculations.Select(item =>
            SifenDocumentLine.Create(
                tenantId,
                document.Id,
                item.LineNumber,
                item.Description,
                item.Quantity,
                item.UnitPrice,
                item.VatRate,
                item.VatAmount,
                item.ExemptAmount,
                item.SubtotalAmount,
                item.TotalAmount)));
        _dbContext.DocumentLogs.Add(CreateLog(tenantId, document.Id, DocumentLogLevel.Information, "xml.generated", "Invoice XML generated."));
        _dbContext.FeInvoiceEvents.Add(FeInvoiceEvent.Create(
            tenantId,
            document.Id,
            correlationId,
            null,
            document.InternalStatus.ToString(),
            "InvoiceCreatedTest",
            "Factura creada en entorno TEST.",
            null,
            _clock.UtcNow));
        _dbContext.FeTenantLogs.Add(FeTenantLog.Create(
            tenantId,
            document.Id,
            correlationId,
            FeTenantLogLevel.INFO,
            "fe.invoice.create",
            "Factura TEST creada y persistida con datos completos.",
            JsonSerializer.Serialize(new
            {
                document.DocumentType,
                document.ExternalDocumentNumber,
                totalAmount,
                items = lineCalculations.Count
            }),
            _clock.UtcNow));

        try
        {
            await _xmlPreSubmissionValidator.ValidateTipoDoc01Async(generated.Xml, generated.Cdc, tenantId, effectiveCommand.Environment, cancellationToken);
            _dbContext.DocumentLogs.Add(CreateLog(tenantId, document.Id, DocumentLogLevel.Information, "xml.validated", "Invoice XML validated locally."));
        }
        catch (Exception ex) when (IsInternalValidationRuntime(ex) && IsDiagnosticTransport(diagnostic.TransportMode))
        {
            if (allowUnsignedLocalValidationOverride && IsMissingFullValidationDependency(ex))
            {
                return await CompleteUnsignedLocalDraftAsync(
                    document,
                    tenantSettings,
                    effectiveCommand.DocumentNumber,
                    ex.Message,
                    cancellationToken);
            }

            return await CompleteInternalValidationFailureAsync(
                document,
                tenantSettings,
                effectiveCommand.DocumentNumber,
                "INTERNAL_VALIDATION_XSD_FAILED",
                ex.Message,
                "internal.validation.failed",
                cancellationToken);
        }

        if (requiresUnsignedLocalDraft)
        {
            return await CompleteUnsignedLocalDraftAsync(
                document,
                tenantSettings,
                effectiveCommand.DocumentNumber,
                BuildUnsignedLocalValidationDetail(diagnostic),
                cancellationToken);
        }

        string? signedXml = null;
        if (canSignLocally)
        {
            try
            {
                var signed = await _xmlDocumentSigner.SignAsync(
                    new SignXmlDocumentCommand(
                        tenantId,
                        effectiveCommand.Environment,
                        generated.Cdc,
                        generated.Xml),
                    cancellationToken);

                signedXml = signed.SignedXml;
                document.MarkSigned(signedXml, _clock.UtcNow);
                _dbContext.DocumentLogs.Add(CreateLog(tenantId, document.Id, DocumentLogLevel.Information, "xml.signed", "Invoice XML signed."));
            }
            catch (Exception ex) when (IsInternalValidationRuntime(ex) && IsDiagnosticTransport(diagnostic.TransportMode))
            {
                if (allowUnsignedLocalValidationOverride && IsMissingFullValidationDependency(ex))
                {
                    return await CompleteUnsignedLocalDraftAsync(
                        document,
                        tenantSettings,
                        effectiveCommand.DocumentNumber,
                        ex.Message,
                        cancellationToken);
                }

                return await CompleteInternalValidationFailureAsync(
                    document,
                    tenantSettings,
                    effectiveCommand.DocumentNumber,
                    "INTERNAL_VALIDATION_SIGNATURE_FAILED",
                    ex.Message,
                    "internal.validation.failed",
                    cancellationToken);
            }
        }
        else
        {
            _dbContext.DocumentLogs.Add(CreateLog(
                tenantId,
                document.Id,
                DocumentLogLevel.Warning,
                "xml.signature.skipped",
                "Internal validation continues without local XML signature because the certificate is not ready."));

            if (allowUnsignedLocalValidationOverride)
            {
                return await CompleteUnsignedLocalDraftAsync(
                    document,
                    tenantSettings,
                    effectiveCommand.DocumentNumber,
                    BuildUnsignedLocalValidationDetail(diagnostic),
                    cancellationToken);
            }
        }

        if (IsDiagnosticTransport(diagnostic.TransportMode))
        {
            document.MarkInternalValidation(
                "INTERNAL_VALIDATION",
                canSignLocally
                    ? "Internal validation completed locally. Signed XML generated without calling SIFEN."
                    : "Internal validation completed locally without XML signature because the certificate is not ready.",
                _clock.UtcNow);
            _dbContext.DocumentLogs.Add(CreateLog(
                tenantId,
                document.Id,
                DocumentLogLevel.Information,
                "internal.validation.completed",
                "Internal FE validation completed without calling SIFEN.",
                JsonSerializer.Serialize(new
                {
                    generated.Cdc,
                    signed = canSignLocally,
                    environment = effectiveCommand.Environment.ToString(),
                    transportMode = diagnostic.TransportMode
                })));
            AdvanceTenantDocumentNumberIfNeeded(tenantSettings, effectiveCommand.DocumentNumber);
            await _dbContext.SaveChangesAsync(cancellationToken);

            return new CreateInvoiceResult(
                document.Id,
                document.Cdc,
                document.Status,
                document.TotalAmount,
                document.XmlPayload,
                document.SignedXmlPayload,
                document.StatusCode,
                document.StatusMessage,
                document.InternalStatus.ToString(),
                document.CorrelationId,
                "Factura creada y validada internamente en entorno TEST.");
        }

        var submission = await _submissionGateway.SendToSifenAsync(
            new SendToSifenCommand(
                tenantId,
                effectiveCommand.Environment,
                generated.Cdc,
                signedXml!),
            cancellationToken);

        var parsed = _responseParser.ParseResponse(submission.RawResponse);
        ApplySubmissionOutcome(document, submission, parsed);
        AddSubmissionLogs(tenantId, document.Id, submission, parsed);

        AdvanceTenantDocumentNumberIfNeeded(tenantSettings, effectiveCommand.DocumentNumber);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new CreateInvoiceResult(
            document.Id,
            document.Cdc,
            document.Status,
            document.TotalAmount,
            document.XmlPayload,
            document.SignedXmlPayload,
            document.StatusCode,
            document.StatusMessage,
            document.InternalStatus.ToString(),
            document.CorrelationId,
            "Factura TEST creada correctamente.");
    }

    public async Task<InvoiceRetryResult> RetryAsync(
        Guid invoiceId,
        CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        await RequireTenantForEmissionAsync(tenantId, cancellationToken);

        var document = await _dbContext.Documents
            .FirstOrDefaultAsync(item => item.Id == invoiceId, cancellationToken);

        if (document is null)
        {
            throw new DomainException("Invoice was not found for the current tenant.");
        }

        EnsureRetryAllowed(document);
        await EnsureSubmissionIsAllowedAsync(tenantId, document.Environment, cancellationToken);

        var attemptNumber = await GetNextRetryAttemptNumberAsync(document.Id, cancellationToken);
        var actorId = NormalizeActorId(_tenantContextAccessor.Current.ClientId);
        var attemptedAt = _clock.UtcNow;

        document.MarkPendingSubmission(document.LastSubmissionEndpoint ?? "retry://pending", attemptedAt);
        _dbContext.DocumentLogs.Add(CreateLog(
            tenantId,
            document.Id,
            DocumentLogLevel.Warning,
            "sifen.retry.attempted",
            "Invoice retry requested.",
            JsonSerializer.Serialize(new
            {
                attemptNumber,
                requestedBy = actorId,
                attemptedAt,
                previousStatus = SifenDocumentStatus.Failed.ToString(),
                previousStatusCode = document.StatusCode
            })));
        await _dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            var submission = await _submissionGateway.SendToSifenAsync(
                new SendToSifenCommand(
                    tenantId,
                    document.Environment,
                    document.Cdc,
                    document.SignedXmlPayload!),
                cancellationToken);

            var parsed = _responseParser.ParseResponse(submission.RawResponse);
            ApplySubmissionOutcome(document, submission, parsed);
            AddSubmissionLogs(tenantId, document.Id, submission, parsed);
            AddRetryResultLog(document, actorId, attemptNumber, attemptedAt, parsed.TechnicalMessage ?? parsed.StatusMessage);
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or TaskCanceledException)
        {
            document.MarkFailed(
                "RETRY_RUNTIME_ERROR",
                "Ocurrio un error tecnico al reintentar el envio.",
                ex.Message,
                _clock.UtcNow);
            _dbContext.DocumentErrors.Add(BuildErrorRecord(
                document,
                "RETRY_RUNTIME_ERROR",
                SifenErrorCategory.SifenTransport,
                ex.Message,
                "No pudimos completar el reintento por un problema temporal.",
                "Puedes reintentar esta factura en unos minutos.",
                true,
                ex.Message));
            AddRetryResultLog(document, actorId, attemptNumber, attemptedAt, ex.Message);
        }

        await RecordRetryAuditAsync(document, actorId, attemptNumber, attemptedAt, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new InvoiceRetryResult(
            document.Id,
            document.Cdc,
            document.Status,
            document.StatusCode,
            document.StatusMessage,
            attemptedAt,
            attemptNumber);
    }

    public async Task<InvoiceDetail?> GetByIdAsync(
        Guid invoiceId,
        CancellationToken cancellationToken = default)
    {
        var document = await _dbContext.Documents
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == invoiceId, cancellationToken);

        return document is null
            ? null
            : await MapDetailAsync(document, cancellationToken);
    }

    public async Task<InvoiceDetail?> GetByCdcAsync(
        string cdc,
        CancellationToken cancellationToken = default)
    {
        var normalizedCdc = string.IsNullOrWhiteSpace(cdc) ? null : cdc.Trim();
        if (normalizedCdc is null)
        {
            throw new DomainException("cdc is required.");
        }

        var document = await _dbContext.Documents
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Cdc == normalizedCdc, cancellationToken);

        return document is null
            ? null
            : await MapDetailAsync(document, cancellationToken);
    }

    public async Task<InvoiceKudePdfResult?> GenerateKudePdfAsync(
        Guid invoiceId,
        CancellationToken cancellationToken = default)
    {
        var document = await _dbContext.Documents
            .FirstOrDefaultAsync(item => item.Id == invoiceId, cancellationToken);

        if (document is null)
        {
            return null;
        }

        try
        {
            var detail = await MapDetailAsync(document, cancellationToken);
            var taxpayer = await _dbContext.TaxpayerProfiles
                .AsNoTracking()
                .IgnoreQueryFilters()
                .Where(item => item.TenantId == document.TenantId && item.IsActive)
                .OrderByDescending(item => item.UpdatedAt ?? item.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);
            var templateSettings = await _dbContext.TenantKudeTemplateSettings
                .AsNoTracking()
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(item => item.TenantId == document.TenantId, cancellationToken);
            var pdf = _invoiceKudePdfRenderer.Render(detail, taxpayer, templateSettings);

            _dbContext.DocumentLogs.Add(CreateLog(
                document.TenantId,
                document.Id,
                DocumentLogLevel.Information,
                "kude.generated",
                "KuDE PDF generated.",
                JsonSerializer.Serialize(new
                {
                    pdf.FileName,
                    size = pdf.Content.Length,
                    pdf.HasQr
                })));

            await _dbContext.SaveChangesAsync(cancellationToken);
            return pdf;
        }
        catch (Exception ex) when (ex is DomainException or InvalidOperationException)
        {
            _dbContext.DocumentLogs.Add(CreateLog(
                document.TenantId,
                document.Id,
                DocumentLogLevel.Error,
                "kude.failed",
                "KuDE PDF generation failed.",
                JsonSerializer.Serialize(new { error = ex.Message })));

            await _dbContext.SaveChangesAsync(cancellationToken);
            throw;
        }
    }

    public async Task<IReadOnlyCollection<InvoiceListItem>> SearchAsync(
        InvoiceSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        IQueryable<SifenDocument> documents = _dbContext.Documents.AsNoTracking();

        if (query.Status.HasValue)
        {
            documents = documents.Where(item => item.Status == query.Status.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Cdc))
        {
            var cdc = query.Cdc.Trim();
            documents = documents.Where(item => item.Cdc.Contains(cdc));
        }

        if (!string.IsNullOrWhiteSpace(query.ExternalDocumentNumber))
        {
            var number = query.ExternalDocumentNumber.Trim().PadLeft(7, '0');
            documents = documents.Where(item => item.ExternalDocumentNumber.Contains(number));
        }

        if (query.DateFrom.HasValue)
        {
            var from = query.DateFrom.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            documents = documents.Where(item => item.IssuedAt >= from);
        }

        if (query.DateTo.HasValue)
        {
            var toExclusive = query.DateTo.Value.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            documents = documents.Where(item => item.IssuedAt < toExclusive);
        }

        var items = await documents
            .OrderByDescending(item => item.IssuedAt)
            .Take(200)
            .ToListAsync(cancellationToken);

        var latestErrors = await LoadLatestErrorsAsync(items.Select(item => item.Id).ToArray(), cancellationToken);

        return items
            .Select(item =>
            {
                latestErrors.TryGetValue(item.Id, out var error);
                return new InvoiceListItem(
                    item.Id,
                    item.Cdc,
                    item.ExternalDocumentNumber,
                    item.EstablishmentCode,
                    item.ExpeditionPointCode,
                    item.ReceiverName,
                    item.TotalAmount,
                    item.CurrencyCode,
                    item.Status,
                    item.StatusCode,
                    item.StatusMessage,
                    item.SifenTrackingId,
                    item.IssuedAt,
                    item.SubmittedAt,
                    item.FinalizedAt,
                    !string.IsNullOrWhiteSpace(item.XmlPayload),
                    !string.IsNullOrWhiteSpace(item.XmlPayload),
                    IsRetryAllowedForStatus(item.Status, error?.IsRetryable ?? false, item.StatusCode),
                    error?.ErrorCode,
                    error?.ErrorCategory.ToString(),
                    error?.UserMessage,
                    error?.SuggestedAction,
                    error?.IsRetryable ?? false,
                    item.CorrelationId ?? error?.CorrelationId);
            })
            .ToArray();
    }

    private GenerateFacturaXmlInput BuildXmlInput(CreateInvoiceCommand command, TaxpayerProfile taxpayer)
    {
        return new GenerateFacturaXmlInput(
            new(
                "01",
                taxpayer.RucNumber,
                taxpayer.RucCheckDigit,
                command.EstablishmentCode,
                command.ExpeditionPointCode,
                command.DocumentNumber,
                command.TipoContribuyente,
                command.TipoEmision,
                command.SecurityCode,
                command.IssueDate.ToString("yyyyMMdd")),
            _clock.UtcNow,
            command.SistemaFacturacion,
            taxpayer.LegalName,
            command.EmisorDireccion,
            command.ReceptorNombre,
            command.ReceptorTipoDocumento,
            command.ReceptorDocumento,
            command.Currency,
            command.SaleCondition,
            command.Items.Select(item => new GenerateFacturaXmlItemInput(
                item.Description,
                item.Quantity,
                item.UnitPrice,
                MapVatType(item.VatRate))).ToArray());
    }

    private async Task<InvoiceDetail> MapDetailAsync(SifenDocument document, CancellationToken cancellationToken)
    {
        var logs = await _dbContext.DocumentLogs
            .AsNoTracking()
            .Where(log => log.DocumentId == document.Id)
            .OrderBy(log => log.CreatedAt)
            .Select(log => new InvoiceLogEntry(
                log.CreatedAt,
                log.Level,
                log.EventType,
                log.Message,
                SanitizeTechnicalDetail(log.MetadataJson)))
            .ToListAsync(cancellationToken);
        var events = await _dbContext.FeInvoiceEvents
            .AsNoTracking()
            .Where(item => item.InvoiceId == document.Id)
            .OrderBy(item => item.CreatedAt)
            .Select(item => new FeInvoiceEventItem(
                item.Id,
                item.InvoiceId,
                item.CorrelationId,
                item.PreviousStatus,
                item.NewStatus,
                item.EventType,
                item.Message,
                SanitizeTechnicalDetail(item.TechnicalDetail),
                item.CreatedAt))
            .ToListAsync(cancellationToken);
        var tenantLogs = await _dbContext.FeTenantLogs
            .AsNoTracking()
            .Where(item => item.InvoiceId == document.Id)
            .OrderByDescending(item => item.CreatedAt)
            .Take(20)
            .Select(item => new FeTenantLogItem(
                item.Id,
                item.TenantId,
                item.InvoiceId,
                item.CorrelationId,
                item.Level.ToString(),
                item.Source,
                item.Message,
                SanitizeTechnicalDetail(item.TechnicalDetail),
                item.CreatedAt))
            .ToListAsync(cancellationToken);
        var error = await _dbContext.DocumentErrors
            .AsNoTracking()
            .Where(item => item.InvoiceId == document.Id)
            .OrderByDescending(item => item.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        var lines = await _dbContext.DocumentLines
            .AsNoTracking()
            .Where(item => item.DocumentId == document.Id)
            .OrderBy(item => item.LineNumber)
            .Select(item => new InvoiceItemSummary(
                item.LineNumber,
                item.Description,
                item.Quantity,
                item.UnitPrice,
                item.VatRate,
                item.VatAmount,
                item.ExemptAmount,
                item.SubtotalAmount,
                item.TotalAmount))
            .ToListAsync(cancellationToken);

        return new InvoiceDetail(
            document.Id,
            document.TenantId,
            document.Environment,
            document.Status,
            document.Cdc,
            document.TestCdc,
            document.TestQrText,
            document.IsFiscalPreviewValid,
            document.DocumentType,
            document.ExternalDocumentNumber,
            document.EstablishmentCode,
            document.ExpeditionPointCode,
            document.CurrencyCode,
            document.SaleCondition,
            document.Notes,
            document.ReceiverName,
            document.ReceiverDocument,
            document.ReceiverAddress,
            document.ReceiverEmail,
            document.ReceiverPhone,
            document.SubtotalAmount,
            document.Vat5Amount,
            document.Vat10Amount,
            document.ExemptAmount,
            document.TotalVatAmount,
            document.TotalAmount,
            document.XmlPayload,
            document.SignedXmlPayload,
            document.LastSubmissionEndpoint,
            document.SifenTrackingId,
            document.StatusCode,
            document.StatusMessage,
            error?.ErrorCode,
            error?.ErrorCategory.ToString(),
            error?.UserMessage,
            error?.SuggestedAction,
            error?.IsRetryable ?? false,
            document.CorrelationId ?? error?.CorrelationId,
            document.InternalStatus.ToString(),
            document.RetryCount,
            document.LastErrorCode,
            document.LastErrorMessage,
            document.IssuedAt,
            document.SignedAt,
            document.SubmittedAt,
            document.FinalizedAt,
            lines,
            logs,
            events,
            tenantLogs);
    }

    private static string? SanitizeTechnicalDetail(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
        {
            return null;
        }

        var normalized = detail.Trim();
        var lowered = normalized.ToLowerInvariant();
        var sensitiveTokens = new[] { "password", "secret", "certificate", "csc" };
        return sensitiveTokens.Any(lowered.Contains)
            ? "Sensitive configuration detail was suppressed."
            : normalized;
    }

    private Guid RequireTenant()
    {
        var tenantId = _tenantContextAccessor.Current.ResolvedTenantId;
        if (!tenantId.HasValue)
        {
            throw new DomainException("Tenant context is required.");
        }

        return tenantId.Value;
    }

    private async Task<Tenant> RequireTenantForEmissionAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var tenant = await _dbContext.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == tenantId, cancellationToken);

        if (tenant is null)
        {
            throw new UserFacingException(
                "TENANT_NOT_FOUND",
                "Configuration",
                "No encontramos la compania seleccionada.",
                "Vuelve a entrar desde la compania correcta e intenta nuevamente.",
                false,
                404,
                "Tenant was not found.");
        }

        if (tenant.Status != TenantStatus.Active)
        {
            throw new UserFacingException(
                "TENANT_INACTIVE",
                "Configuration",
                "Esta compania no esta habilitada para emitir.",
                "Activa la compania o revisa su estado comercial antes de continuar.",
                false,
                403,
                "Tenant is not active for FE issuance.");
        }

        return tenant;
    }

    private async Task EnsureTenantInvoicePlanAllowsEmissionAsync(Tenant tenant, CancellationToken cancellationToken)
    {
        if (!tenant.MaxInvoicesPerMonth.HasValue)
        {
            return;
        }

        var now = _clock.UtcNow;
        var monthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var nextMonthStart = monthStart.AddMonths(1);

        var issuedCount = await _dbContext.Documents
            .AsNoTracking()
            .CountAsync(
                item => item.TenantId == tenant.Id &&
                        item.Kind == SifenDocumentKind.Invoice &&
                        item.IssuedAt >= monthStart &&
                        item.IssuedAt < nextMonthStart,
                cancellationToken);

        if (issuedCount >= tenant.MaxInvoicesPerMonth.Value)
        {
            throw new UserFacingException(
                "PLAN_MONTHLY_LIMIT_REACHED",
                "PlanLimit",
                "Tu plan ya alcanzo el maximo mensual de facturas.",
                "Actualiza tu plan o espera al siguiente periodo antes de emitir una nueva factura.",
                false,
                409,
                "Tenant monthly invoice limit has been reached.");
        }
    }

    private async Task<bool> CanSignXmlLocallyAsync(
        Guid tenantId,
        SifenEnvironmentType environment,
        bool allowMissingCertificate,
        CancellationToken cancellationToken)
    {
        var metadata = await _dbContext.TenantCertificateMetadata
            .OrderByDescending(certificate => certificate.ValidFrom)
            .FirstOrDefaultAsync(certificate =>
                certificate.TenantId == tenantId &&
                certificate.Environment == environment &&
                certificate.Purpose == CertificatePurpose.XmlSignature &&
                certificate.IsActive,
                cancellationToken);

        var validation = await _tenantCertificateValidator.ValidateAsync(metadata, cancellationToken);
        if (!validation.IsReady)
        {
            if (allowMissingCertificate)
            {
                return false;
            }

            throw new UserFacingException(
                "CERTIFICATE_NOT_READY",
                "CertificateSignature",
                "No pudimos usar el certificado de firma de esta compania.",
                "Revisa el certificado, su clave y su vigencia antes de volver a intentar.",
                false,
                409,
                $"XML signing certificate is not ready: {validation.Summary}");
        }

        return true;
    }

    private async Task<TenantSifenSettings?> GetTenantSifenSettingsAsync(
        Guid tenantId,
        SifenEnvironmentType environment,
        CancellationToken cancellationToken)
    {
        return await _dbContext.TenantSifenSettings
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(item =>
                item.TenantId == tenantId &&
                item.Environment == environment &&
                item.IsActive,
                cancellationToken);
    }

    private static CreateInvoiceCommand ApplyTenantSettings(
        CreateInvoiceCommand command,
        TenantSifenSettings? tenantSettings)
    {
        if (tenantSettings is null)
        {
            return command;
        }

        return command with
        {
            Environment = tenantSettings.Environment
        };
    }

    private async Task<TenantFeOperationalDiagnostic> EnsureSubmissionIsAllowedAsync(
        Guid tenantId,
        SifenEnvironmentType environment,
        CancellationToken cancellationToken)
    {
        var diagnostic = await _operationalReadinessReporter.GetTenantFeDiagnosticAsync(
            tenantId,
            environment,
            cancellationToken);

        if (IsDiagnosticTransport(diagnostic.TransportMode))
        {
            if (!diagnostic.ReadyForInternalValidation)
            {
                if (!IsUnsignedLocalValidationOverrideEnabled(diagnostic.TransportMode))
                {
                    throw new UserFacingException(
                        "SIFEN_CONFIGURATION_INCOMPLETE",
                        "Configuration",
                        "Faltan datos para dejar lista la emision.",
                        "Revisa el diagnostico y completa los datos pendientes antes de volver a emitir.",
                        false,
                        409,
                        "Tenant SIFEN configuration is incomplete. Run diagnostic first.");
                }
            }

            return diagnostic;
        }

        if (!diagnostic.ReadyForSifenTestAttempt)
        {
            throw new UserFacingException(
                "SIFEN_CONFIGURATION_INCOMPLETE",
                "Configuration",
                "Faltan datos para dejar lista la emision.",
                "Revisa el diagnostico y completa los datos pendientes antes de volver a emitir.",
                false,
                409,
                "Tenant SIFEN configuration is incomplete. Run diagnostic first.");
        }

        return diagnostic;
    }

    private static bool IsDiagnosticTransport(string? transportMode)
    {
        return string.Equals(transportMode, "Diagnostic", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsUnsignedLocalValidationOverrideEnabled(string? transportMode)
    {
        if (!IsDiagnosticTransport(transportMode))
        {
            return false;
        }

        if (!bool.TryParse(_configuration["Sifen:Development:AllowUnsignedInternalValidation"], out var enabled) || !enabled)
        {
            return false;
        }

        var environmentName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");

        return string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase);
    }

    private static bool RequiresUnsignedLocalDraft(
        TenantFeOperationalDiagnostic diagnostic,
        bool allowUnsignedLocalValidationOverride)
    {
        if (!allowUnsignedLocalValidationOverride)
        {
            return false;
        }

        if (diagnostic.Checks.Count == 0)
        {
            return !diagnostic.ReadyForInternalValidation;
        }

        return diagnostic.Checks.Any(check =>
            IsUnsignedLocalValidationPrerequisite(check.Code) &&
            !check.IsReady);
    }

    private static bool IsUnsignedLocalValidationPrerequisite(string code)
    {
        return code is
            "CSC_REFERENCE_PRESENT" or
            "CSC_SECRET_AVAILABLE" or
            "SIGNATURE_CERTIFICATE_REFERENCE_PRESENT" or
            "SIGNATURE_CERTIFICATE_LOADED" or
            "SIGNATURE_CERTIFICATE_VALID" or
            "XSD_ROOT_PATH_CONFIGURED" or
            "XSD_ROOT_PATH_AVAILABLE" or
            "XML_XSD_VALIDATION";
    }

    private static bool IsInternalValidationRuntime(Exception exception)
    {
        return exception is DomainException or InvalidOperationException or CryptographicException or IOException;
    }

    private static bool IsMissingFullValidationDependency(Exception exception)
    {
        var message = exception.Message;
        return message.Contains("XSD", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("certificate", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("CSC", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildUnsignedLocalValidationDetail(TenantFeOperationalDiagnostic diagnostic)
    {
        var blockers = diagnostic.Checks
            .Where(check => IsUnsignedLocalValidationPrerequisite(check.Code) && !check.IsReady)
            .Select(check => $"{check.Code}: {check.Message}")
            .ToArray();

        return blockers.Length == 0
            ? "Unsigned local validation fallback enabled for Development + Diagnostic."
            : string.Join(" | ", blockers);
    }

    private static void ValidateCommand(CreateInvoiceCommand command)
    {
        ValidateRequired(command.DocumentType, nameof(command.DocumentType));
        ValidateRequired(command.EstablishmentCode, nameof(command.EstablishmentCode));
        ValidateRequired(command.ExpeditionPointCode, nameof(command.ExpeditionPointCode));
        ValidateRequired(command.DocumentNumber, nameof(command.DocumentNumber));
        ValidateRequired(command.SecurityCode, nameof(command.SecurityCode));
        ValidateRequired(command.EmisorDireccion, nameof(command.EmisorDireccion));
        ValidateRequired(command.ReceptorNombre, nameof(command.ReceptorNombre));
        ValidateRequired(command.ReceptorDocumento, nameof(command.ReceptorDocumento));

        if (command.IssueDate == default)
        {
            throw new DomainException("IssueDate is required.");
        }

        if (!Enum.IsDefined(command.ReceptorTipoDocumento))
        {
            throw new DomainException("ReceptorTipoDocumento is invalid.");
        }

        if (!Enum.IsDefined(command.Currency))
        {
            throw new DomainException("Currency is invalid.");
        }

        if (!Enum.IsDefined(command.SaleCondition))
        {
            throw new DomainException("SaleCondition is invalid.");
        }

        if (command.Items is null || command.Items.Count == 0)
        {
            throw new DomainException("At least one invoice item is required.");
        }

        foreach (var item in command.Items)
        {
            ValidateRequired(item.Description, nameof(item.Description));

            if (item.Quantity <= 0)
            {
                throw new DomainException("Item quantity must be greater than 0.");
            }

            if (item.UnitPrice < 0)
            {
                throw new DomainException("Item unit price must be greater than or equal to 0.");
            }

            if (item.VatRate is not 10 and not 5 and not 0)
            {
                throw new DomainException("Item vatRate must be 10, 5 or 0.");
            }
        }
    }

    private static void ValidateGeneratedFacturaResult(GeneratedFacturaXmlResult generated)
    {
        if (!Application.Cdc.CdcGenerator.ValidateCDC(generated.Cdc))
        {
            throw new DomainException("Generated CDC is invalid.");
        }

        var document = System.Xml.Linq.XDocument.Parse(generated.Xml, System.Xml.Linq.LoadOptions.PreserveWhitespace);
        System.Xml.Linq.XNamespace ns = "http://ekuatia.set.gov.py/sifen/xsd";
        var de = document.Root?.Element(ns + "DE")
            ?? throw new DomainException("Generated FE XML must contain DE.");
        var totals = de.Element(ns + "gTotSub")
            ?? throw new DomainException("Generated FE XML must contain gTotSub.");

        ValidateXmlTotal(totals, ns + "dSubExe", generated.TotalExento);
        ValidateXmlTotal(totals, ns + "dSub5", generated.TotalGravado5);
        ValidateXmlTotal(totals, ns + "dSub10", generated.TotalGravado10);
        ValidateXmlTotal(totals, ns + "dTotIVA", generated.TotalIva);
        ValidateXmlTotal(totals, ns + "dTotGralOpe", generated.TotalGeneral);

        if (Round(generated.TotalGeneral) != Round(generated.TotalGravado10 + generated.TotalGravado5 + generated.TotalExento))
        {
            throw new DomainException("Generated FE XML totals are inconsistent.");
        }
    }

    private static void ValidateXmlTotal(System.Xml.Linq.XElement totals, System.Xml.Linq.XName elementName, decimal expected)
    {
        var rawValue = totals.Element(elementName)?.Value;
        if (!decimal.TryParse(rawValue, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
        {
            throw new DomainException($"Generated FE XML must contain numeric total {elementName.LocalName}.");
        }

        if (Round(parsed) != Round(expected))
        {
            throw new DomainException("Generated FE XML totals are inconsistent.");
        }
    }

    private static decimal Round(decimal value)
    {
        return Math.Round(value, 2, MidpointRounding.AwayFromZero);
    }

    private static string IncrementDocumentNumber(string currentNumber)
    {
        if (!int.TryParse(currentNumber, out var numericValue))
        {
            throw new DomainException("Tenant current document number is invalid.");
        }

        return (numericValue + 1).ToString().PadLeft(7, '0');
    }

    private static void AdvanceTenantDocumentNumberIfNeeded(
        TenantSifenSettings? tenantSettings,
        string emittedDocumentNumber)
    {
        if (tenantSettings is null ||
            !string.Equals(tenantSettings.CurrentDocumentNumber, emittedDocumentNumber, StringComparison.Ordinal))
        {
            return;
        }

        tenantSettings.Update(
            tenantSettings.Environment,
            tenantSettings.CscIdentifier,
            tenantSettings.CscSecretReference,
            tenantSettings.EstablishmentCode,
            tenantSettings.ExpeditionPointCode,
            IncrementDocumentNumber(tenantSettings.CurrentDocumentNumber),
            tenantSettings.StampingNumber,
            tenantSettings.CertificateSecretReference,
            tenantSettings.CertificatePasswordSecretReference,
            tenantSettings.CertificateAlias,
            tenantSettings.XmlSchemaRootPath,
            tenantSettings.EndpointUrl,
            tenantSettings.TransportMode);
    }

    private async Task<CreateInvoiceResult> CompleteInternalValidationFailureAsync(
        SifenDocument document,
        TenantSifenSettings? tenantSettings,
        string emittedDocumentNumber,
        string statusCode,
        string detail,
        string logEventType,
        CancellationToken cancellationToken)
    {
        document.MarkInternalValidationFailed(statusCode, detail, detail, _clock.UtcNow);
        _dbContext.DocumentErrors.Add(BuildErrorRecord(
            document,
            statusCode,
            SifenErrorCategory.Configuration,
            detail,
            "No pudimos completar la validacion local de esta factura.",
            "Revisa certificado, XSD y CSC antes de volver a intentarlo.",
            false,
            detail));
        _dbContext.DocumentLogs.Add(CreateLog(
            document.TenantId,
            document.Id,
            DocumentLogLevel.Error,
            logEventType,
            detail,
            JsonSerializer.Serialize(new
            {
                document.Cdc,
                statusCode,
                document.Environment
            })));
        AdvanceTenantDocumentNumberIfNeeded(tenantSettings, emittedDocumentNumber);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new CreateInvoiceResult(
            document.Id,
            document.Cdc,
            document.Status,
            document.TotalAmount,
            document.XmlPayload,
            document.SignedXmlPayload,
            document.StatusCode,
            document.StatusMessage,
            document.InternalStatus.ToString(),
            document.CorrelationId,
            "Factura creada y validada internamente en entorno TEST.");
    }

    private async Task<CreateInvoiceResult> CompleteUnsignedLocalDraftAsync(
        SifenDocument document,
        TenantSifenSettings? tenantSettings,
        string emittedDocumentNumber,
        string detail,
        CancellationToken cancellationToken)
    {
        document.MarkDraftValidatedWithoutSignature(
            "DRAFT_VALIDATED_WITHOUT_SIGNATURE",
            UnsignedLocalValidationMessage,
            detail,
            _clock.UtcNow);
        _dbContext.DocumentErrors.Add(BuildErrorRecord(
            document,
            "DRAFT_VALIDATED_WITHOUT_SIGNATURE",
            SifenErrorCategory.Configuration,
            detail,
            UnsignedLocalValidationMessage,
            "Completa certificado, XSD y CSC cuando quieras pasar a validacion completa.",
            false,
            detail));
        _dbContext.DocumentLogs.Add(CreateLog(
            document.TenantId,
            document.Id,
            DocumentLogLevel.Warning,
            "internal.validation.degraded",
            UnsignedLocalValidationMessage,
            JsonSerializer.Serialize(new
            {
                document.Cdc,
                document.Environment,
                detail
            })));
        AdvanceTenantDocumentNumberIfNeeded(tenantSettings, emittedDocumentNumber);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new CreateInvoiceResult(
            document.Id,
            document.Cdc,
            document.Status,
            document.TotalAmount,
            document.XmlPayload,
            document.SignedXmlPayload,
            document.StatusCode,
            document.StatusMessage,
            document.InternalStatus.ToString(),
            document.CorrelationId,
            UnsignedLocalValidationMessage);
    }

    private static List<InvoiceLineCalculation> BuildLineCalculations(IReadOnlyCollection<CreateInvoiceItemCommand> items)
    {
        return items
            .Select((item, index) =>
            {
                var subtotalAmount = Round(item.Quantity * item.UnitPrice);
                var vatAmount = item.VatRate switch
                {
                    10 => Round(subtotalAmount / 11m),
                    5 => Round(subtotalAmount / 21m),
                    _ => 0m
                };
                var exemptAmount = item.VatRate == 0 ? subtotalAmount : 0m;

                return new InvoiceLineCalculation(
                    index + 1,
                    item.Description.Trim(),
                    item.Quantity,
                    item.UnitPrice,
                    item.VatRate,
                    vatAmount,
                    exemptAmount,
                    subtotalAmount,
                    subtotalAmount);
            })
            .ToList();
    }

    private static InvoiceVatType MapVatType(int vatRate)
    {
        return vatRate switch
        {
            10 => InvoiceVatType.Vat10,
            5 => InvoiceVatType.Vat5,
            0 => InvoiceVatType.Exempt,
            _ => throw new DomainException("Unsupported vatRate.")
        };
    }

    private static string MapSaleCondition(InvoiceSaleCondition saleCondition)
    {
        return saleCondition switch
        {
            InvoiceSaleCondition.Cash => "Contado",
            InvoiceSaleCondition.Credit => "Crédito",
            _ => saleCondition.ToString()
        };
    }

    private static string BuildTestCdc(string cdc)
    {
        return $"TEST-{cdc}";
    }

    private static string BuildTestQrText(string documentNumber, string receiverName)
    {
        return $"QR TEST - Documento {documentNumber} - Receptor {receiverName} - no válido para SET";
    }

    private void ApplySubmissionOutcome(
        SifenDocument document,
        SifenSubmissionResult submission,
        ParsedSifenResponse parsed)
    {
        if (!string.IsNullOrWhiteSpace(submission.Endpoint))
        {
            document.MarkPendingSubmission(submission.Endpoint, _clock.UtcNow);
        }

        switch (parsed.StatusHint)
        {
            case SifenDocumentStatus.Accepted:
                document.MarkAccepted(parsed.TrackingId, parsed.StatusCode, parsed.StatusMessage, submission.RawResponse, _clock.UtcNow);
                break;
            case SifenDocumentStatus.Rejected:
                document.MarkRejected(parsed.StatusCode, parsed.StatusMessage, submission.RawResponse, _clock.UtcNow);
                _dbContext.DocumentErrors.Add(BuildErrorRecord(
                    document,
                    parsed.StatusCode ?? "SIFEN_REJECTED",
                    SifenErrorCategory.SifenRejected,
                    parsed.TechnicalMessage ?? parsed.StatusMessage ?? "SIFEN rejected the invoice.",
                    parsed.StatusMessage ?? "SIFEN rechazo esta factura.",
                    "Revisa los datos de la factura y vuelve a emitirla cuando la correccion este lista.",
                    false,
                    submission.RawResponse));
                break;
            case SifenDocumentStatus.Failed:
                document.MarkFailed(parsed.StatusCode, parsed.StatusMessage, submission.RawResponse, _clock.UtcNow);
                _dbContext.DocumentErrors.Add(BuildErrorRecord(
                    document,
                    parsed.StatusCode ?? "SIFEN_TRANSPORT_ERROR",
                    SifenErrorCategory.SifenTransport,
                    parsed.TechnicalMessage ?? parsed.StatusMessage ?? "SIFEN transport failed.",
                    parsed.StatusMessage ?? "No pudimos completar el envio a SIFEN.",
                    IsTechnicalRetryCode(parsed.StatusCode)
                        ? "Puedes reintentar esta factura cuando el servicio vuelva a estar disponible."
                        : "Revisa la configuracion antes de volver a intentar.",
                    IsTechnicalRetryCode(parsed.StatusCode),
                    submission.RawResponse));
                break;
            default:
                document.MarkSubmitted(parsed.TrackingId, parsed.StatusCode, parsed.StatusMessage, submission.RawResponse, _clock.UtcNow);
                break;
        }
    }

    private void AddSubmissionLogs(
        Guid tenantId,
        Guid documentId,
        SifenSubmissionResult submission,
        ParsedSifenResponse parsed)
    {
        _dbContext.DocumentLogs.Add(CreateLog(
            tenantId,
            documentId,
            submission.IsAcceptedByGateway ? DocumentLogLevel.Information : DocumentLogLevel.Warning,
            "sifen.request",
            submission.IsDiagnostic
                ? "SIFEN request captured in diagnostic mode."
                : "SIFEN request prepared for transport.",
            JsonSerializer.Serialize(new
            {
                submission.Endpoint,
                requestLength = submission.RequestPayload?.Length,
                requestSha256 = ComputeSha256(submission.RequestPayload),
                submission.TransportCode,
                submission.HttpStatusCode,
                submission.IsDiagnostic
            })));

        _dbContext.DocumentLogs.Add(CreateLog(
            tenantId,
            documentId,
            parsed.StatusHint == SifenDocumentStatus.Accepted
                ? DocumentLogLevel.Information
                : parsed.StatusHint == SifenDocumentStatus.Submitted
                    ? DocumentLogLevel.Warning
                    : DocumentLogLevel.Error,
            "sifen.submission",
            parsed.TechnicalMessage ?? parsed.StatusMessage ?? "Invoice submission processed.",
            JsonSerializer.Serialize(new
            {
                submission.Endpoint,
                submission.TransportCode,
                submission.TransportMessage,
                submission.HttpStatusCode,
                submission.IsDiagnostic,
                parsed.Outcome,
                parsed.Cdc,
                parsed.TrackingId,
                parsed.StatusCode,
                parsed.StatusMessage,
                parsed.TechnicalMessage
            })));
    }

    private void AddRetryResultLog(
        SifenDocument document,
        string actorId,
        int attemptNumber,
        DateTimeOffset attemptedAt,
        string? detail)
    {
        _dbContext.DocumentLogs.Add(CreateLog(
            document.TenantId,
            document.Id,
            document.Status == SifenDocumentStatus.Accepted
                ? DocumentLogLevel.Information
                : document.Status == SifenDocumentStatus.Submitted
                    ? DocumentLogLevel.Warning
                    : DocumentLogLevel.Error,
            "sifen.retry.result",
            "Invoice retry processed.",
            JsonSerializer.Serialize(new
            {
                attemptNumber,
                requestedBy = actorId,
                attemptedAt,
                resultStatus = document.Status.ToString(),
                document.StatusCode,
                document.StatusMessage,
                detail
            })));
    }

    private async Task<int> GetNextRetryAttemptNumberAsync(Guid documentId, CancellationToken cancellationToken)
    {
        var previousAttempts = await _dbContext.DocumentLogs
            .AsNoTracking()
            .CountAsync(
                log => log.DocumentId == documentId && log.EventType == "sifen.retry.attempted",
                cancellationToken);

        return previousAttempts + 1;
    }

    private void EnsureRetryAllowed(SifenDocument document)
    {
        if (document.Status == SifenDocumentStatus.Accepted)
        {
            throw new DomainException("Approved invoices cannot be retried.");
        }

        if (document.Status == SifenDocumentStatus.Rejected)
        {
            throw new DomainException("Rejected invoices require explicit reprocessing.");
        }

        if (document.Status != SifenDocumentStatus.Failed)
        {
            throw new DomainException("Only technical failed invoices can be retried.");
        }

        if (!IsTechnicalRetryCode(document.StatusCode))
        {
            throw new DomainException("Retry is only allowed for technical or transient failures.");
        }

        if (string.IsNullOrWhiteSpace(document.SignedXmlPayload))
        {
            throw new DomainException("Retry requires an existing signed XML payload.");
        }
    }

    private static bool IsTechnicalRetryCode(string? statusCode)
    {
        if (string.IsNullOrWhiteSpace(statusCode))
        {
            return false;
        }

        return statusCode.StartsWith("HTTP_", StringComparison.OrdinalIgnoreCase)
            || statusCode.Equals("NO_RESPONSE", StringComparison.OrdinalIgnoreCase)
            || statusCode.Equals("PENDING_TRANSPORT", StringComparison.OrdinalIgnoreCase)
            || statusCode.Equals("SOAP_TIMEOUT", StringComparison.OrdinalIgnoreCase)
            || statusCode.Equals("SOAP_TRANSPORT_ERROR", StringComparison.OrdinalIgnoreCase)
            || statusCode.Equals("INVALID_RESPONSE", StringComparison.OrdinalIgnoreCase)
            || statusCode.Equals("UNPARSEABLE_RESPONSE", StringComparison.OrdinalIgnoreCase)
            || statusCode.Equals("TECHNICAL_ERROR", StringComparison.OrdinalIgnoreCase)
            || statusCode.Equals("DIAGNOSTIC_MODE_ENABLED", StringComparison.OrdinalIgnoreCase)
            || statusCode.Equals("MISSING_ENDPOINT_CONFIGURATION", StringComparison.OrdinalIgnoreCase)
            || statusCode.Equals("MISSING_CLIENT_CERTIFICATE_CONFIGURATION", StringComparison.OrdinalIgnoreCase)
            || statusCode.Equals("RETRY_RUNTIME_ERROR", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<Dictionary<Guid, SifenDocumentError>> LoadLatestErrorsAsync(
        IReadOnlyCollection<Guid> documentIds,
        CancellationToken cancellationToken)
    {
        if (documentIds.Count == 0)
        {
            return [];
        }

        var errors = await _dbContext.DocumentErrors
            .AsNoTracking()
            .Where(item => item.InvoiceId.HasValue && documentIds.Contains(item.InvoiceId.Value))
            .OrderByDescending(item => item.CreatedAt)
            .ToListAsync(cancellationToken);

        return errors
            .GroupBy(item => item.InvoiceId!.Value)
            .ToDictionary(group => group.Key, group => group.First());
    }

    private static bool IsRetryAllowedForStatus(
        SifenDocumentStatus status,
        bool isRetryableError,
        string? statusCode)
    {
        return status == SifenDocumentStatus.Failed &&
               (isRetryableError || IsTechnicalRetryCode(statusCode));
    }

    private SifenDocumentError BuildErrorRecord(
        SifenDocument document,
        string errorCode,
        SifenErrorCategory category,
        string technicalMessage,
        string userMessage,
        string suggestedAction,
        bool isRetryable,
        string? rawResponse)
    {
        return SifenDocumentError.Create(
            document.TenantId,
            document.Id,
            document.Cdc,
            errorCode,
            category,
            technicalMessage,
            userMessage,
            suggestedAction,
            isRetryable,
            ResolveCorrelationId(),
            rawResponse,
            _clock.UtcNow);
    }

    private string ResolveCorrelationId()
    {
        return Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");
    }

    private async Task RecordRetryAuditAsync(
        SifenDocument document,
        string actorId,
        int attemptNumber,
        DateTimeOffset attemptedAt,
        CancellationToken cancellationToken)
    {
        await _auditTrail.RecordAsync(new AuditEvent
        {
            EventName = "invoice.retry",
            Category = AuditCategory.SifenTransmission,
            Severity = document.Status == SifenDocumentStatus.Accepted
                ? AuditSeverity.Information
                : document.Status == SifenDocumentStatus.Submitted
                    ? AuditSeverity.Warning
                    : AuditSeverity.Error,
            OccurredAt = attemptedAt,
            TenantId = document.TenantId.ToString(),
            ActorId = actorId,
            ResourceType = "SifenDocument",
            ResourceId = document.Id.ToString(),
            Outcome = document.Status.ToString(),
            Metadata = new Dictionary<string, string?>
            {
                ["attemptNumber"] = attemptNumber.ToString(),
                ["cdc"] = document.Cdc,
                ["statusCode"] = document.StatusCode,
                ["statusMessage"] = document.StatusMessage
            }
        }, cancellationToken);
    }

    private static string? ComputeSha256(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));
    }

    private static string NormalizeActorId(string? actorId)
    {
        return string.IsNullOrWhiteSpace(actorId) ? "anonymous" : actorId.Trim();
    }

    private static void ValidateRequired(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException($"{fieldName} is required.");
        }
    }

    private static SifenDocumentLog CreateLog(
        Guid tenantId,
        Guid documentId,
        DocumentLogLevel level,
        string eventType,
        string message,
        string? metadataJson = null)
    {
        return SifenDocumentLog.Create(tenantId, documentId, level, eventType, message, metadataJson);
    }
}
