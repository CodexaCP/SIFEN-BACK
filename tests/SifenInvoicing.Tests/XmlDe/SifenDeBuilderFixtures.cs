using SifenInvoicing.Application.Cdc;
using SifenInvoicing.Application.Fiscal;
using SifenInvoicing.Application.Invoices;
using SifenInvoicing.Application.XmlDe;

namespace SifenInvoicing.Tests.XmlDe;

/// <summary>Insumos del escenario minimo de Fase 4.2 (DE01, Test, PYG, IVA 10 %, 2 items, contado). Datos ficticios de prueba.</summary>
public static class SifenDeBuilderFixtures
{
    public static GenerateCdcInput CdcInput { get; } =
        new("01", "80000001", "3", "001", "001", "1000050", "2", "1", "000000023", "20200507");

    public static string Cdc { get; } = CdcGenerator.GenerateCDC(CdcInput);

    public static FiscalDocumentModel Fiscal() =>
        FiscalCalculationEngine.Calculate(
            new[]
            {
                new FiscalLineInput(1m, 1_100_000m, InvoiceVatType.Vat10),
                new FiscalLineInput(1m, 1_100_000m, InvoiceVatType.Vat10),
            },
            "PYG");

    public static SifenDeEmitter Emitter() => new(
        Ruc: "80000001",
        RucCheckDigit: "3",
        TaxpayerType: 2,
        LegalName: "EMPRESA DE PRUEBA S.A.",
        Address: "CALLE 1 CASI CALLE 2",
        HouseNumber: 0,
        DepartmentCode: 1,
        DepartmentDescription: "CAPITAL",
        CityCode: 1,
        CityDescription: "ASUNCION (DISTRITO)",
        Phone: "012123456",
        Email: "correo@correo.com",
        EconomicActivities: new[] { new SifenDeEconomicActivity("46510", "COMERCIO AL POR MAYOR DE EQUIPOS INFORMATICOS Y SOFTWARE") });

    public static SifenDeReceiver TaxpayerReceiver() => new(
        SifenDeReceiverNature.Taxpayer,
        SifenDeOperationType.B2B,
        "RECEPTOR DEL DOCUMENTO",
        TaxpayerKind: 2,
        Ruc: "80000002",
        RucCheckDigit: "1");

    public static SifenDeReceiver NonTaxpayerReceiver() => new(
        SifenDeReceiverNature.NonTaxpayer,
        SifenDeOperationType.B2C,
        "CLIENTE FINAL",
        IdentityDocumentType: 1,
        IdentityDocumentNumber: "1234567");

    public static SifenDeBuildInput Input(SifenDeReceiver? receiver = null) => new(
        Cdc,
        new DateTime(2020, 5, 7, 15, 3, 57),
        new DateTime(2020, 5, 7, 15, 4, 10),
        SifenDeEnvironment.Test,
        new SifenDeStamp("12345678", "001", "001", "1000050", new DateOnly(2019, 8, 13)),
        Emitter(),
        receiver ?? TaxpayerReceiver(),
        new SifenDeOperation(TransactionType: 1, PresenceIndicator: 1),
        new[]
        {
            new SifenDeItem("A1", "ITEM UNO", 77, "UNI"),
            new SifenDeItem("A2", "ITEM DOS", 77, "UNI"),
        },
        Fiscal());
}
