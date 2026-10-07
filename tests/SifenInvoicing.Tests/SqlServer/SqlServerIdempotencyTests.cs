using Microsoft.EntityFrameworkCore;
using SifenInvoicing.Application.Invoices;
using SifenInvoicing.Domain.Common;
using Xunit.Abstractions;

namespace SifenInvoicing.Tests.SqlServer;

/// <summary>Emision completa (EfInvoiceService + EfNumberingService) e Idempotency-Key sobre SQL Server real.</summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", SqlServerTestEnvironment.TraitCategory)]
public sealed class SqlServerIdempotencyTests(SqlServerFixture fixture, ITestOutputHelper output)
{
    private static readonly Func<string, decimal, CreateInvoiceCommand> Cmd =
        (key, price) => SqlServerFixture.Command(key, price);

    private async Task<(CreateInvoiceResult? Result, Exception? Error)> TryCreateAsync(Guid tenantId, string key, decimal price, Task? gate)
    {
        try
        {
            await using var scope = fixture.NewInvoiceScope(tenantId);
            if (gate is not null)
            {
                await gate;
            }

            return (await scope.Service.CreateAsync(Cmd(key, price)), null);
        }
        catch (Exception ex)
        {
            return (null, ex);
        }
    }

    [SqlServerFact]
    public async Task SameKeyAndContent_ShouldReturnSameDocument_WithoutConsumingAnotherNumber()
    {
        var tenantId = await fixture.SeedTenantAsync();
        await using var scope = fixture.NewInvoiceScope(tenantId);

        var first = await scope.Service.CreateAsync(Cmd("same-key", 100000m));
        var second = await scope.Service.CreateAsync(Cmd("same-key", 100000m));

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(first.Cdc, second.Cdc);
        var state = await fixture.ReadStateAsync(tenantId);
        Assert.Equal(1, state.Documents);
        Assert.Equal(1, state.IdempotencyRecords);
        Assert.Equal(2, state.NextNumber);
    }

    [SqlServerFact]
    public async Task SameKeyDifferentContent_ShouldFailWithIdempotencyKeyReused_WithoutConsumingNumber()
    {
        var tenantId = await fixture.SeedTenantAsync();
        await using var scope = fixture.NewInvoiceScope(tenantId);
        await scope.Service.CreateAsync(Cmd("reused", 100000m));

        var ex = await Assert.ThrowsAsync<UserFacingException>(() => scope.Service.CreateAsync(Cmd("reused", 999m)));

        Assert.Equal("IDEMPOTENCY_KEY_REUSED", ex.ErrorCode);
        var state = await fixture.ReadStateAsync(tenantId);
        Assert.Equal(1, state.Documents);
        Assert.Equal(1, state.IdempotencyRecords);
        Assert.Equal(2, state.NextNumber); // sin segunda numeracion
    }

    [SqlServerFact]
    public async Task SimultaneousRequestsWithSameKey_ShouldCreateExactlyOneDocumentAndOneRecord()
    {
        const int parallel = 10;
        var tenantId = await fixture.SeedTenantAsync();
        var start = new TaskCompletionSource();

        var running = Task.WhenAll(Enumerable.Range(0, parallel).Select(_ =>
            Task.Run(() => TryCreateAsync(tenantId, "concurrent-key", 100000m, start.Task))));
        start.SetResult();
        var results = await running;

        var errors = results.Where(r => r.Error is not null).Select(r => $"{r.Error!.GetType().Name}: {r.Error.Message}").ToList();
        output.WriteLine($"Errores: {errors.Count}; ids distintos: {results.Where(r => r.Result is not null).Select(r => r.Result!.Id).Distinct().Count()}");
        Assert.True(errors.Count == 0, "Todas las solicitudes con la misma key deben resolver al mismo documento: " + string.Join(" | ", errors));
        Assert.Single(results.Select(r => r.Result!.Id).Distinct());

        var state = await fixture.ReadStateAsync(tenantId);
        Assert.Equal(1, state.Documents);
        Assert.Equal(1, state.IdempotencyRecords);
        Assert.Equal(1, state.Lines);
        Assert.Equal(["0000001"], state.Numbers);
        Assert.Equal(2, state.NextNumber);
    }

