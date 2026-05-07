using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SifenInvoicing.Application.Diagnostics;
using SifenInvoicing.Application.Invoices;
using SifenInvoicing.Application.Operations;
using SifenInvoicing.Application.Security;
using SifenInvoicing.Application.Tenancy;
using SifenInvoicing.Application.XmlSigning;
using SifenInvoicing.Domain.Common;
using SifenInvoicing.Domain.Documents;
using SifenInvoicing.Domain.Tenants;
using SifenInvoicing.Infrastructure.Persistence;

namespace SifenInvoicing.Infrastructure.Operations;

public sealed class ConfigurationOperationalReadinessReporter : IOperationalReadinessReporter
{
    private const string DiagnosticTransportMode = "Diagnostic";
    private const string LiveTransportMode = "Live";

    private readonly IConfiguration _configuration;
    private readonly ISystemClock _clock;
    private readonly ITenantContextAccessor _tenantContextAccessor;
    private readonly SifenDbContext _dbContext;
    private readonly ITenantSecretProvider _secretProvider;
    private readonly ITenantCertificateValidator _certificateValidator;
    private readonly IFacturaXmlGenerator _xmlGenerator;
    private readonly IFacturaXmlPreSubmissionValidator _xmlPreSubmissionValidator;
    private readonly IXmlDocumentSigner _xmlDocumentSigner;

    public ConfigurationOperationalReadinessReporter(
        IConfiguration configuration,
        ISystemClock clock,
        ITenantContextAccessor tenantContextAccessor,
        SifenDbContext dbContext,
        ITenantSecretProvider secretProvider,
        ITenantCertificateValidator certificateValidator,
        IFacturaXmlGenerator xmlGenerator,
        IFacturaXmlPreSubmissionValidator xmlPreSubmissionValidator,
        IXmlDocumentSigner xmlDocumentSigner)
    {
        _configuration = configuration;
        _clock = clock;
        _tenantContextAccessor = tenantContextAccessor;
        _dbContext = dbContext;
        _secretProvider = secretProvider;
        _certificateValidator = certificateValidator;
        _xmlGenerator = xmlGenerator;
        _xmlPreSubmissionValidator = xmlPreSubmissionValidator;
        _xmlDocumentSigner = xmlDocumentSigner;
    }

    public async Task<IReadOnlyCollection<OperationalDependencyStatus>> GetSnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        var tenant = _tenantContextAccessor.Current;
        var checkedAt = _clock.UtcNow;
        var activeEnvironment = _configuration["Sifen:ActiveEnvironment"] ?? "Test";
        var activeBaseUrl = _configuration[$"Sifen:Environments:{activeEnvironment}:BaseUrl"];
        var certificatePath = _configuration["Sifen:Certificate:Path"];
        var certificatePasswordVariable = _configuration["Sifen:Certificate:PasswordEnvironmentVariable"];
        var connectionString = _configuration.GetConnectionString("DefaultConnection");

        IReadOnlyCollection<OperationalDependencyStatus> result =
        [
            BuildInternalApiStatus(checkedAt, tenant),
            BuildSifenEndpointStatus(checkedAt, tenant, activeEnvironment, activeBaseUrl),
            BuildCertificateStatus(checkedAt, tenant, certificatePath, certificatePasswordVariable),
            await BuildSqlServerStatusAsync(checkedAt, tenant, connectionString, cancellationToken),
            BuildBackgroundWorkerStatus(checkedAt, tenant)
        ];

