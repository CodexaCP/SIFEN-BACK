using System.Collections.Concurrent;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using SifenInvoicing.Application.Auditing;
using SifenInvoicing.Application.Diagnostics;
using SifenInvoicing.Application.Invoices;
using SifenInvoicing.Application.Numbering;
using SifenInvoicing.Application.Operations;
using SifenInvoicing.Application.Security;
using SifenInvoicing.Application.Sifen;
using SifenInvoicing.Application.Tenancy;
using SifenInvoicing.Application.XmlSigning;
using SifenInvoicing.Domain.Common;
using SifenInvoicing.Domain.Documents;
using SifenInvoicing.Domain.Tenants;
using SifenInvoicing.Infrastructure.Diagnostics;
using SifenInvoicing.Infrastructure.Invoices;
using SifenInvoicing.Infrastructure.Numbering;
using SifenInvoicing.Infrastructure.Persistence;
using SifenInvoicing.Infrastructure.Tenancy;

namespace SifenInvoicing.Tests.SqlServer;

// Pruebas de integracion contra SQL Server REAL (separadas de las pruebas SQLite/InMemory).
//
// Requisitos:
//   * Variable de entorno SIFEN_CONNECTION_STRING apuntando a la BD SIFEN_RECONCILE_TEST ya migrada
//     (dotnet ef database update). Las pruebas NO crean ni migran el esquema: fallan si hay migraciones pendientes.
//   * Si la variable no existe, las pruebas se omiten (Skip). Si apunta a otra BD, FALLAN (proteccion).
//
// Ejecutar solo estas:      dotnet test --filter "Category=SqlServerIntegration"
// Ejecutar todo menos estas: dotnet test --filter "Category!=SqlServerIntegration"
//
// Cada prueba crea sus propios tenants (Guid aleatorio) y los borra al finalizar la coleccion; no toca otros datos.

/// <summary>Como [Fact], pero se omite cuando no hay SIFEN_CONNECTION_STRING.</summary>
public sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (!SqlServerTestEnvironment.IsConfigured)
        {
            Skip = $"{SqlServerTestEnvironment.ConnectionStringVariable} no esta definida: pruebas SQL Server omitidas.";
        }
    }
}

public static class SqlServerTestEnvironment
{
    public const string ConnectionStringVariable = "SIFEN_CONNECTION_STRING";
    public const string RequiredDatabase = "SIFEN_RECONCILE_TEST";
    public const string TraitCategory = "SqlServerIntegration";

    public static string? ConnectionString => Environment.GetEnvironmentVariable(ConnectionStringVariable);

    public static bool IsConfigured => !string.IsNullOrWhiteSpace(ConnectionString);
}

[CollectionDefinition(Name)]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerFixture>
{
    public const string Name = "SqlServer";
}

