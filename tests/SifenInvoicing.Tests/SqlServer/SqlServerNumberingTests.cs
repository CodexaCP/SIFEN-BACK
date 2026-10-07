using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SifenInvoicing.Application.Numbering;
using SifenInvoicing.Domain.Common;
using SifenInvoicing.Domain.Documents;
using SifenInvoicing.Domain.Tenants;
using SifenInvoicing.Infrastructure.Numbering;
using Xunit.Abstractions;

namespace SifenInvoicing.Tests.SqlServer;

/// <summary>
/// Numeracion sobre SQL Server real. Diferencia clave con SQLite: SQL Server bloquea a nivel de fila (el segundo UPDATE
/// espera al primero y luego falla por el token de concurrencia) en lugar de rechazar con SQLITE_BUSY; ademas hay
/// transacciones con aislamiento READ COMMITTED y los errores de indice unico llegan como SqlException 2601/2627.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", SqlServerTestEnvironment.TraitCategory)]
public sealed class SqlServerNumberingTests(SqlServerFixture fixture, ITestOutputHelper output)
{
    private static readonly DateOnly Today = new(2026, 4, 25);

    private async Task<ReservedNumber> ReserveInOwnTransactionAsync(Guid tenantId, ConcurrencyFailureCounter? counter, Task? gate)
    {
        // Cada tarea simula un request: su propio DbContext, su propia conexion y su propia transaccion.
        await using var db = counter is null ? fixture.NewContext(tenantId) : fixture.NewContext(tenantId, counter);
        if (gate is not null)
        {
            await gate;
        }

        await using var tx = await db.Database.BeginTransactionAsync();
        var reserved = await new EfNumberingService(db).ReserveAsync(tenantId, SifenEnvironmentType.Test, "01", Today);
        await tx.CommitAsync();
        return reserved;
    }

    private static async Task<(long[] Numbers, List<Exception> Errors)> RunAsync(IEnumerable<Func<Task<ReservedNumber>>> work)
    {
        var results = await Task.WhenAll(work.Select(async job =>
        {
            try
            {
                return (Number: (long?)(await job()).Number, Error: (Exception?)null);
            }
            catch (Exception ex)
            {
                return (Number: (long?)null, Error: (Exception?)ex);
            }
        }));

        return (results.Where(r => r.Number.HasValue).Select(r => r.Number!.Value).ToArray(),
            results.Where(r => r.Error is not null).Select(r => r.Error!).ToList());
    }

