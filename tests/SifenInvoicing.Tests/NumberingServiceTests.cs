using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using SifenInvoicing.Application.Diagnostics;
using SifenInvoicing.Application.Tenancy;
using SifenInvoicing.Domain.Common;
using SifenInvoicing.Domain.Tenants;
using SifenInvoicing.Infrastructure.Diagnostics;
using SifenInvoicing.Infrastructure.Numbering;
using SifenInvoicing.Infrastructure.Persistence;
using SifenInvoicing.Infrastructure.Tenancy;

namespace SifenInvoicing.Tests;

/// <summary>
/// Concurrencia y transaccionalidad de la numeracion sobre un motor relacional real (SQLite en archivo temporal,
/// una conexion por contexto). El proveedor InMemory no sirve para esto: no tiene transacciones.
/// </summary>
/// <summary>Mismo modelo que produccion; solo adapta tipos de columna exclusivos de SQL Server para poder usar SQLite.</summary>
internal sealed class SqliteTestDbContext : SifenDbContext
{
    public SqliteTestDbContext(DbContextOptions<SifenDbContext> options, ISystemClock clock, ITenantContextAccessor accessor)
        : base(options, clock, accessor)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(entity => entity.GetProperties()))
        {
            var columnType = property.FindAnnotation("Relational:ColumnType")?.Value as string;
            if (columnType is not null && columnType.Contains("max", StringComparison.OrdinalIgnoreCase))
            {
                property.SetColumnType("TEXT");
            }
        }
    }
}