/// <summary>Valida una sola vez que la BD es la correcta y esta migrada; limpia los tenants de prueba al final.</summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    private static readonly string[] TenantScopedTables =
    [
        "IdempotencyRecords", "DocumentLines", "DocumentErrors", "Logs", "FeInvoiceEvents", "FeTenantLogs", "Documents",
        "NumberingSequences", "FiscalStamps", "TaxpayerProfiles", "TenantCertificateMetadata", "TenantSifenSettings",
        "TenantKudeTemplateSettings"
    ];

    private readonly ConcurrentBag<Guid> _tenants = [];

    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        if (!SqlServerTestEnvironment.IsConfigured)
        {
            return;
        }

        ConnectionString = SqlServerTestEnvironment.ConnectionString!;
        var builder = new SqlConnectionStringBuilder(ConnectionString);
        if (!string.Equals(builder.InitialCatalog, SqlServerTestEnvironment.RequiredDatabase, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"SIFEN_CONNECTION_STRING debe apuntar a la BD '{SqlServerTestEnvironment.RequiredDatabase}' (apunta a '{builder.InitialCatalog}'). " +
                "Las pruebas de integracion no se ejecutan contra ninguna otra BD.");
        }

        await using var db = NewPlainContext();
        if (!await db.Database.CanConnectAsync())
        {
            throw new InvalidOperationException($"No se puede conectar a la BD '{SqlServerTestEnvironment.RequiredDatabase}'.");
        }

        var applied = (await db.Database.GetAppliedMigrationsAsync()).ToList();
        var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
        if (applied.Count == 0 || pending.Count > 0)
        {
            throw new InvalidOperationException(
                $"La BD debe estar migrada con 'dotnet ef database update'. Aplicadas: {applied.Count}; pendientes: [{string.Join(", ", pending)}].");
        }
    }

    public async Task DisposeAsync()
    {
        if (!SqlServerTestEnvironment.IsConfigured || _tenants.IsEmpty)
        {
            return;
        }

        await using var db = NewPlainContext();
        foreach (var tenantId in _tenants.Distinct())
        {
            foreach (var table in TenantScopedTables)
            {
#pragma warning disable EF1002 // nombres de tabla constantes de esta clase, no entrada externa
                await db.Database.ExecuteSqlRawAsync($"DELETE FROM [{table}] WHERE [TenantId] = {{0}}", tenantId);
#pragma warning restore EF1002
            }

            await db.Database.ExecuteSqlRawAsync("DELETE FROM [Tenants] WHERE [Id] = {0}", tenantId);
        }
    }

    /// <summary>Contexto sin tenant ni interceptores, solo para validar/limpiar (sin filtros de consulta relevantes).</summary>
    private SifenDbContext NewPlainContext()
    {
        var options = new DbContextOptionsBuilder<SifenDbContext>().UseSqlServer(ConnectionString).Options;
        return new SifenDbContext(options, new SystemClock(), new AsyncLocalTenantContextAccessor());
    }

    /// <summary>
    /// Crea un contexto NUEVO (conexion propia) para un tenant. Debe ser sincrono: el tenant ambiente (AsyncLocal) se
    /// fija en el flujo del llamador. Pasar null deja el flujo sin tenant (fail-closed).
    /// </summary>
    public SifenDbContext NewContext(Guid? tenantId, params IInterceptor[] interceptors)
    {
        var accessor = new AsyncLocalTenantContextAccessor();
        if (tenantId.HasValue)
        {
            accessor.SetCurrent(new TenantContext { TenantId = tenantId.ToString(), ResolvedTenantId = tenantId, IsResolved = true });
        }
        else
        {
            accessor.Clear();
        }

        var options = new DbContextOptionsBuilder<SifenDbContext>()
            .UseSqlServer(ConnectionString, sql => sql.CommandTimeout(60))
            .AddInterceptors(interceptors)
            .Options;
        return new SifenDbContext(options, new SystemClock(), accessor);
    }

    /// <summary>Servicio de facturas completo (sin red) sobre un contexto propio de un tenant.</summary>
    public InvoiceScope NewInvoiceScope(Guid tenantId, INumberingService? numbering = null, params IInterceptor[] interceptors)
    {
        var db = NewContext(tenantId, interceptors);
        var accessor = new AsyncLocalTenantContextAccessor();
        var service = new EfInvoiceService(
            db, accessor, DeTestKit.Builder(), DeTestKit.Xsd(), new InvoiceKudePdfRenderer(),
            new ReadyCertificateValidator(), new FakeSigner(), new StubReadiness(), new CountingGateway(),
            new FakeParser(), SqlServerFakes.Configuration(), new NullAudit(), new SystemClock(),
            numbering ?? new EfNumberingService(db), new TestFiscalClock());
        return new InvoiceScope(service, db, tenantId);
    }

    /// <summary>Tenant completo con configuracion fiscal (perfil, timbrado, secuencia) y certificado de prueba.</summary>
    public async Task<Guid> SeedTenantAsync(string prefix = "sqlsrv", long firstNumber = 1, bool numbering = true, bool taxpayer = true)
    {
        var tenant = Tenant.CreateSharedDatabaseTenant($"{prefix}-{Guid.NewGuid():N}"[..28], "SQLSERVER TEST");
        _tenants.Add(tenant.Id);

        await using var db = NewContext(tenant.Id);
        db.Tenants.Add(tenant);
        if (numbering)
        {
            TestFiscalSetup.Seed(db, tenant.Id, firstNumber: firstNumber, withTaxpayer: taxpayer);
        }
        else if (taxpayer)
        {
            TestFiscalSetup.AddTaxpayer(db, tenant.Id);
        }

        db.TenantCertificateMetadata.Add(TenantCertificateMetadata.Create(
            tenant.Id, SifenEnvironmentType.Test, CertificatePurpose.XmlSignature, "xml-signing", "CN=ACME", "ABC123", "123",
            "config:certificate", "config:password", DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30)));
        await db.SaveChangesAsync();
        return tenant.Id;
    }

    public static CreateInvoiceCommand Command(string key, decimal price = 100000m, string description = "Servicio mensual") =>
        new(key, null, "Cliente Demo", InvoiceReceiverDocumentType.Ci, "1234567", null, null, null,
            InvoiceCurrency.PYG, InvoiceSaleCondition.Cash, [new CreateInvoiceItemCommand(description, 1, price, 10, "SRV-001", 77)]);

    /// <summary>Estado real en SQL Server, leido con un contexto nuevo (sin cache del contexto de la prueba).</summary>
    public async Task<TenantState> ReadStateAsync(Guid tenantId)
    {
        await using var db = NewContext(tenantId);
        return new TenantState(
            (await db.NumberingSequences.SingleAsync()).NextNumber,
            await db.Documents.CountAsync(),
            await db.DocumentLines.CountAsync(),
            await db.IdempotencyRecords.CountAsync(),
            await db.FeInvoiceEvents.CountAsync(),
            await db.FeTenantLogs.CountAsync(),
            (await db.Documents.Select(document => document.ExternalDocumentNumber).ToListAsync()).OrderBy(number => number).ToList());
    }
}