    /// <summary>
    /// 50 reservas simultaneas sobre la misma secuencia, cada una con su propio DbContext/conexion/transaccion.
    /// HALLAZGO: bajo esta carga el servicio sufre cientos de DbUpdateConcurrencyException (todos los lectores bloqueados
    /// leen la misma version cuando se libera el bloqueo y solo uno gana por ronda) y a veces una reserva agota sus 25
    /// intentos y responde FISCAL_NUMBERING_BUSY (503, reintentable). La prueba admite SOLO ese error, reintenta la
    /// reserva como lo haria el cliente y exige igualmente 50 numeros unicos, consecutivos 1..50 y NextNumber == 51.
    /// Cualquier otro error o un duplicado/hueco falla la prueba. Los conteos se imprimen en la salida.
    /// </summary>
    [SqlServerFact]
    public async Task Reserve_50ConcurrentRequests_ShouldYieldUniqueConsecutiveNumbers()
    {
        const int parallel = 50;
        var tenantId = await fixture.SeedTenantAsync();
        var counter = new ConcurrencyFailureCounter();
        var start = new TaskCompletionSource();
        var busyResponses = 0;

        async Task<ReservedNumber> Job()
        {
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    return await ReserveInOwnTransactionAsync(tenantId, counter, attempt == 0 ? start.Task : null);
                }
                catch (UserFacingException ex) when (ex.ErrorCode == "FISCAL_NUMBERING_BUSY" && attempt < 10)
                {
                    Interlocked.Increment(ref busyResponses);
                }
            }
        }

        var running = RunAsync(Enumerable.Range(0, parallel).Select(_ => (Func<Task<ReservedNumber>>)(() => Task.Run(Job))).ToList());
        start.SetResult(); // todas las tareas parten a la vez
        var (numbers, errors) = await running;

        output.WriteLine($"DbUpdateConcurrencyException detectados por EF (reintentos optimistas): {counter.Count}");
        output.WriteLine($"Reservas que agotaron 25 intentos (FISCAL_NUMBERING_BUSY, reintentadas por el cliente): {Volatile.Read(ref busyResponses)}");
        Assert.True(errors.Count == 0, "Errores inesperados: " + string.Join(" | ", errors.Select(e => $"{e.GetType().Name}: {e.Message}")));
        Assert.Equal(parallel, numbers.Distinct().Count());
        Assert.Equal(Enumerable.Range(1, parallel).Select(n => (long)n), numbers.OrderBy(n => n));

        var state = await fixture.ReadStateAsync(tenantId);
        Assert.Equal(parallel + 1, state.NextNumber);
    }

    [SqlServerFact]
    public async Task Reserve_ShouldReleaseNumber_WhenTransactionRollsBack()
    {
        var tenantId = await fixture.SeedTenantAsync();

        await using (var db = fixture.NewContext(tenantId))
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            var reserved = await new EfNumberingService(db).ReserveAsync(tenantId, SifenEnvironmentType.Test, "01", Today);
            Assert.Equal(1, reserved.Number);
            await tx.RollbackAsync();
        }

        // Estado real: el contador no avanzo.
        Assert.Equal(1, (await fixture.ReadStateAsync(tenantId)).NextNumber);

        await using var db2 = fixture.NewContext(tenantId);
        var next = await new EfNumberingService(db2).ReserveAsync(tenantId, SifenEnvironmentType.Test, "01", Today);
        Assert.Equal(1, next.Number);
        Assert.Equal("0000001", next.FormattedNumber);
    }

    [SqlServerFact]
    public async Task Reserve_ShouldBlockSecondTransaction_UntilFirstRollsBack_ThenReuseTheNumber()
    {
        var tenantId = await fixture.SeedTenantAsync();

        await using var first = fixture.NewContext(tenantId);
        await using var tx1 = await first.Database.BeginTransactionAsync();
        var held = await new EfNumberingService(first).ReserveAsync(tenantId, SifenEnvironmentType.Test, "01", Today);
        Assert.Equal(1, held.Number);

        // La segunda transaccion debe quedar BLOQUEADA por el bloqueo de fila de SQL Server (READ COMMITTED).
        var second = Task.Run(async () =>
        {
            await using var db = fixture.NewContext(tenantId);
            await using var tx = await db.Database.BeginTransactionAsync();
            var reserved = await new EfNumberingService(db).ReserveAsync(tenantId, SifenEnvironmentType.Test, "01", Today);
            await tx.CommitAsync();
            return reserved.Number;
        });

        var finishedEarly = await Task.WhenAny(second, Task.Delay(TimeSpan.FromSeconds(1))) == second;
        Assert.False(finishedEarly, "La segunda reserva debio esperar mientras la primera transaccion sigue abierta.");

        await tx1.RollbackAsync();
        Assert.Equal(1, await second.WaitAsync(TimeSpan.FromSeconds(30)));
        Assert.Equal(2, (await fixture.ReadStateAsync(tenantId)).NextNumber);
    }

    [SqlServerFact]
    public async Task Reserve_ShouldGiveNextNumberToSecondTransaction_WhenFirstCommits()
    {
        var tenantId = await fixture.SeedTenantAsync();

        await using var first = fixture.NewContext(tenantId);
        await using var tx1 = await first.Database.BeginTransactionAsync();
        var held = await new EfNumberingService(first).ReserveAsync(tenantId, SifenEnvironmentType.Test, "01", Today);

        var second = Task.Run(async () =>
        {
            await using var db = fixture.NewContext(tenantId);
            await using var tx = await db.Database.BeginTransactionAsync();
            var reserved = await new EfNumberingService(db).ReserveAsync(tenantId, SifenEnvironmentType.Test, "01", Today);
            await tx.CommitAsync();
            return reserved.Number;
        });

        await Task.Delay(500);
        await tx1.CommitAsync();

        Assert.Equal(1, held.Number);
        Assert.Equal(2, await second.WaitAsync(TimeSpan.FromSeconds(30)));
        Assert.Equal(3, (await fixture.ReadStateAsync(tenantId)).NextNumber);
    }

    [SqlServerFact]
    public async Task Reserve_ShouldRetry_WhenAnotherTransactionCommitsBetweenReadAndUpdate()
    {
        var tenantId = await fixture.SeedTenantAsync();
        var counter = new ConcurrencyFailureCounter();
        var pause = new PauseBeforeFirstSaveInterceptor();

        // A lee la secuencia (version 1) y se detiene justo antes de su UPDATE.
        var a = Task.Run(async () =>
        {
            await using var db = fixture.NewContext(tenantId, pause, counter);
            await using var tx = await db.Database.BeginTransactionAsync();
            var reserved = await new EfNumberingService(db).ReserveAsync(tenantId, SifenEnvironmentType.Test, "01", Today);
            await tx.CommitAsync();
            return reserved.Number;
        });
        await pause.Reached.WaitAsync(TimeSpan.FromSeconds(30));

        // B reserva y confirma en esa ventana: obtiene el numero 1.
        var b = await ReserveInOwnTransactionAsync(tenantId, null, null);
        Assert.Equal(1, b.Number);

        // A reanuda: su UPDATE usa la version vieja => DbUpdateConcurrencyException => recarga, reintenta y obtiene el 2.
        pause.Release();
        Assert.Equal(2, await a.WaitAsync(TimeSpan.FromSeconds(30)));
        Assert.Equal(1, counter.Count);
        Assert.Equal(3, (await fixture.ReadStateAsync(tenantId)).NextNumber);
    }

    [SqlServerFact]
    public async Task ConcurrencyToken_ShouldRejectStaleUpdate_OnSqlServer()
    {
        var tenantId = await fixture.SeedTenantAsync();
        await using var a = fixture.NewContext(tenantId);
        await using var b = fixture.NewContext(tenantId);
        var seqA = await a.NumberingSequences.SingleAsync();
        var seqB = await b.NumberingSequences.SingleAsync();

        seqA.Reserve();
        await a.SaveChangesAsync();
        seqB.Reserve();

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => b.SaveChangesAsync());
        Assert.Equal(2, (await fixture.ReadStateAsync(tenantId)).NextNumber);
    }

    [SqlServerFact]
    public async Task Reserve_TwoTenantsConcurrently_ShouldKeepIndependentSequences()
    {
        const int perTenant = 20;
        var tenantA = await fixture.SeedTenantAsync("tenant-a");
        var tenantB = await fixture.SeedTenantAsync("tenant-b");
        var start = new TaskCompletionSource();

        Func<Task<ReservedNumber>> Job(Guid tenant) => () => Task.Run(() => ReserveInOwnTransactionAsync(tenant, null, start.Task));
        var running = Task.WhenAll(
            RunAsync(Enumerable.Range(0, perTenant).Select(_ => Job(tenantA))),
            RunAsync(Enumerable.Range(0, perTenant).Select(_ => Job(tenantB))));
        start.SetResult();
        var results = await running;

        foreach (var (numbers, errors) in results)
        {
            Assert.True(errors.Count == 0, string.Join(" | ", errors.Select(e => e.Message)));
            Assert.Equal(Enumerable.Range(1, perTenant).Select(n => (long)n), numbers.OrderBy(n => n));
        }

        Assert.Equal(perTenant + 1, (await fixture.ReadStateAsync(tenantA)).NextNumber);
        Assert.Equal(perTenant + 1, (await fixture.ReadStateAsync(tenantB)).NextNumber);
    }

    [SqlServerFact]
    public async Task UniqueIndexes_ShouldRaiseSqlServerDuplicateKeyErrors()
    {
        var tenantId = await fixture.SeedTenantAsync();

        // Idempotencia: unica por (TenantId, Key).
        await using var db = fixture.NewContext(tenantId);
        db.IdempotencyRecords.Add(IdempotencyRecord.Create(tenantId, "key-1", "hash", Guid.NewGuid()));
        await db.SaveChangesAsync();
        db.IdempotencyRecords.Add(IdempotencyRecord.Create(tenantId, "key-1", "hash", Guid.NewGuid()));
        var duplicateKey = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.IsType<SqlException>(duplicateKey.InnerException);
        Assert.Contains(((SqlException)duplicateKey.InnerException!).Number, new[] { 2601, 2627 });

        // Numeracion: una sola secuencia por (tenant, ambiente, tipo, establecimiento, punto, serie "").
        await using var db2 = fixture.NewContext(tenantId);
        var stamp = await db2.FiscalStamps.SingleAsync();
        db2.NumberingSequences.Add(NumberingSequence.Create(tenantId, SifenEnvironmentType.Test, stamp.Id, "01", "001", "001"));
        var duplicateSequence = await Assert.ThrowsAsync<DbUpdateException>(() => db2.SaveChangesAsync());
        Assert.Contains(((SqlException)duplicateSequence.InnerException!).Number, new[] { 2601, 2627 });

        // La misma key en otro tenant es independiente.
        var other = await fixture.SeedTenantAsync();
        await using var db3 = fixture.NewContext(other);
        db3.IdempotencyRecords.Add(IdempotencyRecord.Create(other, "key-1", "hash", Guid.NewGuid()));
        await db3.SaveChangesAsync();
    }

    [SqlServerFact]
    public async Task Reserve_ShouldFailClosed_WithoutTenantOrWithAnotherTenant()
    {
        var tenantId = await fixture.SeedTenantAsync();
        var other = await fixture.SeedTenantAsync();

        await using (var asOther = fixture.NewContext(other))
        {
            var ex = await Assert.ThrowsAsync<UserFacingException>(() =>
                new EfNumberingService(asOther).ReserveAsync(tenantId, SifenEnvironmentType.Test, "01", Today));
            Assert.Equal("FISCAL_NUMBERING_NOT_CONFIGURED", ex.ErrorCode);
        }

        await using (var anonymous = fixture.NewContext(null))
        {
            await Assert.ThrowsAsync<UserFacingException>(() =>
                new EfNumberingService(anonymous).ReserveAsync(tenantId, SifenEnvironmentType.Test, "01", Today));
        }

        Assert.Equal(1, (await fixture.ReadStateAsync(tenantId)).NextNumber);
        Assert.Equal(1, (await fixture.ReadStateAsync(other)).NextNumber);
    }
}
