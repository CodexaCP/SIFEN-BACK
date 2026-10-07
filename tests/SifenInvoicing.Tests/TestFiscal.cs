using SifenInvoicing.Application.Fiscal;
using SifenInvoicing.Application.Invoices;

namespace SifenInvoicing.Tests;

internal static class TestFiscal
{
    public static FiscalDocumentModel Model(params (string Description, decimal Quantity, decimal UnitPrice, InvoiceVatType Vat)[] lines) =>
        FiscalCalculationEngine.Calculate(
            lines.Select(line => new FiscalLineInput(line.Quantity, line.UnitPrice, line.Vat)).ToList(),
            "PYG");

    public static IReadOnlyList<string> Descriptions(params (string Description, decimal Quantity, decimal UnitPrice, InvoiceVatType Vat)[] lines) =>
        lines.Select(line => line.Description).ToList();
}
