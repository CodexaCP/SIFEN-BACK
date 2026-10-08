using System.Text.Json;
using System.Security.Cryptography;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using SifenInvoicing.Application.Auditing;
using SifenInvoicing.Application.Cdc;
using SifenInvoicing.Application.Diagnostics;
using SifenInvoicing.Application.Fiscal;
using SifenInvoicing.Application.Numbering;
using SifenInvoicing.Application.Invoices;
using SifenInvoicing.Application.Operations;
using SifenInvoicing.Application.Security;
using SifenInvoicing.Application.Sifen;
using SifenInvoicing.Application.Tenancy;
using SifenInvoicing.Application.XmlDe;
using SifenInvoicing.Application.XmlSigning;
using SifenInvoicing.Domain.Common;
using SifenInvoicing.Domain.Documents;
using SifenInvoicing.Domain.Tenants;
using SifenInvoicing.Infrastructure.Persistence;

namespace SifenInvoicing.Infrastructure.Invoices;

public sealed class EfInvoiceService : IInvoiceService
{
    private const string UnsignedLocalValidationMessage = "XML generated locally. Certificate/XSD/CSC are missing for full validation.";

    /// <summary>Manual v150: iTiDE 01 = factura electronica; iTipEmi 1 = emision normal.</summary>
    private const string FacturaDocumentTypeCode = "01";
    private const string NormalEmissionType = "1";
    private const string FacturaDocumentTypeName = "Factura electrónica";

    private readonly SifenDbContext _dbContext;
    private readonly ITenantContextAccessor _tenantContextAccessor;
    private readonly SifenDeXmlBuilder _deXmlBuilder;
    private readonly ISifenDeXsdValidator _deXsdValidator;
    private readonly IInvoiceKudePdfRenderer _invoiceKudePdfRenderer;
    private readonly ITenantCertificateValidator _tenantCertificateValidator;
    private readonly IXmlDocumentSigner _xmlDocumentSigner;
    private readonly IOperationalReadinessReporter _operationalReadinessReporter;
    private readonly ISifenSubmissionGateway _submissionGateway;
    private readonly ISifenResponseParser _responseParser;
    private readonly IConfiguration _configuration;
    private readonly IAuditTrail _auditTrail;
    private readonly ISystemClock _clock;
    private readonly INumberingService _numberingService;
    private readonly IFiscalClock _fiscalClock;
    private readonly SifenInvoicing.Application.Qr.ISifenDeQrAttacher _qrAttacher;

    public EfInvoiceService(
        SifenDbContext dbContext,
        ITenantContextAccessor tenantContextAccessor,
        SifenDeXmlBuilder deXmlBuilder,
        ISifenDeXsdValidator deXsdValidator,
        IInvoiceKudePdfRenderer invoiceKudePdfRenderer,
        ITenantCertificateValidator tenantCertificateValidator,
        IXmlDocumentSigner xmlDocumentSigner,
        IOperationalReadinessReporter operationalReadinessReporter,
        ISifenSubmissionGateway submissionGateway,
        ISifenResponseParser responseParser,
        IConfiguration configuration,
        IAuditTrail auditTrail,
        ISystemClock clock,
        INumberingService numberingService,
        IFiscalClock fiscalClock,
        SifenInvoicing.Application.Qr.ISifenDeQrAttacher qrAttacher)
    {
        _dbContext = dbContext;
        _tenantContextAccessor = tenantContextAccessor;
        _deXmlBuilder = deXmlBuilder;
        _deXsdValidator = deXsdValidator;
        _invoiceKudePdfRenderer = invoiceKudePdfRenderer;
        _tenantCertificateValidator = tenantCertificateValidator;
        _xmlDocumentSigner = xmlDocumentSigner;
        _operationalReadinessReporter = operationalReadinessReporter;
        _submissionGateway = submissionGateway;
        _responseParser = responseParser;
        _configuration = configuration;
        _auditTrail = auditTrail;
        _clock = clock;
        _numberingService = numberingService;
        _fiscalClock = fiscalClock;
        _qrAttacher = qrAttacher;
    }

