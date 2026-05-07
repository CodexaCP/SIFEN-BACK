using Microsoft.EntityFrameworkCore;
using SifenInvoicing.Application.Operations;
using SifenInvoicing.Domain.Documents;
using SifenInvoicing.Domain.Tenants;
using SifenInvoicing.Infrastructure.Persistence;

namespace SifenInvoicing.Infrastructure.Operations;

public sealed class EfFeTenantDiagnosticService : IFeTenantDiagnosticService
{
    private const string TestMode = "TEST_INTERNAL";

    private readonly SifenDbContext _dbContext;

    public EfFeTenantDiagnosticService(SifenDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<FeTenantDiagnosticResult> GetDiagnosticAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var tenant = await _dbContext.Tenants
            .AsNoTracking()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(item => item.Id == tenantId, cancellationToken);
        var taxpayer = await _dbContext.TaxpayerProfiles
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(item => item.TenantId == tenantId && item.IsActive)
            .OrderByDescending(item => item.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        var settings = await _dbContext.TenantSifenSettings
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(item => item.TenantId == tenantId && item.Environment == SifenEnvironmentType.Test && item.IsActive)
            .OrderByDescending(item => item.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        var certificateMetadata = await _dbContext.TenantCertificateMetadata
            .AsNoTracking()
            .IgnoreQueryFilters()
            .AnyAsync(item =>
                item.TenantId == tenantId &&
                item.Environment == SifenEnvironmentType.Test &&
                item.Purpose == CertificatePurpose.XmlSignature &&
                item.IsActive,
                cancellationToken);

        var monthStart = new DateTimeOffset(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var nextMonthStart = monthStart.AddMonths(1);
        var usedInvoices = await _dbContext.Documents
            .AsNoTracking()
            .IgnoreQueryFilters()
            .CountAsync(item =>
                item.TenantId == tenantId &&
                item.Kind == SifenDocumentKind.Invoice &&
                item.IssuedAt >= monthStart &&
                item.IssuedAt < nextMonthStart,
                cancellationToken);

        var checks = new List<FeTenantDiagnosticCheck>
        {
            BuildCheck("tenant-active", "Tenant activo", tenant?.Status == TenantStatus.Active, tenant is null ? "El tenant no existe." : tenant.Status == TenantStatus.Active ? "El tenant puede operar." : "El tenant esta inactivo o suspendido."),
            BuildCheck("plan-active", "Plan activo", tenant?.Status == TenantStatus.Active, tenant is null ? "No se puede validar el plan porque el tenant no existe." : tenant.Status == TenantStatus.Active ? "El plan comercial esta habilitado." : "El plan comercial esta bloqueado por el estado del tenant."),
            BuildCheck("monthly-limit", "Limite mensual disponible", !tenant?.MaxInvoicesPerMonth.HasValue ?? true || usedInvoices < tenant!.MaxInvoicesPerMonth!.Value, tenant is null ? "No se puede validar el limite mensual porque el tenant no existe." : tenant.MaxInvoicesPerMonth.HasValue ? $"Uso actual {usedInvoices} de {tenant.MaxInvoicesPerMonth.Value} facturas." : $"Sin limite mensual configurado. Uso actual {usedInvoices}."),
            BuildCheck("ruc-configured", "RUC configurado", !string.IsNullOrWhiteSpace(taxpayer?.RucNumber) && !string.IsNullOrWhiteSpace(taxpayer?.RucCheckDigit), taxpayer is null ? "Falta el perfil tributario activo." : "El RUC esta cargado."),
            BuildCheck("establishment-configured", "Establecimiento configurado", !string.IsNullOrWhiteSpace(settings?.EstablishmentCode), settings is null ? "Falta configuracion SIFEN TEST del tenant." : "El establecimiento esta configurado."),
            BuildCheck("expedition-point-configured", "Punto de expedicion configurado", !string.IsNullOrWhiteSpace(settings?.ExpeditionPointCode), settings is null ? "Falta configuracion SIFEN TEST del tenant." : "El punto de expedicion esta configurado."),
            BuildCheck("numbering-configured", "Numeracion configurada", !string.IsNullOrWhiteSpace(settings?.CurrentDocumentNumber), settings is null ? "Falta configuracion SIFEN TEST del tenant." : "La numeracion actual esta configurada."),
            BuildWarningCheck("csc-present", "CSC presente", !string.IsNullOrWhiteSpace(settings?.CscIdentifier) && !string.IsNullOrWhiteSpace(settings?.CscSecretReference), settings is null ? "No hay settings TEST para revisar CSC." : !string.IsNullOrWhiteSpace(settings.CscIdentifier) && !string.IsNullOrWhiteSpace(settings.CscSecretReference) ? "El CSC esta referenciado sin exponer el valor." : "Aun falta cargar la referencia del CSC TEST."),
            BuildWarningCheck("certificate-metadata-present", "Certificado metadata presente", certificateMetadata, certificateMetadata ? "Existe metadata de certificado para TEST." : "Todavia no hay metadata de certificado TEST cargada."),
            BuildWarningCheck("xsd-path-configured", "XSD path configurado", !string.IsNullOrWhiteSpace(settings?.XmlSchemaRootPath), settings is null ? "No hay settings TEST para revisar XSD." : !string.IsNullOrWhiteSpace(settings.XmlSchemaRootPath) ? "El path del XSD esta configurado." : "El path del XSD queda pendiente en esta base TEST."),
            new("mode", "Modo actual", "OK", TestMode)
        };

        var globalStatus = checks.Any(item => item.Status == "ERROR")
            ? "BLOCKED"
            : checks.Any(item => item.Status == "WARNING")
                ? "PARTIAL"
                : "READY_TEST";

        return new FeTenantDiagnosticResult(tenantId, globalStatus, TestMode, checks);
    }

    private static FeTenantDiagnosticCheck BuildCheck(string key, string label, bool isOk, string message)
        => new(key, label, isOk ? "OK" : "ERROR", message);

    private static FeTenantDiagnosticCheck BuildWarningCheck(string key, string label, bool isOk, string message)
        => new(key, label, isOk ? "OK" : "WARNING", message);
}