        return result;
    }

    public async Task<TenantFeOperationalDiagnostic> GetTenantFeDiagnosticAsync(
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContextAccessor.Current.ResolvedTenantId
            ?? throw new DomainException("Tenant context is required for FE diagnostic.");

        return await GetTenantFeDiagnosticAsync(
            tenantId,
            ParseEnvironment(_configuration["Sifen:ActiveEnvironment"]),
            cancellationToken);
    }

    public async Task<TenantFeOperationalDiagnostic> GetTenantFeDiagnosticAsync(
        SifenEnvironmentType environment,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContextAccessor.Current.ResolvedTenantId
            ?? throw new DomainException("Tenant context is required for FE diagnostic.");

        return await GetTenantFeDiagnosticAsync(
            tenantId,
            environment,
            cancellationToken);
    }

    public async Task<TenantFeOperationalDiagnostic> GetTenantFeDiagnosticAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        return await GetTenantFeDiagnosticAsync(
            tenantId,
            ParseEnvironment(_configuration["Sifen:ActiveEnvironment"]),
            cancellationToken);
    }

    public async Task<TenantFeOperationalDiagnostic> GetTenantFeDiagnosticAsync(
        Guid tenantId,
        SifenEnvironmentType environment,
        CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        var baseUrl = _configuration[$"Sifen:Environments:{environment}:BaseUrl"];
        var receivePath = _configuration[$"Sifen:Environments:{environment}:Wsdl:Receive"];
        var xsdRootPath = _configuration["Sifen:XmlSchemas:Invoice01RootPath"];
        var transportCertificatePath = _configuration["Sifen:Transport:ClientCertificatePath"];
        var transportCertificatePasswordVariable = _configuration["Sifen:Transport:ClientCertificatePasswordEnvironmentVariable"];

        var tenant = await _dbContext.Tenants
            .AsNoTracking()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(item => item.Id == tenantId, cancellationToken);

        var taxpayerProfile = await _dbContext.TaxpayerProfiles
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(item => item.TenantId == tenantId && item.IsActive)
            .OrderByDescending(item => item.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var sifenSettings = await _dbContext.TenantSifenSettings
            .AsNoTracking()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(item =>
                item.TenantId == tenantId &&
                item.Environment == environment &&
                item.IsActive,
                cancellationToken);

        var latestCertificate = await _dbContext.TenantCertificateMetadata
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(item =>
                item.TenantId == tenantId &&
                item.Environment == environment &&
                item.Purpose == CertificatePurpose.XmlSignature &&
                item.IsActive)
            .OrderByDescending(item => item.ValidTo)
            .FirstOrDefaultAsync(cancellationToken);

        var latestSubmission = await _dbContext.Documents
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(item => item.TenantId == tenantId && item.SubmittedAt != null)
            .OrderByDescending(item => item.SubmittedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var latestError = await _dbContext.Documents
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(item =>
                item.TenantId == tenantId &&
                (item.Status == SifenDocumentStatus.Failed || item.Status == SifenDocumentStatus.Rejected))
            .OrderByDescending(item => item.FinalizedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var transportMode = NormalizeTransportMode(sifenSettings?.TransportMode ?? _configuration["Sifen:Transport:Mode"]);
        var effectiveEndpoint = !string.IsNullOrWhiteSpace(sifenSettings?.EndpointUrl)
            ? sifenSettings.EndpointUrl
            : BuildConfiguredEndpoint(baseUrl, receivePath);
        var effectiveXsdRootPath = !string.IsNullOrWhiteSpace(sifenSettings?.XmlSchemaRootPath)
            ? sifenSettings.XmlSchemaRootPath
            : xsdRootPath;

        var monthlyLimitStatus = await BuildMonthlyLimitStatusAsync(tenant, now, cancellationToken);
        var cscCheck = sifenSettings is null
            ? new SecretCheckResult("csc.secret", SecretStatus.Missing, "SIFEN settings are missing for the selected environment.")
            : await _secretProvider.CheckStringSecretAsync(sifenSettings.CscSecretReference, "csc.secret", cancellationToken);
        var certificateValidation = await _certificateValidator.ValidateAsync(latestCertificate, cancellationToken);

        var checks = new List<TenantFeOperationalCheck>
        {
            BuildCheck("TENANT_EXISTS", tenant is not null, tenant is null ? "Tenant does not exist." : "Tenant exists."),
            BuildCheck("TENANT_ACTIVE", tenant?.Status == TenantStatus.Active, tenant?.Status == TenantStatus.Active ? "Tenant is active." : "Tenant is inactive."),
            BuildCheck("PLAN_ACTIVE", tenant?.Status == TenantStatus.Active, tenant?.Status == TenantStatus.Active ? "Commercial plan is active." : "Commercial plan is inactive because the tenant is suspended."),
            BuildCheck("MONTHLY_LIMIT_AVAILABLE", monthlyLimitStatus.IsReady, monthlyLimitStatus.Message),
            BuildCheck("TAXPAYER_PROFILE_CONFIGURED", taxpayerProfile is not null, taxpayerProfile is null ? "Active TaxpayerProfile is missing." : "Active TaxpayerProfile is configured."),
            BuildCheck("RUC_PRESENT", !string.IsNullOrWhiteSpace(taxpayerProfile?.RucNumber) && !string.IsNullOrWhiteSpace(taxpayerProfile?.RucCheckDigit), taxpayerProfile is null ? "RUC cannot be validated because TaxpayerProfile is missing." : "RUC is configured."),
            BuildCheck("LEGAL_NAME_PRESENT", !string.IsNullOrWhiteSpace(taxpayerProfile?.LegalName), taxpayerProfile is null ? "Legal name cannot be validated because TaxpayerProfile is missing." : "Legal name is configured."),
            BuildCheck("ENVIRONMENT_CONFIGURED", sifenSettings is not null, sifenSettings is null ? $"No active SIFEN settings exist for environment {environment}." : $"SIFEN settings are configured for environment {environment}."),
            BuildCheck("CSC_REFERENCE_PRESENT", !string.IsNullOrWhiteSpace(sifenSettings?.CscSecretReference), sifenSettings is null ? "CSC reference cannot be validated because SIFEN settings are missing." : "CSC secret reference is configured."),
            BuildCheck("CSC_SECRET_AVAILABLE", cscCheck.IsReady, cscCheck.Summary),
            BuildCheck("SIGNATURE_CERTIFICATE_REFERENCE_PRESENT", latestCertificate is not null || !string.IsNullOrWhiteSpace(sifenSettings?.CertificateSecretReference), latestCertificate is not null ? "Signature certificate metadata is configured." : !string.IsNullOrWhiteSpace(sifenSettings?.CertificateSecretReference) ? "Signature certificate reference is configured in tenant settings." : "Signature certificate reference is missing."),
            BuildCheck("SIGNATURE_CERTIFICATE_LOADED", latestCertificate is not null && certificateValidation.Checks.Any(check => check.Name == "certificate.pfx" && check.IsReady), latestCertificate is null ? "Signature certificate metadata is missing." : certificateValidation.Summary),
            BuildCheck("SIGNATURE_CERTIFICATE_VALID", latestCertificate is not null && certificateValidation.IsReady, latestCertificate is null ? "Signature certificate metadata is missing." : certificateValidation.Summary),
            BuildCheck("SECRET_REFERENCES_ONLY", UsesSecretReferencesOnly(sifenSettings, latestCertificate), UsesSecretReferencesOnly(sifenSettings, latestCertificate) ? "Secrets are stored by reference and are not exposed by this diagnostic." : "One or more tenant secret fields do not use a supported secret reference scheme."),
            BuildCheck("ESTABLISHMENT_CONFIGURED", !string.IsNullOrWhiteSpace(sifenSettings?.EstablishmentCode), sifenSettings is null ? "Establishment cannot be validated because SIFEN settings are missing." : "Establishment code is configured."),
            BuildCheck("EXPEDITION_POINT_CONFIGURED", !string.IsNullOrWhiteSpace(sifenSettings?.ExpeditionPointCode), sifenSettings is null ? "Expedition point cannot be validated because SIFEN settings are missing." : "Expedition point code is configured."),
            BuildCheck("DOCUMENT_NUMBER_CONFIGURED", !string.IsNullOrWhiteSpace(sifenSettings?.CurrentDocumentNumber), sifenSettings is null ? "Current document number cannot be validated because SIFEN settings are missing." : "Current document number is configured."),
            BuildCheck("XSD_ROOT_PATH_CONFIGURED", !string.IsNullOrWhiteSpace(effectiveXsdRootPath), string.IsNullOrWhiteSpace(effectiveXsdRootPath) ? "Invoice TipoDoc 01 XSD root path is missing." : "Invoice TipoDoc 01 XSD root path is configured."),
            BuildCheck("XSD_ROOT_PATH_AVAILABLE", !string.IsNullOrWhiteSpace(effectiveXsdRootPath) && File.Exists(effectiveXsdRootPath), string.IsNullOrWhiteSpace(effectiveXsdRootPath) ? "Invoice TipoDoc 01 XSD root path is missing." : File.Exists(effectiveXsdRootPath) ? "Invoice TipoDoc 01 XSD root path exists." : "Invoice TipoDoc 01 XSD root path does not exist on disk."),
            BuildTransportModeCheck(transportMode),
            BuildCheck("SIFEN_ENDPOINT_CONFIGURED", !string.IsNullOrWhiteSpace(effectiveEndpoint), !string.IsNullOrWhiteSpace(effectiveEndpoint) ? $"SIFEN endpoint is configured for {environment}." : $"SIFEN endpoint is missing for {environment}."),
            BuildTransportCertificateCheck(transportMode, transportCertificatePath, transportCertificatePasswordVariable)
        };

        var sampleResult = await RunInternalValidationSampleAsync(
            tenantId,
            environment,
            taxpayerProfile,
            sifenSettings,
            cancellationToken);

        checks.Add(sampleResult.BuilderCheck);
        checks.Add(sampleResult.XsdCheck);
        checks.Add(sampleResult.SignatureCheck);

        var requiredForInternalValidation = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "TENANT_EXISTS",
            "TENANT_ACTIVE",
            "PLAN_ACTIVE",
            "MONTHLY_LIMIT_AVAILABLE",
            "TAXPAYER_PROFILE_CONFIGURED",
            "RUC_PRESENT",
            "LEGAL_NAME_PRESENT",
            "ENVIRONMENT_CONFIGURED",
            "CSC_REFERENCE_PRESENT",
            "CSC_SECRET_AVAILABLE",
            "SIGNATURE_CERTIFICATE_REFERENCE_PRESENT",
            "SIGNATURE_CERTIFICATE_LOADED",
            "SIGNATURE_CERTIFICATE_VALID",
            "SECRET_REFERENCES_ONLY",
            "ESTABLISHMENT_CONFIGURED",
            "EXPEDITION_POINT_CONFIGURED",
            "DOCUMENT_NUMBER_CONFIGURED",
            "XSD_ROOT_PATH_CONFIGURED",
            "XSD_ROOT_PATH_AVAILABLE",
            "TRANSPORT_MODE_IDENTIFIED",
            "XML_BUILDER_TYPEDOC01",
            "XML_XSD_VALIDATION",
            "LOCAL_SIGNATURE_EXECUTION"
        };

        var requiredForSifenAttempt = new HashSet<string>(requiredForInternalValidation, StringComparer.OrdinalIgnoreCase)
        {
            "SIFEN_ENDPOINT_CONFIGURED",
            "TRANSPORT_CLIENT_CERTIFICATE_CONFIGURED"
        };

        requiredForSifenAttempt.Remove("TRANSPORT_MODE_IDENTIFIED");
        var requiredTransportMode = string.Equals(transportMode, LiveTransportMode, StringComparison.OrdinalIgnoreCase);

        var readyForInternalValidation = checks
            .Where(check => requiredForInternalValidation.Contains(check.Code))
            .All(check => string.Equals(check.Status, "Passed", StringComparison.Ordinal));

        var readyForSifenTestAttempt = requiredTransportMode &&
            checks.Where(check => requiredForSifenAttempt.Contains(check.Code))
                .All(check => string.Equals(check.Status, "Passed", StringComparison.Ordinal));

        var missing = checks
            .Where(check => string.Equals(check.Status, "Failed", StringComparison.Ordinal))
            .Select(check => check.Message)
            .ToArray();

        var warnings = checks
            .Where(check => string.Equals(check.Status, "Warning", StringComparison.Ordinal))
            .Select(check => check.Message)
            .ToArray();

        var summary = readyForSifenTestAttempt
            ? $"Tenant is ready to attempt the first real FE submission in SIFEN {environment}."
            : readyForInternalValidation
                ? $"Tenant is ready for internal validation, but a real SIFEN {environment} attempt is still blocked."
                : "Tenant configuration is incomplete for internal FE pre-homologation validation.";

        return new TenantFeOperationalDiagnostic(
            tenantId,
            environment.ToString(),
            transportMode,
            readyForInternalValidation,
            readyForSifenTestAttempt,
            summary,
            missing,
            warnings,
            checks,
            latestSubmission is null
                ? null
                : new TenantFeLastSubmission(
                    latestSubmission.Cdc,
                    latestSubmission.Status.ToString(),
                    latestSubmission.SubmittedAt,
                    latestSubmission.LastSubmissionEndpoint),
            latestError is null
                ? null
                : new TenantFeLastIssue(
                    latestError.Cdc,
                    latestError.Status.ToString(),
                    latestError.StatusCode,
                    latestError.StatusMessage,
                    latestError.FinalizedAt));
    }

    private async Task<InternalValidationSampleResult> RunInternalValidationSampleAsync(
        Guid tenantId,
        SifenEnvironmentType environment,
        TaxpayerProfile? taxpayerProfile,
        TenantSifenSettings? sifenSettings,
        CancellationToken cancellationToken)
    {
        if (taxpayerProfile is null || sifenSettings is null)
        {
            return new InternalValidationSampleResult(
                BuildCheck("XML_BUILDER_TYPEDOC01", false, "Internal TipoDoc 01 XML generation could not run because tenant fiscal configuration is incomplete."),
                BuildCheck("XML_XSD_VALIDATION", false, "TipoDoc 01 XSD validation could not run because XML generation did not complete."),
                BuildCheck("LOCAL_SIGNATURE_EXECUTION", false, "Local XML signature could not run because XML generation did not complete."));
        }

        GeneratedFacturaXmlResult? generated;
        try
        {
            generated = _xmlGenerator.GenerateFacturaXML(BuildInternalValidationInput(taxpayerProfile, sifenSettings));
        }
        catch (Exception ex) when (ex is DomainException or InvalidOperationException)
        {
            return new InternalValidationSampleResult(
                BuildCheck("XML_BUILDER_TYPEDOC01", false, $"Internal TipoDoc 01 XML generation failed: {ex.Message}"),
                BuildCheck("XML_XSD_VALIDATION", false, "TipoDoc 01 XSD validation could not run because XML generation did not complete."),
                BuildCheck("LOCAL_SIGNATURE_EXECUTION", false, "Local XML signature could not run because XML generation did not complete."));
        }

        var builderCheck = BuildCheck(
            "XML_BUILDER_TYPEDOC01",
            true,
            "Internal TipoDoc 01 XML generation completed.");

        TenantFeOperationalCheck xsdCheck;
        try
        {
            await _xmlPreSubmissionValidator.ValidateTipoDoc01Async(generated.Xml, generated.Cdc, tenantId, environment, cancellationToken);
            xsdCheck = BuildCheck("XML_XSD_VALIDATION", true, "TipoDoc 01 XML passed local XSD validation.");
        }
        catch (Exception ex) when (ex is DomainException or InvalidOperationException)
        {
            xsdCheck = BuildCheck("XML_XSD_VALIDATION", false, ex.Message);
        }

        TenantFeOperationalCheck signatureCheck;
        try
        {
            await _xmlDocumentSigner.SignAsync(
                new SignXmlDocumentCommand(
                    tenantId,
                    environment,
                    generated.Cdc,
                    generated.Xml),
                cancellationToken);

            signatureCheck = BuildCheck("LOCAL_SIGNATURE_EXECUTION", true, "Local XML signature completed.");
        }
        catch (Exception ex) when (ex is DomainException or InvalidOperationException)
        {
            signatureCheck = BuildCheck("LOCAL_SIGNATURE_EXECUTION", false, $"Local XML signature failed: {ex.Message}");
        }

        return new InternalValidationSampleResult(builderCheck, xsdCheck, signatureCheck);
    }

    private GenerateFacturaXmlInput BuildInternalValidationInput(
        TaxpayerProfile taxpayerProfile,
        TenantSifenSettings sifenSettings)
    {
        var issueDate = _clock.UtcNow;
        return new GenerateFacturaXmlInput(
            new Application.Cdc.GenerateCdcInput(
                "01",
                taxpayerProfile.RucNumber,
                taxpayerProfile.RucCheckDigit,
                sifenSettings.EstablishmentCode,
                sifenSettings.ExpeditionPointCode,
                sifenSettings.CurrentDocumentNumber,
                "1",
                "1",
                "123456789",
                issueDate.ToString("yyyyMMdd")),
            issueDate,
            1,
            taxpayerProfile.LegalName,
            "Internal validation address",
            "Internal Validation Customer",
            InvoiceReceiverDocumentType.Ci,
            "1234567",
            InvoiceCurrency.PYG,
            InvoiceSaleCondition.Cash,
            [new GenerateFacturaXmlItemInput("Internal validation item", 1m, 10000m, InvoiceVatType.Vat10)]);
    }

    private async Task<MonthlyLimitStatus> BuildMonthlyLimitStatusAsync(
        Tenant? tenant,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (tenant is null)
        {
            return new MonthlyLimitStatus(false, "Monthly invoice limit cannot be validated because tenant does not exist.");
        }

        if (!tenant.MaxInvoicesPerMonth.HasValue)
        {
            return new MonthlyLimitStatus(true, "Monthly invoice limit is not configured.");
        }

        var monthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var nextMonthStart = monthStart.AddMonths(1);
        var usedInvoices = await _dbContext.Documents
            .AsNoTracking()
            .IgnoreQueryFilters()
            .CountAsync(item =>
                item.TenantId == tenant.Id &&
                item.Kind == SifenDocumentKind.Invoice &&
                item.IssuedAt >= monthStart &&
                item.IssuedAt < nextMonthStart,
                cancellationToken);

        return usedInvoices < tenant.MaxInvoicesPerMonth.Value
            ? new MonthlyLimitStatus(true, $"Monthly invoice limit is available ({usedInvoices}/{tenant.MaxInvoicesPerMonth.Value}).")
            : new MonthlyLimitStatus(false, $"Monthly invoice limit was reached ({usedInvoices}/{tenant.MaxInvoicesPerMonth.Value}).");
    }

    private static TenantFeOperationalCheck BuildTransportModeCheck(string transportMode)
    {
        return transportMode switch
        {
            DiagnosticTransportMode or LiveTransportMode => BuildCheck("TRANSPORT_MODE_IDENTIFIED", true, $"Transport mode is {transportMode}."),
            _ => BuildWarning("TRANSPORT_MODE_IDENTIFIED", $"Transport mode '{transportMode}' is unknown. Live emission stays blocked.")
        };
    }

    private static TenantFeOperationalCheck BuildTransportCertificateCheck(
        string transportMode,
        string? transportCertificatePath,
        string? transportCertificatePasswordVariable)
    {
        var configured = !string.IsNullOrWhiteSpace(transportCertificatePath) &&
                         !string.IsNullOrWhiteSpace(transportCertificatePasswordVariable);

        if (string.Equals(transportMode, DiagnosticTransportMode, StringComparison.OrdinalIgnoreCase))
        {
            return configured
                ? BuildCheck("TRANSPORT_CLIENT_CERTIFICATE_CONFIGURED", true, "Transport client certificate is configured.")
                : BuildWarning("TRANSPORT_CLIENT_CERTIFICATE_CONFIGURED", "Transport client certificate is not configured yet. This is acceptable in Diagnostic mode but blocks a real SIFEN attempt.");
        }

        return BuildCheck(
            "TRANSPORT_CLIENT_CERTIFICATE_CONFIGURED",
            configured,
            configured
                ? "Transport client certificate is configured."
                : "Transport client certificate configuration is incomplete.");
    }

    private static bool UsesSecretReferencesOnly(
        TenantSifenSettings? sifenSettings,
        TenantCertificateMetadata? latestCertificate)
    {
        return IsSecretReference(sifenSettings?.CscSecretReference) &&
               IsOptionalSecretReference(sifenSettings?.CertificateSecretReference) &&
               IsOptionalSecretReference(sifenSettings?.CertificatePasswordSecretReference) &&
               IsOptionalSecretReference(latestCertificate?.CertificateSecretReference) &&
               IsOptionalSecretReference(latestCertificate?.CertificatePasswordSecretReference);
    }

    private static bool IsOptionalSecretReference(string? value)
    {
        return string.IsNullOrWhiteSpace(value) || IsSecretReference(value);
    }

    private static bool IsSecretReference(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.StartsWith("config:", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("env:", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("file:", StringComparison.OrdinalIgnoreCase);
    }

    private static string? BuildConfiguredEndpoint(string? baseUrl, string? receivePath)
    {
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(receivePath))
        {
            return null;
        }

        return $"{baseUrl.TrimEnd('/')}{receivePath.Replace("?wsdl", string.Empty, StringComparison.OrdinalIgnoreCase)}";
    }

    private static string NormalizeTransportMode(string? mode)
    {
        if (string.Equals(mode, LiveTransportMode, StringComparison.OrdinalIgnoreCase))
        {
            return LiveTransportMode;
        }

        if (string.Equals(mode, DiagnosticTransportMode, StringComparison.OrdinalIgnoreCase))
        {
            return DiagnosticTransportMode;
        }

        return string.IsNullOrWhiteSpace(mode) ? "Unknown" : mode.Trim();
    }

    private static TenantFeOperationalCheck BuildCheck(string code, bool passed, string message)
    {
        return new TenantFeOperationalCheck(
            code,
            passed ? "Passed" : "Failed",
            message,
            passed);
    }

    private static TenantFeOperationalCheck BuildWarning(string code, string message)
    {
        return new TenantFeOperationalCheck(
            code,
            "Warning",
            message,
            true);
    }

    private static OperationalDependencyStatus BuildInternalApiStatus(
        DateTimeOffset checkedAt,
        TenantContext tenant)
    {
        return new OperationalDependencyStatus
        {
            Name = "internal-api",
            Kind = DependencyKind.InternalApi,
            State = DependencyState.Healthy,
            CheckedAt = checkedAt,
            TenantId = tenant.TenantId,
            Summary = "API process is responding.",
            DiagnosticHint = "If many tenants report failures while this is healthy, inspect SIFEN, certificate, queue and database statuses.",
            Data = new Dictionary<string, string?>
            {
                ["tenant.resolved"] = tenant.IsResolved.ToString()
            }
        };
    }

    private static OperationalDependencyStatus BuildSifenEndpointStatus(
        DateTimeOffset checkedAt,
        TenantContext tenant,
        string activeEnvironment,
        string? activeBaseUrl)
    {
        var configured = !string.IsNullOrWhiteSpace(activeBaseUrl);

        return new OperationalDependencyStatus
        {
            Name = "sifen-endpoints",
            Kind = DependencyKind.SifenEndpointConfiguration,
            State = configured ? DependencyState.Healthy : DependencyState.NotConfigured,
            CheckedAt = checkedAt,
            TenantId = tenant.TenantId,
            Summary = configured
                ? "SIFEN endpoint base URL is configured. Network and mTLS checks are not active yet."
                : "SIFEN endpoint base URL is missing.",
            DiagnosticHint = "When SOAP/mTLS is implemented this status must include connectivity, timeout and response-code evidence.",
            Data = new Dictionary<string, string?>
            {
                ["sifen.environment"] = activeEnvironment,
                ["sifen.base_url_configured"] = configured.ToString()
            }
        };
    }

    private static OperationalDependencyStatus BuildCertificateStatus(
        DateTimeOffset checkedAt,
        TenantContext tenant,
        string? certificatePath,
        string? certificatePasswordVariable)
    {
        var certificatePathConfigured = !string.IsNullOrWhiteSpace(certificatePath);
        var passwordVariableConfigured = !string.IsNullOrWhiteSpace(certificatePasswordVariable);

        return new OperationalDependencyStatus
        {
            Name = "sifen-certificate",
            Kind = DependencyKind.SifenCertificateConfiguration,
            State = certificatePathConfigured && passwordVariableConfigured
                ? DependencyState.Degraded
                : DependencyState.NotConfigured,
            CheckedAt = checkedAt,
            TenantId = tenant.TenantId,
            Summary = certificatePathConfigured
                ? "Certificate path is configured, but certificate loading and RUC validation are not implemented yet."
                : "Certificate path is not configured yet.",
            DiagnosticHint = "Per-tenant certificate storage and validation must be implemented before real SIFEN test calls.",
            Data = new Dictionary<string, string?>
            {
                ["certificate.path_configured"] = certificatePathConfigured.ToString(),
                ["certificate.password_variable_configured"] = passwordVariableConfigured.ToString()
            }
        };
    }

    private static SifenEnvironmentType ParseEnvironment(string? value)
    {
        return Enum.TryParse<SifenEnvironmentType>(value, true, out var environment)
            ? environment
            : SifenEnvironmentType.Test;
    }

    private async Task<OperationalDependencyStatus> BuildSqlServerStatusAsync(
        DateTimeOffset checkedAt,
        TenantContext tenant,
        string? connectionString,
        CancellationToken cancellationToken)
    {
        var configured = !string.IsNullOrWhiteSpace(connectionString);
        var canConnect = false;
        string? errorType = null;

        if (configured)
        {
            try
            {
                canConnect = await _dbContext.Database.CanConnectAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                errorType = ex.GetType().Name;
            }
        }

        return new OperationalDependencyStatus
        {
            Name = "sql-server",
            Kind = DependencyKind.SqlServer,
            State = !configured
                ? DependencyState.NotConfigured
                : canConnect
                    ? DependencyState.Healthy
                    : DependencyState.Degraded,
            CheckedAt = checkedAt,
            TenantId = tenant.TenantId,
            Summary = !configured
                ? "SQL Server connection string is not configured yet."
                : canConnect
                    ? "SQL Server is reachable."
                    : "SQL Server connection string is configured, but the database is not reachable.",
            DiagnosticHint = "If this is degraded while SIFEN endpoints are healthy, document persistence and audit operations may fail.",
            Data = new Dictionary<string, string?>
            {
                ["connection.default_configured"] = configured.ToString(),
                ["connection.can_connect"] = canConnect.ToString(),
                ["connection.error_type"] = errorType
            }
        };
    }

    private static OperationalDependencyStatus BuildBackgroundWorkerStatus(
        DateTimeOffset checkedAt,
        TenantContext tenant)
    {
        return new OperationalDependencyStatus
        {
            Name = "background-workers",
            Kind = DependencyKind.BackgroundWorkers,
            State = DependencyState.NotConfigured,
            CheckedAt = checkedAt,
            TenantId = tenant.TenantId,
            Summary = "Background workers for batches, polling, retries and dead-letter queues are not implemented yet.",
            DiagnosticHint = "This becomes critical before async SIFEN lote processing.",
            Data = new Dictionary<string, string?>
            {
                ["workers.batch_sender"] = "NotConfigured",
                ["workers.batch_poller"] = "NotConfigured",
                ["workers.dead_letter"] = "NotConfigured"
            }
        };
    }

    private sealed record MonthlyLimitStatus(bool IsReady, string Message);

    private sealed record InternalValidationSampleResult(
        TenantFeOperationalCheck BuilderCheck,
        TenantFeOperationalCheck XsdCheck,
        TenantFeOperationalCheck SignatureCheck);
}
