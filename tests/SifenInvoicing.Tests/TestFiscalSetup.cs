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

    public static TaxpayerProfile Taxpayer(Guid tenantId, string ruc = "80012345", string dv = "0", string name = "ACME Paraguay SA")
    {
        var taxpayer = TaxpayerProfile.Create(tenantId, ruc, dv, name);
        // Datos de ejemplo con la forma de la muestra del Manual (no de un contribuyente real).
        taxpayer.UpdateFiscalData(2, "CALLE 1 CASI CALLE 2", "0", "1", "CAPITAL", null, null, "1", "ASUNCION (DISTRITO)", "021123456", "correo@correo.com");
        return taxpayer;
    }

    /// <summary>gActEco de ejemplo (D130-D132): el codigo/descripcion reales los define la DNIT.</summary>
    public static TaxpayerEconomicActivity Activity(TaxpayerProfile taxpayer) =>
        TaxpayerEconomicActivity.Create(taxpayer.TenantId, taxpayer.Id, "46510", "COMERCIO AL POR MAYOR DE EQUIPOS INFORMATICOS Y SOFTWARE");

    public static TaxpayerProfile AddTaxpayer(SifenDbContext dbContext, Guid tenantId)
    {
        var taxpayer = Taxpayer(tenantId);
        dbContext.TaxpayerProfiles.Add(taxpayer);
        dbContext.TaxpayerEconomicActivities.Add(Activity(taxpayer));
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
            AddTaxpayer(dbContext, tenantId);
        }

        var stamp = FiscalStamp.Create(tenantId, environment, stampingNumber, new DateOnly(2020, 1, 1));
        var sequence = NumberingSequence.Create(tenantId, environment, stamp.Id, "01", establishment, point, null, firstNumber);
        dbContext.FiscalStamps.Add(stamp);
        dbContext.NumberingSequences.Add(sequence);
        return sequence;
    }
}
