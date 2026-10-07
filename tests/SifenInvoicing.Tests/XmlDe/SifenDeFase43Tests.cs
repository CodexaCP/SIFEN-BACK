using System.Xml.Linq;
using Microsoft.Extensions.Configuration;
using SifenInvoicing.Application.Fiscal;
using SifenInvoicing.Application.Invoices;
using SifenInvoicing.Application.Numbering;
using SifenInvoicing.Application.XmlDe;
using SifenInvoicing.Domain.Common;
using SifenInvoicing.Domain.Tenants;
using SifenInvoicing.Infrastructure.XmlValidation;

namespace SifenInvoicing.Tests.XmlDe;

/// <summary>Fase 4.3 - integridad CDC/XML, validador XSD de produccion, assembler y tabla de unidades.</summary>
public sealed class SifenDeFase43Tests
{
    private static readonly XNamespace Ns = DeReferenceStructure.Namespace;

    private static string ValidXml(SifenDeBuilderOptions? options = null) =>
        new SifenDeXmlBuilder(options ?? new SifenDeBuilderOptions(DSisFactMode.EmitContributorSystem))
            .Build(SifenDeBuilderFixtures.Input()).Xml;

    private static ISifenDeXsdValidator Validator(string? directory = null) =>
        new SifenDeXsdValidator(
            new XmlSchemaValidator(),
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { [SifenDeXsdValidator.ConfigurationKey] = directory ?? XsdPackage.Dir })
                .Build());

    // ---- Integridad CDC / XML ----

    [Fact]
    public void Integrity_AcceptsBuilderOutput()
        => SifenDeXmlIntegrity.Verify(ValidXml(), SifenDeBuilderFixtures.Cdc, SifenDeBuilderFixtures.Fiscal());

    [Fact]
    public void Integrity_RejectsDeIdDifferentFromCdc()
    {
        var doc = XDocument.Parse(ValidXml());
        var cdc = SifenDeBuilderFixtures.Cdc;
        doc.Root!.Element(Ns + "DE")!.SetAttributeValue("Id", cdc[..43] + (cdc[43] == '0' ? '1' : '0'));

        var ex = Assert.Throws<DomainException>(() => SifenDeXmlIntegrity.Verify(doc.ToString(), cdc, SifenDeBuilderFixtures.Fiscal()));
        Assert.Contains("DE@Id", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Integrity_RejectsWrongCheckDigit()
    {
        var doc = XDocument.Parse(ValidXml());
        var dv = doc.Root!.Element(Ns + "DE")!.Element(Ns + "dDVId")!;
        dv.Value = dv.Value == "0" ? "1" : "0";

        var ex = Assert.Throws<DomainException>(() => SifenDeXmlIntegrity.Verify(doc.ToString(), SifenDeBuilderFixtures.Cdc, SifenDeBuilderFixtures.Fiscal()));
        Assert.Contains("dDVId", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Integrity_RejectsInvalidCdc()
    {
        var ex = Assert.Throws<DomainException>(() => SifenDeXmlIntegrity.Verify(ValidXml(), new string('1', 44), SifenDeBuilderFixtures.Fiscal()));
        Assert.Contains("CDC", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Integrity_RejectsTotalsDifferentFromFiscalEngine()
    {
        var xml = ValidXml().Replace("<dTotGralOpe>2200000</dTotGralOpe>", "<dTotGralOpe>2199999</dTotGralOpe>", StringComparison.Ordinal);

        var ex = Assert.Throws<DomainException>(() => SifenDeXmlIntegrity.Verify(xml, SifenDeBuilderFixtures.Cdc, SifenDeBuilderFixtures.Fiscal()));
        Assert.Contains("dTotGralOpe", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Integrity_RejectsWrongNamespaceRoot()
    {
        var xml = ValidXml().Replace(DeReferenceStructure.Namespace, "http://ekuatia.set.gov.py/sifen/otro", StringComparison.Ordinal);
        Assert.Throws<DomainException>(() => SifenDeXmlIntegrity.Verify(xml, SifenDeBuilderFixtures.Cdc, SifenDeBuilderFixtures.Fiscal()));
    }

    [Fact]
    public void Builder_DoesNotRegenerateCdc_AndIsDeterministic()
    {
        var builder = new SifenDeXmlBuilder(new SifenDeBuilderOptions(DSisFactMode.EmitContributorSystem));
        var first = builder.Build(SifenDeBuilderFixtures.Input());
        var second = builder.Build(SifenDeBuilderFixtures.Input());

        Assert.Equal(SifenDeBuilderFixtures.Cdc, first.Cdc);
        Assert.Equal(first.Xml, second.Xml);
    }

    [Fact]
    public void Builder_RejectsCdcInconsistentWithReservedNumber()
    {
        var reserved = new ReservedNumber(Guid.NewGuid(), "12345678", new DateOnly(2019, 8, 13), null, "01", "001", "001", null, 1000051);
        var plan = SifenDeInputAssembler.PlanRequest(
            new CreateInvoiceCommand("k", null, "Cliente", InvoiceReceiverDocumentType.Ci, "1234567", null, null, null,
                InvoiceCurrency.PYG, InvoiceSaleCondition.Cash,
                [new CreateInvoiceItemCommand("ITEM", 1, 1100000m, 10, "A1", 77)]), 1, 1);
        var fiscal = FiscalCalculationEngine.Calculate([new FiscalLineInput(1m, 1100000m, InvoiceVatType.Vat10)], "PYG");

        // El CDC de la fixture corresponde al numero 1000050; el reservado es 1000051: el builder lo rechaza.
        var input = SifenDeInputAssembler.Assemble(plan, SifenDeBuilderFixtures.Emitter(), reserved, SifenDeBuilderFixtures.Cdc,
            new DateTime(2020, 5, 7, 15, 3, 57), SifenDeEnvironment.Test, fiscal);

        Assert.Throws<DomainException>(() => new SifenDeXmlBuilder().Build(input));
    }

    // ---- Validador XSD de produccion / local ----

    [XsdPackageFact(XsdPackage.ReceptionRoot)]
    public async Task XsdValidator_AcceptsBuilderOutput_WithPublishedXsdVariant()
        => await Validator().EnsureValidAsync(ValidXml());

    [XsdPackageFact(XsdPackage.ReceptionRoot)]
    public async Task XsdValidator_RejectsOmittedDSisFact_WithDetail()
    {
        var ex = await Assert.ThrowsAsync<DomainException>(() => Validator().EnsureValidAsync(ValidXml(new SifenDeBuilderOptions(DSisFactMode.Omit))));
        Assert.Contains("dSisFact", ex.Message, StringComparison.Ordinal);
        Assert.Contains("linea", ex.Message, StringComparison.Ordinal);
    }

    [XsdPackageFact(XsdPackage.ReceptionRoot)]
    public async Task XsdValidator_RejectsXmlOutsideXsd()
    {
        var doc = XDocument.Parse(ValidXml());
        doc.Root!.Element(Ns + "DE")!.Element(Ns + "gTimb")!.Remove();

        var ex = await Assert.ThrowsAsync<DomainException>(() => Validator().EnsureValidAsync(doc.ToString()));
        Assert.Contains("XSD", ex.Message, StringComparison.Ordinal);
    }

    [XsdPackageFact(XsdPackage.ReceptionRoot)]
    public async Task XsdValidator_RejectsWrongNamespace_NotSilently()
    {
        var xml = ValidXml().Replace(DeReferenceStructure.Namespace, "http://ekuatia.set.gov.py/sifen/otro", StringComparison.Ordinal);
        await Assert.ThrowsAsync<DomainException>(() => Validator().EnsureValidAsync(xml));
    }

    [XsdPackageFact(XsdPackage.ReceptionRoot)]
    public async Task XsdValidator_RejectsDFeFinT_NotInXsd()
    {
        var doc = XDocument.Parse(ValidXml());
        doc.Root!.Element(Ns + "DE")!.Element(Ns + "gTimb")!.Add(new XElement(Ns + "dFeFinT", "2030-01-01"));

        await Assert.ThrowsAsync<DomainException>(() => Validator().EnsureValidAsync(doc.ToString()));
    }

    [Fact]
    public async Task XsdValidator_FailsClosed_WhenPackageIsNotDeployed()
    {
        var empty = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var ex = await Assert.ThrowsAsync<DomainException>(() => Validator(empty).EnsureValidAsync("<rDE/>"));
            Assert.Contains("no esta desplegado", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(empty, true);
        }
    }

    [Fact]
    public void LocalResolver_NeverReachesTheNetwork()
    {
        var resolver = new LocalSchemaPackageResolver(Path.GetTempPath());
        var uri = resolver.ResolveUri(null, LocalSchemaPackageResolver.OfficialBaseUrl + "DE_v150.xsd");
        Assert.True(uri.IsFile);
        Assert.Throws<System.Xml.XmlException>(() => resolver.GetEntity(new Uri("https://example.org/x.xsd"), null, null));
        Assert.Throws<System.Xml.XmlException>(() => resolver.ResolveUri(null, LocalSchemaPackageResolver.OfficialBaseUrl + "../etc/passwd"));
    }

    [Fact]
    public void ShippedPackage_IsCopiedToTheOutputDirectoryOfInfrastructure()
        => Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "Schemas", "sifen", "v150", SifenDeXsdValidator.RootSchemaFile)));

    // ---- Assembler ----

    private static CreateInvoiceCommand Command(
        InvoiceReceiverDocumentType type = InvoiceReceiverDocumentType.Ci,
        string document = "1234567",
        InvoiceCurrency currency = InvoiceCurrency.PYG,
        InvoiceSaleCondition condition = InvoiceSaleCondition.Cash,
        int? kind = null) =>
        new("k", null, "Cliente", type, document, null, null, null, currency, condition,
            [new CreateInvoiceItemCommand("ITEM", 1, 1100000m, 10, "A1", 77)], ReceptorTaxpayerKind: kind);

    [Fact]
    public void Assembler_MapsTaxpayerReceiverWithRucAndDv()
    {
        var plan = SifenDeInputAssembler.PlanRequest(Command(InvoiceReceiverDocumentType.Ruc, "80000002-1", kind: 2), 1, 1);

        Assert.Equal(SifenDeReceiverNature.Taxpayer, plan.Receiver.Nature);
        Assert.Equal("80000002", plan.Receiver.Ruc);
        Assert.Equal("1", plan.Receiver.RucCheckDigit);
        Assert.Null(plan.Receiver.IdentityDocumentNumber);
    }

    [Fact]
    public void Assembler_MapsNonTaxpayerReceiverWithIdentityDocument()
    {
        var plan = SifenDeInputAssembler.PlanRequest(Command(), 1, 1);

        Assert.Equal(SifenDeReceiverNature.NonTaxpayer, plan.Receiver.Nature);
        Assert.Equal(1, plan.Receiver.IdentityDocumentType);
        Assert.Equal("1234567", plan.Receiver.IdentityDocumentNumber);
        Assert.Null(plan.Receiver.Ruc);
    }

    [Theory]
    [InlineData(InvoiceReceiverDocumentType.Ruc, "80000002", 2, "numero-DV")]
    [InlineData(InvoiceReceiverDocumentType.Ruc, "80000002-1", null, "iTiContRec")]
    public void Assembler_RejectsIncompleteTaxpayerReceiver(InvoiceReceiverDocumentType type, string document, int? kind, string expected)
    {
        var ex = Assert.Throws<DomainException>(() => SifenDeInputAssembler.PlanRequest(Command(type, document, kind: kind), 1, 1));
        Assert.Contains(expected, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Assembler_RejectsOutOfScopeCurrencyAndCondition()
    {
        var ex = Assert.Throws<DomainException>(() =>
            SifenDeInputAssembler.PlanRequest(Command(currency: InvoiceCurrency.USD, condition: InvoiceSaleCondition.Credit), 1, 1));
        Assert.Contains("moneda", ex.Message, StringComparison.Ordinal);
        Assert.Contains("contado", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Assembler_RequiresOperationDecisionsFromRequestOrConfiguration()
    {
        var ex = Assert.Throws<DomainException>(() => SifenDeInputAssembler.PlanRequest(Command(), null, null));
        Assert.Contains("iTipTra", ex.Message, StringComparison.Ordinal);
        Assert.Contains("iIndPres", ex.Message, StringComparison.Ordinal);

        var plan = SifenDeInputAssembler.PlanRequest(Command() with { TransactionType = 2, PresenceIndicator = 2 }, 1, 1);
        Assert.Equal(2, plan.Operation.TransactionType);
        Assert.Equal(2, plan.Operation.PresenceIndicator);
    }

    [Fact]
    public void Assemble_DropsFractionsOfSecondsAndLeavesKindUnspecified()
    {
        var reserved = new ReservedNumber(Guid.NewGuid(), "12345678", new DateOnly(2019, 8, 13), null, "01", "001", "001", null, 1000050);
        var plan = SifenDeInputAssembler.PlanRequest(Command(), 1, 1);
        var fiscal = FiscalCalculationEngine.Calculate([new FiscalLineInput(1m, 1100000m, InvoiceVatType.Vat10)], "PYG");

        var input = SifenDeInputAssembler.Assemble(plan, SifenDeBuilderFixtures.Emitter(), reserved, SifenDeBuilderFixtures.Cdc,
            new DateTimeOffset(2020, 5, 7, 15, 3, 57, 987, TimeSpan.FromHours(-3)).DateTime, SifenDeEnvironment.Test, fiscal);

        Assert.Equal(0, input.FechaEmision.Millisecond);
        Assert.Equal(DateTimeKind.Unspecified, input.FechaFirma.Kind);
        Assert.Equal(SifenDeBuilderFixtures.Cdc, input.Cdc);
    }

    // ---- Tabla 5 / opciones ----

    [XsdPackageFact("Unidades_Medida_v141.xsd")]
    public void UnitsTable_EveryCodeExistsInPackagedXsdAndRepresentationMatches()
    {
        var xsd = XDocument.Load(Path.Combine(XsdPackage.Dir, "Unidades_Medida_v141.xsd"));
        var xs = XNamespace.Get("http://www.w3.org/2001/XMLSchema");
        var annotated = xsd.Descendants(xs + "simpleType").Single(t => (string?)t.Attribute("name") == "tcUniMed")
            .Descendants(xs + "enumeration")
            .ToDictionary(
                e => int.Parse((string)e.Attribute("value")!),
                e => e.Descendants(XNamespace.Get("http://www.w3.org/2001/XMLSchema") + "documentation").First().Value);

        var checkedCodes = 0;
        foreach (var code in annotated.Keys)
        {
            if (!SifenDeUnitsOfMeasure.TryGetRepresentation(code, out var representation))
            {
                continue;
            }

            checkedCodes++;
            // El XSD anota "Descripcion - REPRESENTACION". Diferencia conocida: kg/m2 (XSD) vs kg/m² (Manual, Tabla 5).
            var xsdRepresentation = annotated[code][(annotated[code].LastIndexOf(" - ", StringComparison.Ordinal) + 3)..];
            Assert.True(
                representation == xsdRepresentation || (code == 79 && xsdRepresentation == "kg/m2"),
                $"cUniMed {code}: Manual '{representation}' vs XSD '{xsdRepresentation}'");
        }

        Assert.Equal(34, checkedCodes);
    }

    [Fact]
    public void UnitsTable_RejectsUnknownCode()
        => Assert.False(SifenDeUnitsOfMeasure.TryGetRepresentation(1, out _));

    [Fact]
    public void BuilderOptionsFactory_ReadsConfigurationAndRejectsInvalidValues()
    {
        static IConfiguration Config(params (string, string)[] values) => new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(v => v.Item1, v => (string?)v.Item2)).Build();

        Assert.Equal(DSisFactMode.Omit, SifenDeBuilderOptionsFactory.Create(Config()).DSisFact);
        Assert.Equal(DSisFactMode.EmitContributorSystem,
            SifenDeBuilderOptionsFactory.Create(Config(("Sifen:De:DSisFact", "EmitContributorSystem"))).DSisFact);
        Assert.Throws<DomainException>(() => SifenDeBuilderOptionsFactory.Create(Config(("Sifen:De:DSisFact", "xx"))));
    }

    [Fact]
    public void EconomicActivity_ValidatesLimits()
    {
        var tenant = Guid.NewGuid();
        var profile = Guid.NewGuid();
        Assert.Throws<DomainException>(() => TaxpayerEconomicActivity.Create(tenant, profile, "", "x"));
        Assert.Throws<DomainException>(() => TaxpayerEconomicActivity.Create(tenant, profile, "123456789", "x"));
        Assert.Throws<DomainException>(() => TaxpayerEconomicActivity.Create(tenant, profile, "46510", new string('x', 301)));
        Assert.Equal("46510", TaxpayerEconomicActivity.Create(tenant, profile, " 46510 ", "COMERCIO").Code);
    }
}