    public async Task<CreateInvoiceResult> CreateAsync(
        CreateInvoiceCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        // 1. Autenticacion/tenant (ya resuelto por la identidad) y validacion de datos comerciales.
        var tenantId = RequireTenant();
        ValidateCommand(command);

        // 2. Idempotency-Key: mismo contenido => misma factura; distinto contenido => 422.
        var requestHash = command.ComputeRequestHash();
        var replay = await TryReplayAsync(tenantId, command.IdempotencyKey, requestHash, cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        // 3. Configuracion fiscal del tenant.
        var environment = ResolveEnvironment();
        var tenant = await RequireTenantForEmissionAsync(tenantId, cancellationToken);
        var tenantSettings = await GetTenantSifenSettingsAsync(tenantId, environment, cancellationToken);
        await EnsureTenantInvoicePlanAllowsEmissionAsync(tenant, cancellationToken);

        var taxpayer = await _dbContext.TaxpayerProfiles
            .OrderByDescending(profile => profile.CreatedAt)
            .FirstOrDefaultAsync(profile => profile.IsActive, cancellationToken);

        if (taxpayer is null)
        {
            throw new DomainException("Active taxpayer profile was not found for the current tenant.");
        }

        if (taxpayer.TaxpayerType is null || string.IsNullOrWhiteSpace(taxpayer.Address))
        {
            throw new UserFacingException(
                "FISCAL_CONFIGURATION_INCOMPLETE",
                "Configuration",
                "Faltan datos fiscales del emisor (tipo de contribuyente o direccion).",
                "Completa el perfil fiscal de la empresa antes de emitir.",
                false,
                422,
                "Taxpayer profile is missing TaxpayerType or Address.");
        }

        // 3b. Datos del DE01 que no dependen del numero: emisor (gEmis/gActEco), receptor, operacion e items.
        //     Todo dato obligatorio ausente se rechaza AQUI, antes de reservar numero; nada se inventa.
        var economicActivities = await _dbContext.TaxpayerEconomicActivities
            .Where(activity => activity.TaxpayerProfileId == taxpayer.Id)
            .OrderBy(activity => activity.SortOrder)
            .ThenBy(activity => activity.Code)
            .Select(activity => new SifenDeEconomicActivity(activity.Code, activity.Description))
            .ToListAsync(cancellationToken);

        SifenDeEmitter emitter;
        try
        {
            emitter = SifenDeEmitterMapper.Map(taxpayer, economicActivities);
        }
        catch (DomainException ex)
        {
            throw new UserFacingException(
                "FISCAL_CONFIGURATION_INCOMPLETE",
                "Configuration",
                ex.Message,
                "Completa el perfil fiscal del emisor (incluida su actividad economica) antes de emitir.",
                false,
                422,
                ex.Message);
        }

        var requestPlan = SifenDeInputAssembler.PlanRequest(
            command,
            SifenDeInputAssembler.ParseOptionalInt(_configuration["Sifen:De:DefaultTransactionType"]),
            SifenDeInputAssembler.ParseOptionalInt(_configuration["Sifen:De:DefaultPresenceIndicator"]));

        var diagnostic = await EnsureSubmissionIsAllowedAsync(tenantId, environment, cancellationToken);
        var allowUnsignedLocalValidationOverride = IsUnsignedLocalValidationOverrideEnabled(diagnostic.TransportMode);
        var requiresUnsignedLocalDraft = RequiresUnsignedLocalDraft(diagnostic, allowUnsignedLocalValidationOverride);
        var canSignLocally = await CanSignXmlLocallyAsync(
            tenantId,
            environment,
            allowUnsignedLocalValidationOverride,
            cancellationToken);

        // 4. Calculo fiscal (puro, sin XML ni acceso a datos): falla antes de consumir un numero.
        var fiscal = FiscalCalculationEngine.Calculate(
            command.Items
                .Select(item => new FiscalLineInput(item.Quantity, item.UnitPrice, MapVatType(item.VatRate)))
                .ToList(),
            command.Currency.ToString());
        var itemDescriptions = command.Items.Select(item => item.Description.Trim()).ToList();

        // 5. Transaccion unica: reserva de numero -> codigo de seguridad -> CDC -> XML (SifenDeXmlBuilder) -> XSD local
        //    -> persistencia -> commit. Si el XML no se puede construir o no valida contra el XSD oficial, la transaccion
        //    se revierte: el numero no queda consumido y no existe documento. Sin red dentro de la transaccion.
        //    (El proveedor InMemory de pruebas no soporta transacciones.)
        await using IDbContextTransaction? transaction = _dbContext.Database.IsRelational()
            ? await _dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

        SifenDocument document;
        string generatedCdc;
        string generatedXml;
        string correlationId;
        string? signedXml = null;

        try
        {
            var fiscalNow = _fiscalClock.Now;
            var reserved = await _numberingService.ReserveAsync(
                tenantId,
                environment,
                FacturaDocumentTypeCode,
                DateOnly.FromDateTime(fiscalNow.DateTime),
                cancellationToken);
            var securityCode = SecurityCodeGenerator.Generate(reserved.FormattedNumber);

            var cdc = CdcGenerator.GenerateCDC(new GenerateCdcInput(
                FacturaDocumentTypeCode,
                taxpayer.RucNumber,
                taxpayer.RucCheckDigit,
                reserved.EstablishmentCode,
                reserved.ExpeditionPointCode,
                reserved.FormattedNumber,
                taxpayer.TaxpayerType.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                NormalEmissionType,
                securityCode,
                fiscalNow.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture)));

            // El CDC se calcula UNA vez aqui y lo reciben, sin regenerarlo, el builder, el documento y la integridad.
            var buildInput = SifenDeInputAssembler.Assemble(
                requestPlan,
                emitter,
                reserved,
                cdc,
                fiscalNow.DateTime,
                environment == SifenEnvironmentType.Test ? SifenDeEnvironment.Test : SifenDeEnvironment.Production,
                fiscal);
            var built = _deXmlBuilder.Build(buildInput);
            SifenDeXmlIntegrity.Verify(built.Xml, cdc, fiscal);
            await _deXsdValidator.EnsureValidAsync(built.Xml, cancellationToken);
            generatedCdc = built.Cdc;
            generatedXml = built.Xml;

            // Firma XMLDSig del DE ANTES del commit: si falla, la transaccion se revierte (el numero no se consume y no
            // queda documento). El tenant sale siempre de la resolucion del servidor, nunca del cliente.
            if (canSignLocally && !requiresUnsignedLocalDraft)
            {
                signedXml = await SignAndValidateAsync(tenantId, environment, generatedCdc, generatedXml, cancellationToken);
            }

            var totals = fiscal.Totals;
            document = SifenDocument.CreateInvoice(
                tenantId,
                environment,
                generatedCdc,
                FacturaDocumentTypeName,
                reserved.FormattedNumber,
                reserved.EstablishmentCode,
                reserved.ExpeditionPointCode,
                command.Currency.ToString(),
                MapSaleCondition(command.SaleCondition),
                command.ReceptorNombre,
                command.ReceptorDocumento,
                command.ReceptorDireccion,
                command.ReceptorEmail,
                command.ReceptorPhone,
                command.Notes,
                totals.TotalOperacion,
                totals.Iva5,
                totals.Iva10,
                totals.SubExento,
                totals.TotalIva,
                totals.TotalGeneral,
                generatedXml,
                BuildTestCdc(generatedCdc),
                BuildTestQrText(reserved.FormattedNumber, command.ReceptorNombre),
                false,
                fiscalNow);
            document.SetFiscalTrace(reserved.StampingNumber, reserved.SequenceId);
            correlationId = document.EnsureCorrelationId();
            document.SetInternalStatus(FeInvoiceInternalStatus.DRAFT);
            if (signedXml is not null)
            {
                document.MarkSigned(signedXml, _clock.UtcNow);
            }

            _dbContext.Documents.Add(document);
            _dbContext.DocumentLines.AddRange(fiscal.Lines.Select((line, index) =>
            {
                var documentLine = SifenDocumentLine.Create(
                    tenantId,
                    document.Id,
                    line.Number,
                    itemDescriptions[index],
                    line.Quantity,
                    line.UnitPrice,
                    (int)line.TasaIva,
                    line.LiquidacionIva,
                    line.BaseExenta,
                    line.TotalOperacion,
                    line.TotalOperacion);
                var item = requestPlan.Items[index];
                documentLine.SetCatalogData(item.Code, item.UnitCode, item.UnitDescription);
                return documentLine;
            }));
            _dbContext.DocumentLogs.Add(CreateLog(tenantId, document.Id, DocumentLogLevel.Information, "xml.generated", "Invoice XML generated (SifenDeXmlBuilder)."));
            _dbContext.DocumentLogs.Add(CreateLog(tenantId, document.Id, DocumentLogLevel.Information, "xml.validated", "Invoice XML validated against the local official XSD v150 before commit."));
            if (signedXml is not null)
            {
                _dbContext.DocumentLogs.Add(CreateLog(tenantId, document.Id, DocumentLogLevel.Information, "xml.signed", "Invoice XML signed (XMLDSig) and validated against the XSD before commit."));
            }

            _dbContext.FeInvoiceEvents.Add(FeInvoiceEvent.Create(
                tenantId,
                document.Id,
                correlationId,
                null,
                document.InternalStatus.ToString(),
                "InvoiceCreatedTest",
                "Factura creada.",
                null,
                _clock.UtcNow));
            _dbContext.FeTenantLogs.Add(FeTenantLog.Create(
                tenantId,
                document.Id,
                correlationId,
                FeTenantLogLevel.INFO,
                "fe.invoice.create",
                "Factura creada y persistida con numeracion reservada en servidor.",
                JsonSerializer.Serialize(new
                {
                    document.DocumentType,
                    document.ExternalDocumentNumber,
                    totalAmount = totals.TotalGeneral,
                    items = fiscal.Lines.Count
                }),
                _clock.UtcNow));
            _dbContext.IdempotencyRecords.Add(IdempotencyRecord.Create(tenantId, command.IdempotencyKey, requestHash, document.Id));

            await _dbContext.SaveChangesAsync(cancellationToken);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
        }
        catch (DbUpdateException)
        {
            // Carrera con la misma Idempotency-Key (indice unico): se descarta todo y se devuelve la factura ganadora.
            if (transaction is not null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }

            _dbContext.ChangeTracker.Clear();
            var winner = await TryReplayAsync(tenantId, command.IdempotencyKey, requestHash, cancellationToken);
            if (winner is not null)
            {
                return winner;
            }

            throw;
        }

        if (requiresUnsignedLocalDraft)
        {
            return await CompleteUnsignedLocalDraftAsync(
                document,
                BuildUnsignedLocalValidationDetail(diagnostic),
                cancellationToken);
        }

        if (signedXml is null)
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
                    generatedCdc,
                    signed = canSignLocally,
                    environment = environment.ToString(),
                    transportMode = diagnostic.TransportMode
                })));
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
                environment,
                generatedCdc,
                signedXml!),
            cancellationToken);

        var parsed = _responseParser.ParseResponse(submission.RawResponse);
        ApplySubmissionOutcome(document, submission, parsed);
        AddSubmissionLogs(tenantId, document.Id, submission, parsed);

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

    private async Task<string> SignAndValidateAsync(
        Guid tenantId,
        SifenEnvironmentType environment,
        string cdc,
        string unsignedXml,
        CancellationToken cancellationToken)
    {
        string signedXml;
        try
        {
            var signed = await _xmlDocumentSigner.SignAsync(
                new SignXmlDocumentCommand(tenantId, environment, cdc, unsignedXml),
                cancellationToken);
            await _deXsdValidator.EnsureSignedValidAsync(signed.SignedXml, cancellationToken);
            signedXml = signed.SignedXml;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new UserFacingException(
                "XML_SIGNATURE_FAILED",
                "CertificateSignature",
                "No pudimos firmar la factura.",
                "Revisa el certificado de firma de esta compania y vuelve a intentar. No se consumio ningun numero.",
                false,
                409,
                $"XMLDSig signing failed before commit: {ex.Message}");
        }

        // Fase 4.5: el QR se genera DESPUES de firmar, con el DigestValue definitivo y el CSC del tenant efectivo.
        try
        {
            var finalXml = await _qrAttacher.AttachAsync(tenantId, environment, signedXml, cancellationToken);
            await _deXsdValidator.EnsureFinalValidAsync(finalXml, cancellationToken);
            return finalXml;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new UserFacingException(
                "QR_GENERATION_FAILED",
                "Configuration",
                "No pudimos generar el QR de la factura.",
                "Revisa el IdCSC y el CSC de esta compania para el ambiente activo. No se consumio ningun numero.",
                false,
                409,
                $"QR/CSC generation failed before commit: {ex.Message}");
        }
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
        ValidateRequired(command.IdempotencyKey, "Idempotency-Key");

        if (command.IdempotencyKey.Length > 128)
        {
            throw new DomainException("Idempotency-Key must not exceed 128 characters.");
        }

        ValidateRequired(command.ReceptorNombre, nameof(command.ReceptorNombre));
        ValidateRequired(command.ReceptorDocumento, nameof(command.ReceptorDocumento));

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

    /// <summary>Ambiente activo de la plataforma (Sifen:ActiveEnvironment); nunca lo decide el cliente.</summary>
    private SifenEnvironmentType ResolveEnvironment()
    {
        var configured = _configuration["Sifen:ActiveEnvironment"];
        return Enum.TryParse<SifenEnvironmentType>(configured, true, out var parsed)
            ? parsed
            : SifenEnvironmentType.Test;
    }

    private async Task<CreateInvoiceResult?> TryReplayAsync(
        Guid tenantId,
        string idempotencyKey,
        string requestHash,
        CancellationToken cancellationToken)
    {
        var record = await _dbContext.IdempotencyRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.TenantId == tenantId && item.Key == idempotencyKey, cancellationToken);

        if (record is null)
        {
            return null;
        }

        if (!string.Equals(record.RequestHash, requestHash, StringComparison.Ordinal))
        {
            throw new UserFacingException(
                "IDEMPOTENCY_KEY_REUSED",
                "Validation",
                "La Idempotency-Key ya se uso con otro contenido.",
                "Usa una Idempotency-Key nueva para una factura distinta.",
                false,
                422,
                "Idempotency key reused with a different request payload.");
        }

        var document = await _dbContext.Documents
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == record.DocumentId && item.TenantId == tenantId, cancellationToken);

        return document is null
            ? null
            : new CreateInvoiceResult(
                document.Id,
                document.Cdc,
                document.Status,
                document.TotalAmount,
                document.XmlPayload,
                document.SignedXmlPayload,
                document.StatusCode,
                document.StatusMessage,
                document.InternalStatus.ToString(),
                document.CorrelationId ?? string.Empty,
                "Solicitud repetida: se devuelve la factura ya creada.");
    }

    private async Task<CreateInvoiceResult> CompleteInternalValidationFailureAsync(
        SifenDocument document,
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