public sealed class NumberingServiceTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"numbering-{Guid.NewGuid():N}.db");
    private readonly Guid _tenantId;
    private readonly Guid _otherTenantId;

    public NumberingServiceTests()
    {
        _tenantId = Seed("tenant-a");
        _otherTenantId = Seed("tenant-b");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var file in new[] { _path, _path + "-wal", _path + "-shm" })
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
    }

    private SifenDbContext NewContext(Guid? tenantId)
    {
        var accessor = new AsyncLocalTenantContextAccessor();
        if (tenantId.HasValue)
        {
            accessor.SetCurrent(new TenantContext { TenantId = tenantId.ToString(), ResolvedTenantId = tenantId, IsResolved = true });
        }

        var options = new DbContextOptionsBuilder<SifenDbContext>()
            .UseSqlite($"Data Source={_path};Default Timeout=60;Pooling=False")
            .Options;
        return new SqliteTestDbContext(options, new SystemClock(), accessor);
    }

    private Guid Seed(string slug)
    {
        var tenant = Tenant.CreateSharedDatabaseTenant(slug, slug);
        using var db = NewContext(tenant.Id);
        db.Database.EnsureCreated();
        db.Tenants.Add(tenant);
        TestFiscalSetup.Seed(db, tenant.Id);
        db.SaveChanges();
        return tenant.Id;
    }

    private static readonly DateOnly Today = new(2026, 4, 25);

    [Fact]
    public async Task Reserve_ShouldNotDuplicateOrSkipNumbers_UnderConcurrency()
    {
        const int parallel = 50;

        var numbers = await Task.WhenAll(Enumerable.Range(0, parallel).Select(_ => Task.Run(async () =>
        {
            // Cada tarea simula un request: su propio contexto, su propia conexion y su propia transaccion.
            await using var db = NewContext(_tenantId);
            var service = new EfNumberingService(db);
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    await using var tx = await db.Database.BeginTransactionAsync();
                    var reserved = await service.ReserveAsync(_tenantId, SifenEnvironmentType.Test, "01", Today);
                    await tx.CommitAsync();
                    return reserved.Number;
                }
                catch (SqliteException ex) when (ex.SqliteErrorCode == 5 && attempt < 200)
                {
                    // SQLite (no SQL Server) puede rechazar la mejora de lectura->escritura bajo contencion; se reintenta la transaccion completa.
                    db.ChangeTracker.Clear();
                    await Task.Delay(5);
                }
            }
        })));

        Assert.Equal(parallel, numbers.Distinct().Count());
        Assert.Equal(Enumerable.Range(1, parallel).Select(n => (long)n), numbers.OrderBy(n => n));

        await using var check = NewContext(_tenantId);
        Assert.Equal(parallel + 1, (await check.NumberingSequences.SingleAsync()).NextNumber);
    }

    [Fact]
    public async Task Reserve_ShouldReleaseNumber_WhenTransactionRollsBack()
    {
        await using (var db = NewContext(_tenantId))
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            var reserved = await new EfNumberingService(db).ReserveAsync(_tenantId, SifenEnvironmentType.Test, "01", Today);
            Assert.Equal(1, reserved.Number);
            await tx.RollbackAsync();
        }

        await using var db2 = NewContext(_tenantId);
        var next = await new EfNumberingService(db2).ReserveAsync(_tenantId, SifenEnvironmentType.Test, "01", Today);

        Assert.Equal(1, next.Number);
        Assert.Equal("0000001", next.FormattedNumber);
        Assert.Equal(TestFiscalSetup.StampingNumber, next.StampingNumber);
    }

    [Fact]
    public async Task ConcurrencyToken_ShouldRejectStaleReservation()
    {
        await using var a = NewContext(_tenantId);
        await using var b = NewContext(_tenantId);
        var seqA = await a.NumberingSequences.SingleAsync();
        var seqB = await b.NumberingSequences.SingleAsync();

        seqA.Reserve();
        await a.SaveChangesAsync();
        seqB.Reserve();

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => b.SaveChangesAsync());
    }

    private static void At(Guid? tenantId)
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
    }

    [Fact]
    public async Task Reserve_ShouldKeepTenantsIsolated()
    {
        // El tenant es ambiente por request (un contexto = un tenant); aqui se alterna explicitamente.
        await using var db = NewContext(_tenantId);
        var service = new EfNumberingService(db);

        At(_tenantId);
        var a1 = await service.ReserveAsync(_tenantId, SifenEnvironmentType.Test, "01", Today);
        var a2 = await service.ReserveAsync(_tenantId, SifenEnvironmentType.Test, "01", Today);

        At(_otherTenantId);
        await using var dbB = NewContext(_otherTenantId);
        var b1 = await new EfNumberingService(dbB).ReserveAsync(_otherTenantId, SifenEnvironmentType.Test, "01", Today);

        Assert.Equal([1, 2], [a1.Number, a2.Number]);
        Assert.Equal(1, b1.Number);
        Assert.NotEqual(a1.SequenceId, b1.SequenceId);

        // Con el contexto del tenant B no se puede consumir la numeracion del tenant A aunque se pida su id.
        await Assert.ThrowsAsync<UserFacingException>(() =>
            new EfNumberingService(dbB).ReserveAsync(_tenantId, SifenEnvironmentType.Test, "01", Today));

        // Sin tenant resuelto (fail-closed) no se ve ninguna secuencia.
        At(null);
        await using var anonymous = NewContext(null);
        await Assert.ThrowsAsync<UserFacingException>(() =>
            new EfNumberingService(anonymous).ReserveAsync(_tenantId, SifenEnvironmentType.Test, "01", Today));
    }

    [Fact]
    public async Task Reserve_ShouldFail_WhenStampIsNotValidForTheDate()
    {
        await using var db = NewContext(_tenantId);
        var ex = await Assert.ThrowsAsync<UserFacingException>(() =>
            new EfNumberingService(db).ReserveAsync(_tenantId, SifenEnvironmentType.Test, "01", new DateOnly(2019, 12, 31)));

        Assert.Equal("FISCAL_NUMBERING_NOT_CONFIGURED", ex.ErrorCode);
    }

    [Fact]
    public async Task Reserve_ShouldFail_WhenSequenceIsExhausted()
    {
        var tenant = Tenant.CreateSharedDatabaseTenant("exhausted", "exhausted");
        await using (var seed = NewContext(tenant.Id))
        {
            seed.Tenants.Add(tenant);
            TestFiscalSetup.Seed(seed, tenant.Id, firstNumber: NumberingSequence.MaxDocumentNumber);
            await seed.SaveChangesAsync();
        }

        await using var db = NewContext(tenant.Id);
        var service = new EfNumberingService(db);
        var last = await service.ReserveAsync(tenant.Id, SifenEnvironmentType.Test, "01", Today);

        Assert.Equal(9_999_999, last.Number);
        await Assert.ThrowsAsync<DomainException>(() => service.ReserveAsync(tenant.Id, SifenEnvironmentType.Test, "01", Today));
    }

    [Fact]
    public async Task IdempotencyRecord_ShouldBeUniquePerTenantAndKey()
    {
        await using var db = NewContext(_tenantId);
        db.IdempotencyRecords.Add(SifenInvoicing.Domain.Documents.IdempotencyRecord.Create(_tenantId, "key-1", "hash", Guid.NewGuid()));
        await db.SaveChangesAsync();

        db.IdempotencyRecords.Add(SifenInvoicing.Domain.Documents.IdempotencyRecord.Create(_tenantId, "key-1", "hash", Guid.NewGuid()));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

        // El mismo key en otro tenant es independiente.
        await using var other = NewContext(_otherTenantId);
        other.IdempotencyRecords.Add(SifenInvoicing.Domain.Documents.IdempotencyRecord.Create(_otherTenantId, "key-1", "hash", Guid.NewGuid()));
        await other.SaveChangesAsync();
    }

    [Fact]
    public async Task NumberingSequence_ShouldNotAllowTwoSequencesWithTheSameKeyAndNoSeries()
    {
        await using var db = NewContext(_tenantId);
        var stamp = await db.FiscalStamps.SingleAsync();
        db.NumberingSequences.Add(NumberingSequence.Create(_tenantId, SifenEnvironmentType.Test, stamp.Id, "01", "001", "001"));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
