using Microsoft.EntityFrameworkCore;
using SifenInvoicing.Domain.Common;

namespace SifenInvoicing.Tests.SqlServer;

/// <summary>Aislamiento multi-tenant (filtros globales, numeracion e Idempotency-Key por tenant) sobre SQL Server real.</summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", SqlServerTestEnvironment.TraitCategory)]
public sealed class SqlServerTenantIsolationTests(SqlServerFixture fixture)
{
    [SqlServerFact]
    public async Task SameIdempotencyKeyInTwoTenants_ShouldNotConflict_AndEachStartsItsOwnNumbering()
    {
        var a = await fixture.SeedTenantAsync("iso-a");
        var b = await fixture.SeedTenantAsync("iso-b");

        await using var scopeA = fixture.NewInvoiceScope(a);
        var ra = await scopeA.Service.CreateAsync(SqlServerFixture.Command("shared-key"));
        await using var scopeB = fixture.NewInvoiceScope(b);
        var rb = await scopeB.Service.CreateAsync(SqlServerFixture.Command("shared-key"));

        Assert.NotEqual(ra.Id, rb.Id);
        var stateA = await fixture.ReadStateAsync(a);
        var stateB = await fixture.ReadStateAsync(b);
        Assert.Equal(["0000001"], stateA.Numbers);
        Assert.Equal(["0000001"], stateB.Numbers);
        Assert.Equal(2, stateA.NextNumber);
        Assert.Equal(2, stateB.NextNumber);
        Assert.Equal(1, stateA.IdempotencyRecords);
        Assert.Equal(1, stateB.IdempotencyRecords);
    }

    [SqlServerFact]
    public async Task ConcurrentEmissionsInTwoTenants_ShouldNotCrossNumberingOrDocuments()
    {
        const int perTenant = 15;
        var a = await fixture.SeedTenantAsync("iso-a");
        var b = await fixture.SeedTenantAsync("iso-b");
        var start = new TaskCompletionSource();

        async Task<Exception?> Emit(Guid tenant, int i)
        {
            try
            {
                await using var scope = fixture.NewInvoiceScope(tenant);
                await start.Task;
                await scope.Service.CreateAsync(SqlServerFixture.Command($"k-{i}", 1000m + i)); // mismas keys en ambos tenants
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        }

        var running = Task.WhenAll(
            Enumerable.Range(0, perTenant).Select(i => Task.Run(() => Emit(a, i)))
                .Concat(Enumerable.Range(0, perTenant).Select(i => Task.Run(() => Emit(b, i)))));
        start.SetResult();
        var errors = (await running).Where(e => e is not null).Select(e => e!.Message).ToList();

        Assert.True(errors.Count == 0, string.Join(" | ", errors.Distinct()));
        var expected = Enumerable.Range(1, perTenant).Select(n => n.ToString("D7")).ToList();
        foreach (var tenant in new[] { a, b })
        {
            var state = await fixture.ReadStateAsync(tenant);
            Assert.Equal(expected, state.Numbers);
            Assert.Equal(perTenant, state.IdempotencyRecords);
            Assert.Equal(perTenant + 1, state.NextNumber);
        }
    }

    [SqlServerFact]
    public async Task GlobalFilters_ShouldHideOtherTenantsData_AndFailClosedWithoutTenant()
    {
        var a = await fixture.SeedTenantAsync("flt-a");
        var b = await fixture.SeedTenantAsync("flt-b");
        await using (var scopeA = fixture.NewInvoiceScope(a))
        {
            await scopeA.Service.CreateAsync(SqlServerFixture.Command("fa"));
        }

        Guid documentOfB;
        await using (var scopeB = fixture.NewInvoiceScope(b))
        {
            documentOfB = (await scopeB.Service.CreateAsync(SqlServerFixture.Command("fb"))).Id;
        }

        // Como tenant A: solo ve lo suyo.
        await using (var dbA = fixture.NewContext(a))
        {
            Assert.Single(await dbA.Documents.ToListAsync());
            Assert.All(await dbA.Documents.ToListAsync(), d => Assert.Equal(a, d.TenantId));
            Assert.Single(await dbA.NumberingSequences.ToListAsync());
            Assert.Single(await dbA.IdempotencyRecords.ToListAsync());
            Assert.Single(await dbA.FiscalStamps.ToListAsync());
            Assert.Empty(await dbA.Documents.Where(d => d.Id == documentOfB).ToListAsync());
            Assert.Empty(await dbA.DocumentLines.Where(l => l.DocumentId == documentOfB).ToListAsync());
        }

        // Sin tenant: no ve nada (fail-closed); ignorando los filtros los datos existen.
        await using (var anonymous = fixture.NewContext(null))
        {
            Assert.Empty(await anonymous.Documents.ToListAsync());
            Assert.Empty(await anonymous.NumberingSequences.ToListAsync());
            Assert.Empty(await anonymous.IdempotencyRecords.ToListAsync());
            Assert.Empty(await anonymous.DocumentLines.ToListAsync());
            Assert.Empty(await anonymous.FiscalStamps.ToListAsync());
            Assert.Equal(2, await anonymous.Documents.IgnoreQueryFilters().CountAsync(d => d.TenantId == a || d.TenantId == b));
        }
    }

    [SqlServerFact]
    public async Task Emission_WithoutTenantContext_ShouldFailClosedAndConsumeNothing()
    {
        var tenantId = await fixture.SeedTenantAsync();
        // Servicio creado sobre un contexto SIN tenant: debe fallar cerrado.
        await using var anonymousDb = fixture.NewContext(null);
        var service = new SifenInvoicing.Infrastructure.Invoices.EfInvoiceService(
            anonymousDb, new SifenInvoicing.Infrastructure.Tenancy.AsyncLocalTenantContextAccessor(),
            DeTestKit.Builder(), DeTestKit.Xsd(),
            new SifenInvoicing.Infrastructure.Invoices.InvoiceKudePdfRenderer(), new ReadyCertificateValidator(), new FakeSigner(),
            new StubReadiness(), new CountingGateway(), new FakeParser(), SqlServerFakes.Configuration(), new NullAudit(),
            new SifenInvoicing.Infrastructure.Diagnostics.SystemClock(),
            new SifenInvoicing.Infrastructure.Numbering.EfNumberingService(anonymousDb), new TestFiscalClock());

        await Assert.ThrowsAsync<DomainException>(() => service.CreateAsync(SqlServerFixture.Command("no-tenant")));

        var state = await fixture.ReadStateAsync(tenantId);
        Assert.Equal(1, state.NextNumber);
        Assert.Equal(0, state.Documents);
    }
}
