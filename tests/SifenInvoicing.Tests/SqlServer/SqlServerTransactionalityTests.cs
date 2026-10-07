using Microsoft.EntityFrameworkCore;
using SifenInvoicing.Application.Invoices;
using SifenInvoicing.Domain.Common;

namespace SifenInvoicing.Tests.SqlServer;

/// <summary>Atomicidad numero + documento + idempotencia: el estado real en SQL Server tras cada fallo.</summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", SqlServerTestEnvironment.TraitCategory)]
public sealed class SqlServerTransactionalityTests(SqlServerFixture fixture)
{
    private async Task AssertNothingConsumedAsync(Guid tenantId)
    {
        var state = await fixture.ReadStateAsync(tenantId);
        Assert.Equal(1, state.NextNumber);
        Assert.Equal(0, state.Documents);
        Assert.Equal(0, state.Lines);
        Assert.Equal(0, state.IdempotencyRecords);
        Assert.Equal(0, state.Events);
        Assert.Equal(0, state.Logs);
    }

    [SqlServerFact]
    public async Task ValidationError_BeforeReserving_ShouldNotConsumeNumber()
    {
        var tenantId = await fixture.SeedTenantAsync();
        await using var scope = fixture.NewInvoiceScope(tenantId);

        await Assert.ThrowsAsync<DomainException>(() => scope.Service.CreateAsync(SqlServerFixture.Command("  ")));
        await Assert.ThrowsAsync<DomainException>(() =>
            scope.Service.CreateAsync(SqlServerFixture.Command("k-neg", -5m)));

        await AssertNothingConsumedAsync(tenantId);
    }

    [SqlServerFact]
    public async Task FiscalCalculationError_ShouldNotConsumeNumber()
    {
        var tenantId = await fixture.SeedTenantAsync();
        await using var scope = fixture.NewInvoiceScope(tenantId);
        var usd = SqlServerFixture.Command("k-usd") with { Currency = InvoiceCurrency.USD };

        await Assert.ThrowsAsync<DomainException>(() => scope.Service.CreateAsync(usd));

        await AssertNothingConsumedAsync(tenantId);
    }

    [SqlServerFact]
    public async Task IncompleteFiscalProfile_ShouldFailBeforeReserving()
    {
        var tenantId = await fixture.SeedTenantAsync(taxpayer: false);
        await using (var db = fixture.NewContext(tenantId))
        {
            db.TaxpayerProfiles.Add(SifenInvoicing.Domain.Tenants.TaxpayerProfile.Create(tenantId, "80012345", "6", "ACME Paraguay SA"));
            await db.SaveChangesAsync(); // sin tipo de contribuyente ni direccion
        }

        await using var scope = fixture.NewInvoiceScope(tenantId);
        var ex = await Assert.ThrowsAsync<UserFacingException>(() => scope.Service.CreateAsync(SqlServerFixture.Command("k-1")));

        Assert.Equal("FISCAL_CONFIGURATION_INCOMPLETE", ex.ErrorCode);
        await AssertNothingConsumedAsync(tenantId);
    }

    [SqlServerFact]
    public async Task NumberingNotConfigured_ShouldFailWithoutPersistingAnything()
    {
        var tenantId = await fixture.SeedTenantAsync(numbering: false);
        await using var scope = fixture.NewInvoiceScope(tenantId);

        var ex = await Assert.ThrowsAsync<UserFacingException>(() => scope.Service.CreateAsync(SqlServerFixture.Command("k-1")));

        Assert.Equal("FISCAL_NUMBERING_NOT_CONFIGURED", ex.ErrorCode);
        await using var db = fixture.NewContext(tenantId);
        Assert.Empty(await db.Documents.ToListAsync());
        Assert.Empty(await db.IdempotencyRecords.ToListAsync());
    }

    [SqlServerFact]
    public async Task FailureInsideTransaction_AfterReservingNumber_ShouldRollBackEverything()
    {
        var tenantId = await fixture.SeedTenantAsync();

        // El numero ya se reservo y se guardo (UPDATE de la secuencia) cuando falla el INSERT del documento.
        await using (var failing = fixture.NewInvoiceScope(tenantId, null, new FailOnDocumentInsertInterceptor()))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => failing.Service.CreateAsync(SqlServerFixture.Command("k-boom")));
        }

        await AssertNothingConsumedAsync(tenantId);

        // El numero 1 sigue disponible y la misma Idempotency-Key puede reintentarse sin 'IDEMPOTENCY_KEY_REUSED'.
        await using var healthy = fixture.NewInvoiceScope(tenantId);
        var created = await healthy.Service.CreateAsync(SqlServerFixture.Command("k-boom"));

        var state = await fixture.ReadStateAsync(tenantId);
        Assert.Equal(["0000001"], state.Numbers);
        Assert.Equal(2, state.NextNumber);
        Assert.Equal(1, state.IdempotencyRecords);
        Assert.NotEqual(Guid.Empty, created.Id);
    }
}
