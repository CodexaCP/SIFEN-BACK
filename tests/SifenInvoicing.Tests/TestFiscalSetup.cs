using SifenInvoicing.Application.Fiscal;
using SifenInvoicing.Domain.Tenants;
using SifenInvoicing.Infrastructure.Persistence;

namespace SifenInvoicing.Tests;

/// <summary>Reloj fiscal fijo para pruebas.</summary>
internal sealed class TestFiscalClock : IFiscalClock
{
    public DateTimeOffset Now => DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-3));

    public DateOnly Today => DateOnly.FromDateTime(Now.DateTime);
}

/// <summary>Configuracion fiscal minima de un tenant de prueba (datos de ejemplo, no de un contribuyente real).</summary>
internal static class TestFiscalSetup
{
    public const string StampingNumber = "12345678";

    public static TaxpayerProfile Taxpayer(Guid tenantId, string ruc = "80012345", string dv = "6", string name = "ACME Paraguay SA")
    {
        var taxpayer = TaxpayerProfile.Create(tenantId, ruc, dv, name);
        taxpayer.UpdateFiscalData(2, "Asuncion 123", null, null, null, null, null, null, null, null, null);
        return taxpayer;
    }

    public static NumberingSequence Seed(
        SifenDbContext dbContext,
        Guid tenantId,
        SifenEnvironmentType environment = SifenEnvironmentType.Test,
        string stampingNumber = StampingNumber,
        string establishment = "001",
        string point = "001",
        long firstNumber = 1,
        bool withTaxpayer = true)
    {
        if (withTaxpayer)
        {
            dbContext.TaxpayerProfiles.Add(Taxpayer(tenantId));
        }

        var stamp = FiscalStamp.Create(tenantId, environment, stampingNumber, new DateOnly(2020, 1, 1));
        var sequence = NumberingSequence.Create(tenantId, environment, stamp.Id, "01", establishment, point, null, firstNumber);
        dbContext.FiscalStamps.Add(stamp);
        dbContext.NumberingSequences.Add(sequence);
        return sequence;
    }
}
