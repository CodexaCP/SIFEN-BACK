using SifenInvoicing.Application.Fiscal;
using SifenInvoicing.Application.Invoices;
using SifenInvoicing.Domain.Common;

namespace SifenInvoicing.Tests;

public sealed class FiscalCalculationEngineTests
{
    [Fact]
    public void Calculate_TwoItemsAtTenPercent_ShouldFollowManualFormulas()
    {
        // Manual v150 E735 = EA008*(E733/100)/1,1 ; E736 = E735*(E734/100); F016 = suma E736; F019 = suma E735.
        // Coherente con el ejemplo del QR del Manual (total 300000, IVA 27272, 2 items); los items son supuestos.
        var model = FiscalCalculationEngine.Calculate(
            [new(1, 150000m, InvoiceVatType.Vat10), new(1, 150000m, InvoiceVatType.Vat10)], "PYG");

        Assert.All(model.Lines, l =>
        {
            Assert.Equal(150000m, l.TotalOperacion);
            Assert.Equal(1, l.AfectacionIva);
            Assert.Equal(100m, l.ProporcionIva);
            Assert.Equal(10m, l.TasaIva);
            Assert.Equal(136364m, l.BaseGravadaIva);
            Assert.Equal(13636m, l.LiquidacionIva);
            Assert.Equal(0m, l.BaseExenta);
        });
        Assert.Equal(300000m, model.Totals.Sub10);
        Assert.Equal(300000m, model.Totals.TotalOperacion);
        Assert.Equal(300000m, model.Totals.TotalGeneral);
        Assert.Equal(27272m, model.Totals.Iva10);
        Assert.Equal(27272m, model.Totals.TotalIva);
        Assert.Equal(272728m, model.Totals.BaseGravada10);
        Assert.Equal(272728m, model.Totals.TotalBaseGravada);
        Assert.Equal(0m, model.Totals.TotalDescuento);
        Assert.Equal(0m, model.Totals.Redondeo);
    }

    [Fact]
    public void Calculate_ShouldComputeQuantityTimesPrice_AndMixedRates()
    {
        var model = FiscalCalculationEngine.Calculate(
            [new(3m, 11000m, InvoiceVatType.Vat10), new(2m, 10500m, InvoiceVatType.Vat5), new(1m, 5000m, InvoiceVatType.Exempt)],
            "PYG");

        Assert.Equal(33000m, model.Lines[0].TotalBruto);
        Assert.Equal(30000m, model.Lines[0].BaseGravadaIva);
        Assert.Equal(3000m, model.Lines[0].LiquidacionIva);
        Assert.Equal(20000m, model.Lines[1].BaseGravadaIva);
        Assert.Equal(1000m, model.Lines[1].LiquidacionIva);
        Assert.Equal(3, model.Lines[2].AfectacionIva);
        Assert.Equal(5000m, model.Totals.SubExento);
        Assert.Equal(59000m, model.Totals.TotalGeneral);
        Assert.Equal(4000m, model.Totals.TotalIva);
    }

    [Fact]
    public void Calculate_TotalsMustEqualSumOfItems()
    {
        var rnd = new Random(7);
        for (var n = 0; n < 200; n++)
        {
            var lines = Enumerable.Range(0, rnd.Next(1, 8))
                .Select(_ => new FiscalLineInput(rnd.Next(1, 20), rnd.Next(1, 500000), (InvoiceVatType)rnd.Next(1, 4)))
                .ToList();
            var m = FiscalCalculationEngine.Calculate(lines, "PYG");

            Assert.Equal(m.Lines.Sum(l => l.TotalOperacion), m.Totals.TotalGeneral);
            Assert.Equal(m.Lines.Sum(l => l.LiquidacionIva), m.Totals.TotalIva);
            Assert.Equal(m.Totals.SubExento + m.Totals.Sub5 + m.Totals.Sub10, m.Totals.TotalOperacion);
        }
    }

    [Fact]
    public void Calculate_ShouldRejectUnsupportedInput()
    {
        FiscalLineInput[] ok = [new(1m, 100m, InvoiceVatType.Vat10)];
        Assert.Throws<DomainException>(() => FiscalCalculationEngine.Calculate(ok, "USD"));
        Assert.Throws<DomainException>(() => FiscalCalculationEngine.Calculate([], "PYG"));
        Assert.Throws<DomainException>(() => FiscalCalculationEngine.Calculate([new(0m, 100m, InvoiceVatType.Vat10)], "PYG"));
        Assert.Throws<DomainException>(() => FiscalCalculationEngine.Calculate([new(1m, -1m, InvoiceVatType.Vat10)], "PYG"));
    }
}