    [SqlServerFact]
    public async Task SimultaneousRequestsWithSameKeyAndDifferentContent_ShouldAllowOnlyOneWinner()
    {
        const int parallel = 6;
        var tenantId = await fixture.SeedTenantAsync();
        var start = new TaskCompletionSource();

        var running = Task.WhenAll(Enumerable.Range(0, parallel).Select(i =>
            Task.Run(() => TryCreateAsync(tenantId, "mixed-key", 1000m * (i + 1), start.Task))));
        start.SetResult();
        var results = await running;

        var winners = results.Where(r => r.Result is not null).ToList();
        var reused = results.Where(r => r.Error is UserFacingException { ErrorCode: "IDEMPOTENCY_KEY_REUSED" }).ToList();
        Assert.Single(winners);
        Assert.Equal(parallel - 1, reused.Count);

        var state = await fixture.ReadStateAsync(tenantId);
        Assert.Equal(1, state.Documents);
        Assert.Equal(1, state.IdempotencyRecords);
        Assert.Equal(2, state.NextNumber);
    }

    /// <summary>
    /// 50 emisiones simultaneas del mismo tenant/punto. La reserva usa UPDATE ... OUTPUT atomico: se exigen cero
    /// FISCAL_NUMBERING_BUSY, numeros unicos consecutivos 1..50 y ningun otro error.
    /// </summary>
    [SqlServerFact]
    public async Task FullPipeline_50ConcurrentRequestsWithDistinctKeys_ShouldAssignUniqueConsecutiveNumbers()
    {
        const int parallel = 50;
        var tenantId = await fixture.SeedTenantAsync();
        var start = new TaskCompletionSource();
        var busyResponses = 0;

        async Task<(CreateInvoiceResult? Result, Exception? Error)> Emit(int i)
        {
            for (var attempt = 0; attempt < 10; attempt++)
            {
                var outcome = await TryCreateAsync(tenantId, $"key-{i}", 1000m + i, attempt == 0 ? start.Task : null);
                if (outcome.Error is UserFacingException { ErrorCode: "FISCAL_NUMBERING_BUSY" })
                {
                    Interlocked.Increment(ref busyResponses);
                    continue; // contrato: reintentar con la misma Idempotency-Key
                }

                return outcome;
            }

            return (null, new InvalidOperationException("FISCAL_NUMBERING_BUSY persistente tras 10 reintentos."));
        }

        var running = Task.WhenAll(Enumerable.Range(0, parallel).Select(i => Task.Run(() => Emit(i))));
        start.SetResult();
        var results = await running;

        output.WriteLine($"Respuestas FISCAL_NUMBERING_BUSY (503 reintentables) antes de lograr la emision: {Volatile.Read(ref busyResponses)}");
        Assert.Equal(0, Volatile.Read(ref busyResponses));
        var errors = results.Where(r => r.Error is not null).Select(r => $"{r.Error!.GetType().Name}: {r.Error.Message}").ToList();
        Assert.True(errors.Count == 0, $"{errors.Count} errores: " + string.Join(" | ", errors.Distinct()));

        var state = await fixture.ReadStateAsync(tenantId);
        Assert.Equal(parallel, state.Documents);
        Assert.Equal(parallel, state.IdempotencyRecords);
        Assert.Equal(parallel, state.Lines);
        Assert.Equal(Enumerable.Range(1, parallel).Select(n => n.ToString("D7")), state.Numbers);
        Assert.Equal(parallel + 1, state.NextNumber);
        Assert.Equal(parallel, results.Select(r => r.Result!.Cdc).Distinct().Count());
    }
}