public sealed record InvoiceScope(EfInvoiceService Service, SifenDbContext Db, Guid TenantId) : IAsyncDisposable
{
    public ValueTask DisposeAsync() => Db.DisposeAsync();
}

public sealed record TenantState(
    long NextNumber, int Documents, int Lines, int IdempotencyRecords, int Events, int Logs, IReadOnlyList<string> Numbers);

/// <summary>Cuenta los DbUpdateConcurrencyException que EF detecta (reintentos de la numeracion optimista).</summary>
public sealed class ConcurrencyFailureCounter : SaveChangesInterceptor
{
    private int _count;

    public int Count => Volatile.Read(ref _count);

    public override ValueTask<InterceptionResult> ThrowingConcurrencyExceptionAsync(
        ConcurrencyExceptionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _count);
        return base.ThrowingConcurrencyExceptionAsync(eventData, result, cancellationToken);
    }
}

/// <summary>Detiene la primera operacion de guardado (despues de la lectura) hasta que la prueba la libere.</summary>
public sealed class PauseBeforeFirstSaveInterceptor : SaveChangesInterceptor
{
    private readonly TaskCompletionSource _reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _used;

    public Task Reached => _reached.Task;

    public void Release() => _release.TrySetResult();

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _used, 1) == 0)
        {
            _reached.TrySetResult();
            await _release.Task;
        }

        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}

/// <summary>Falla (dentro de la transaccion, despues de reservar numero) al guardar un documento nuevo.</summary>
public sealed class FailOnDocumentInsertInterceptor : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context!.ChangeTracker.Entries<SifenDocument>().Any(entry => entry.State == EntityState.Added))
        {
            throw new InvalidOperationException("Fallo simulado al guardar el documento (prueba de transaccionalidad).");
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}

internal static class SqlServerFakes
{
    public static IConfiguration Configuration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Sifen:Development:AllowUnsignedInternalValidation"] = "False", ["Sifen:De:DefaultTransactionType"] = "1", ["Sifen:De:DefaultPresenceIndicator"] = "1" })
            .Build();
}


internal sealed class ReadyCertificateValidator : ITenantCertificateValidator
{
    public Task<CertificateValidationResult> ValidateAsync(TenantCertificateMetadata? metadata, CancellationToken cancellationToken = default) =>
        Task.FromResult(new CertificateValidationResult(
            true, "Certificate loaded and validated.", [new SecretCheckResult("certificate.private_key", SecretStatus.Present, "OK")]));
}

internal sealed class FakeSigner : IXmlDocumentSigner
{
    public Task<SignedXmlDocumentResult> SignAsync(SignXmlDocumentCommand command, CancellationToken cancellationToken = default) =>
        Task.FromResult(new SignedXmlDocumentResult($"<signed>{command.Xml}</signed>", command.DocumentId, "c14n", "rsa-sha256", "sha256", "enveloped"));
}

internal sealed class CountingGateway : ISifenSubmissionGateway
{
    public Task<SifenSubmissionResult> SendToSifenAsync(SendToSifenCommand command, CancellationToken cancellationToken = default) =>
        Task.FromResult(new SifenSubmissionResult(true, "https://example.test", null, "<ok />"));
}

internal sealed class FakeParser : ISifenResponseParser
{
    public ParsedSifenResponse ParseResponse(string? rawResponse) =>
        new(SifenResponseOutcome.Approved, SifenDocumentStatus.Accepted, true, true, null, "123456789012345", "0300",
            "Factura electronica aprobada por SIFEN.", "Aprobado");
}

internal sealed class NullAudit : IAuditTrail
{
    public Task RecordAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

internal sealed class StubReadiness : IOperationalReadinessReporter
{
    public Task<IReadOnlyCollection<OperationalDependencyStatus>> GetSnapshotAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyCollection<OperationalDependencyStatus>>([]);

    public Task<TenantFeOperationalDiagnostic> GetTenantFeDiagnosticAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Build(Guid.Empty, SifenEnvironmentType.Test));

    public Task<TenantFeOperationalDiagnostic> GetTenantFeDiagnosticAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Build(tenantId, SifenEnvironmentType.Test));

    public Task<TenantFeOperationalDiagnostic> GetTenantFeDiagnosticAsync(Guid tenantId, SifenEnvironmentType environment, CancellationToken cancellationToken = default) =>
        Task.FromResult(Build(tenantId, environment));

    public Task<TenantFeOperationalDiagnostic> GetTenantFeDiagnosticAsync(SifenEnvironmentType environment, CancellationToken cancellationToken = default) =>
        Task.FromResult(Build(Guid.Empty, environment));

    private static TenantFeOperationalDiagnostic Build(Guid tenantId, SifenEnvironmentType environment) =>
        new(tenantId, environment.ToString(), "Diagnostic", true, true, "stub", [], [], [], null, null);
}
