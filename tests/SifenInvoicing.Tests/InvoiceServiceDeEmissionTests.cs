using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SifenInvoicing.Application.Invoices;
using SifenInvoicing.Application.Tenancy;
using SifenInvoicing.Application.XmlDe;
using SifenInvoicing.Domain.Common;
using SifenInvoicing.Domain.Tenants;
using SifenInvoicing.Infrastructure.Diagnostics;
using SifenInvoicing.Infrastructure.Invoices;
using SifenInvoicing.Infrastructure.Persistence;
using SifenInvoicing.Infrastructure.Tenancy;
using SifenInvoicing.Tests.XmlDe;

namespace SifenInvoicing.Tests;

/// <summary>
/// Fase 4.3 - flujo real EfInvoiceService -> FiscalCalculationEngine -> SifenDeXmlBuilder -> XSD local, con el paquete
/// oficial v150. Variante del XSD en uso: DE_v150.xsd con dSisFact (DSisFactMode.EmitContributorSystem); la contradiccion
/// NT-10 vs XSD sigue abierta (PENDIENTE DE PRUEBA SIFEN TEST). Las pruebas de transaccion usan SQLite (el proveedor
/// InMemory no tiene transacciones).
/// </summary>
public sealed partial class InvoiceServiceTests
{
    private static readonly XNamespace Sifen = "http://ekuatia.set.gov.py/sifen/xsd";

    private static CreateInvoiceCommand TwoItemCommand(string key) =>
        new(key, null, "Cliente Demo", InvoiceReceiverDocumentType.Ci, "1234567", null, null, null,
            InvoiceCurrency.PYG, InvoiceSaleCondition.Cash,
            [
                new CreateInvoiceItemCommand("ITEM UNO", 1, 1100000m, 10, "A1", 77),
                new CreateInvoiceItemCommand("ITEM DOS", 1, 1100000m, 10, "A2", 77),
            ]);

    private static string ValidateWithPlaceholders(string persistedXml) =>
        string.Join("; ", XsdPackage.ValidateDe(DeFixtures.AttachPlaceholderSignature(XDocument.Parse(persistedXml))));

    [XsdPackageFact(XsdPackage.ReceptionRoot)]
    public async Task DeFlow_PositiveCase_BuildsValidXmlWithPersistedCdc()
    {
        var f = await CreateFiscalFixtureAsync();
        f.Use();

        var result = await f.Service.CreateAsync(TwoItemCommand("de-ok"));

        var doc = await f.Db.Documents.SingleAsync();
        var xml = XDocument.Parse(doc.XmlPayload);
        var de = xml.Root!.Element(Sifen + "DE")!;

        // CDC: el del documento persistido es el del XML (DE@Id y dDVId), sin regeneracion.
        Assert.Equal(doc.Cdc, result.Cdc);
        Assert.Equal(doc.Cdc, de.Attribute("Id")!.Value);
        Assert.Equal(doc.Cdc[^1].ToString(), de.Element(Sifen + "dDVId")!.Value);
        Assert.Equal(44, doc.Cdc.Length);

        // XML valido contra el XSD oficial v150 local (revalidado de forma independiente).
        Assert.Equal(string.Empty, ValidateWithPlaceholders(doc.XmlPayload));

        // Sin elementos inexistentes en el XSD y sin firma/QR en el XML persistido.
        Assert.Null(de.Descendants(Sifen + "dFeFinT").FirstOrDefault());
        Assert.Null(xml.Root.Element(Sifen + "Signature"));
        Assert.Null(xml.Root.Element(Sifen + "gCamFuFD"));
        Assert.Matches(new Regex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}$"), de.Element(Sifen + "dFecFirma")!.Value);
        Assert.Matches(new Regex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}$"), de.Element(Sifen + "gDatGralOpe")!.Element(Sifen + "dFeEmiDE")!.Value);
        Assert.Equal("150", xml.Root.Element(Sifen + "dVerFor")!.Value);

        // Timbrado y numero reservados por el servidor.
        Assert.Equal(TestFiscalSetup.StampingNumber, de.Element(Sifen + "gTimb")!.Element(Sifen + "dNumTim")!.Value);
        Assert.Equal("0000001", de.Element(Sifen + "gTimb")!.Element(Sifen + "dNumDoc")!.Value);

        // Dos items con codigo y unidad (Tabla 5) y totales del motor fiscal.
        var items = de.Element(Sifen + "gDtipDE")!.Elements(Sifen + "gCamItem").ToList();
        Assert.Equal(2, items.Count);
        Assert.Equal(["A1", "A2"], items.Select(i => i.Element(Sifen + "dCodInt")!.Value));
        Assert.All(items, i => Assert.Equal("77", i.Element(Sifen + "cUniMed")!.Value));
        Assert.All(items, i => Assert.Equal("UNI", i.Element(Sifen + "dDesUniMed")!.Value));
        Assert.Equal("2200000", de.Element(Sifen + "gTotSub")!.Element(Sifen + "dTotGralOpe")!.Value);
        Assert.Equal(2200000m, doc.TotalAmount);

        var lines = await f.Db.DocumentLines.OrderBy(l => l.LineNumber).ToListAsync();
        Assert.Equal(["A1", "A2"], lines.Select(l => l.ProductCode));
        Assert.All(lines, l => Assert.Equal(77, l.UnitCode));
        Assert.All(lines, l => Assert.Equal("UNI", l.UnitDescription));

        // Emisor y actividad economica vienen del perfil persistido.
        var emitter = de.Element(Sifen + "gDatGralOpe")!.Element(Sifen + "gEmis")!;
        Assert.Equal("80012345", emitter.Element(Sifen + "dRucEm")!.Value);
        Assert.Equal("46510", emitter.Element(Sifen + "gActEco")!.Element(Sifen + "cActEco")!.Value);
    }

    [XsdPackageFact(XsdPackage.ReceptionRoot)]
    public async Task DeFlow_Replay_ReturnsSameCdcAndXmlWithoutRegenerating()
    {
        var f = await CreateFiscalFixtureAsync();
        f.Use();

        var first = await f.Service.CreateAsync(TwoItemCommand("de-replay"));
        var second = await f.Service.CreateAsync(TwoItemCommand("de-replay"));

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(first.Cdc, second.Cdc);
        Assert.Equal(first.XmlPayload, second.XmlPayload);
        Assert.Equal(1, await f.Db.Documents.CountAsync());
    }

    [Fact]
    public async Task DeFlow_EmitterWithoutEconomicActivity_FailsBeforeReservingNumber()
    {
        var f = await CreateFiscalFixtureAsync(withActivity: false);
        f.Use();
        var before = await f.Db.NumberingSequences.Select(s => s.NextNumber).SingleAsync();

        var ex = await Assert.ThrowsAsync<UserFacingException>(() => f.Service.CreateAsync(TwoItemCommand("no-act")));

        Assert.Equal("FISCAL_CONFIGURATION_INCOMPLETE", ex.ErrorCode);
        Assert.Contains("gActEco", ex.Message, StringComparison.Ordinal);
        Assert.Equal(before, await f.Db.NumberingSequences.Select(s => s.NextNumber).SingleAsync());
        Assert.Empty(await f.Db.Documents.ToListAsync());
        Assert.Empty(await f.Db.IdempotencyRecords.ToListAsync());
    }

    [Fact]
    public async Task DeFlow_EmitterWithoutMandatoryData_ListsMissingFieldsAndDoesNotInvent()
    {
        var f = await CreateFiscalFixtureAsync(customizeTaxpayer: t =>
            t.UpdateFiscalData(2, "CALLE 1 CASI CALLE 2", "0", "1", "CAPITAL", null, null, "1", "ASUNCION (DISTRITO)", null, null));
        f.Use();

        var ex = await Assert.ThrowsAsync<UserFacingException>(() => f.Service.CreateAsync(TwoItemCommand("no-phone")));

        Assert.Equal("FISCAL_CONFIGURATION_INCOMPLETE", ex.ErrorCode);
        Assert.Contains("dTelEmi", ex.Message, StringComparison.Ordinal);
        Assert.Contains("dEmailE", ex.Message, StringComparison.Ordinal);
        Assert.Empty(await f.Db.Documents.ToListAsync());
    }

    [Theory]
    [InlineData(null, 77, "dCodInt")]
    [InlineData("A1", null, "cUniMed")]
    [InlineData("A1", 99999, "Tabla 5")]
    public async Task DeFlow_ItemWithoutCodeOrUnit_IsRejectedBeforeReservingNumber(string? code, int? unit, string expectedMessage)
    {
        var f = await CreateFiscalFixtureAsync();
        f.Use();
        var before = await f.Db.NumberingSequences.Select(s => s.NextNumber).SingleAsync();
        var command = Command("bad-item") with { Items = [new CreateInvoiceItemCommand("Servicio", 1, 100000m, 10, code, unit)] };

        var ex = await Assert.ThrowsAsync<DomainException>(() => f.Service.CreateAsync(command));

        Assert.Contains(expectedMessage, ex.Message, StringComparison.Ordinal);
        Assert.Equal(before, await f.Db.NumberingSequences.Select(s => s.NextNumber).SingleAsync());
        Assert.Empty(await f.Db.Documents.ToListAsync());
    }

    [Fact]
    public async Task DeFlow_TenantIsolation_ActivitiesOfAnotherTenantAreNotUsed()
    {
        var shared = new AsyncLocalTenantContextAccessor();
        var a = await CreateFiscalFixtureAsync(sharedAccessor: shared);
        var b = await CreateFiscalFixtureAsync(sharedAccessor: shared, sharedDb: a.Db, withActivity: false);

        shared.SetCurrent(new TenantContext { TenantId = a.TenantId.ToString(), ResolvedTenantId = a.TenantId, IsResolved = true });
        var ok = await a.Service.CreateAsync(TwoItemCommand("iso"));
        shared.SetCurrent(new TenantContext { TenantId = b.TenantId.ToString(), ResolvedTenantId = b.TenantId, IsResolved = true });
        var ex = await Assert.ThrowsAsync<UserFacingException>(() => b.Service.CreateAsync(TwoItemCommand("iso")));

        Assert.NotNull(ok.Cdc);
        Assert.Equal("FISCAL_CONFIGURATION_INCOMPLETE", ex.ErrorCode);
        Assert.Equal(1, await a.Db.Documents.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task Onboarding_RegisterFiscalProfile_ReplacesEconomicActivities_PerTenant()
    {
        var shared = new AsyncLocalTenantContextAccessor();
        var a = await CreateFiscalFixtureAsync(sharedAccessor: shared, withActivity: false);
        var b = await CreateFiscalFixtureAsync(sharedAccessor: shared, sharedDb: a.Db, withActivity: false);
        var onboarding = new SifenInvoicing.Infrastructure.Onboarding.EfTenantOnboardingService(a.Db, new NullAuditTrail(), new SystemClock(), null!, null!);

        FiscalCommand(a.TenantId, [new("46510", "COMERCIO"), new("62010", "SOFTWARE")]).Apply(onboarding);
        await FiscalCommand(a.TenantId, [new("46510", "COMERCIO AL POR MAYOR")]).RunAsync(onboarding);

        var rows = await a.Db.TaxpayerEconomicActivities.IgnoreQueryFilters().ToListAsync();
        var mine = Assert.Single(rows);
        Assert.Equal(a.TenantId, mine.TenantId);
        Assert.Equal("COMERCIO AL POR MAYOR", mine.Description);
        Assert.DoesNotContain(rows, r => r.TenantId == b.TenantId);

        var tooMany = Enumerable.Range(0, 10).Select(i => new SifenInvoicing.Application.Onboarding.RegisterEconomicActivity($"4651{i}", "X")).ToList();
        await Assert.ThrowsAsync<DomainException>(() => FiscalCommand(a.TenantId, tooMany).RunAsync(onboarding));
    }

    private static FiscalCall FiscalCommand(Guid tenantId, List<SifenInvoicing.Application.Onboarding.RegisterEconomicActivity> activities) =>
        new(new SifenInvoicing.Application.Onboarding.RegisterFiscalProfileCommand(
            tenantId, 2, "CALLE 1 CASI CALLE 2", "0", "1", "CAPITAL", null, null, "1", "ASUNCION (DISTRITO)", "021123456", "correo@correo.com", activities));

    private sealed record FiscalCall(SifenInvoicing.Application.Onboarding.RegisterFiscalProfileCommand Command)
    {
        public void Apply(SifenInvoicing.Infrastructure.Onboarding.EfTenantOnboardingService service) =>
            service.RegisterFiscalProfileAsync(Command).GetAwaiter().GetResult();

        public Task RunAsync(SifenInvoicing.Infrastructure.Onboarding.EfTenantOnboardingService service) =>
            service.RegisterFiscalProfileAsync(Command);
    }

    // ---- Transaccion: SQLite real (rollback efectivo) ----

    private sealed class SqliteScope : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"de-flow-{Guid.NewGuid():N}.db");

        public SifenDbContext NewContext(ITenantContextAccessor accessor)
        {
            var options = new DbContextOptionsBuilder<SifenDbContext>()
                .UseSqlite($"Data Source={Path};Default Timeout=60;Pooling=False")
                .Options;
            return new SqliteTestDbContext(options, new SystemClock(), accessor);
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            foreach (var file in new[] { Path, Path + "-wal", Path + "-shm" })
            {
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
            }
        }
    }

    private static async Task<Fixture> SqliteFixtureAsync(
        SqliteScope scope,
        SifenDeXmlBuilder? builder = null,
        Action<TaxpayerProfile>? customizeTaxpayer = null)
    {
        var accessor = new AsyncLocalTenantContextAccessor();
        var db = scope.NewContext(accessor);
        await db.Database.EnsureCreatedAsync();
        return await CreateFiscalFixtureAsync(sharedAccessor: accessor, sharedDb: db, builder: builder, customizeTaxpayer: customizeTaxpayer);
    }

    [XsdPackageFact(XsdPackage.ReceptionRoot)]
    public async Task DeFlow_XsdFailure_RollsBackAndDoesNotConsumeNumber_ThenSameKeyCanBeRetried()
    {
        using var scope = new SqliteScope();

        // Variante NT-10 (dSisFact omitido): el XSD publicado v150 lo exige 1..1 => el DE no valida => rollback.
        var failing = await SqliteFixtureAsync(scope, new SifenDeXmlBuilder(new SifenDeBuilderOptions(DSisFactMode.Omit)));
        failing.Use();
        var ex = await Assert.ThrowsAsync<DomainException>(() => failing.Service.CreateAsync(TwoItemCommand("xsd-key")));

        Assert.Contains("XSD", ex.Message, StringComparison.Ordinal);
        Assert.Contains("dSisFact", ex.Message, StringComparison.Ordinal);
        await using (var check = scope.NewContext(failing.Accessor))
        {
            Assert.Empty(await check.Documents.ToListAsync());
            Assert.Empty(await check.DocumentLines.ToListAsync());
            Assert.Empty(await check.IdempotencyRecords.ToListAsync());
            Assert.Equal(1, await check.NumberingSequences.Select(s => s.NextNumber).SingleAsync());
        }

        // Misma Idempotency-Key con la variante del XSD: el fallo no envenena la clave y reutiliza el numero 1.
        var okService = await RebuildServiceAsync(failing, scope, DeTestKit.Builder());
        var created = await okService.CreateAsync(TwoItemCommand("xsd-key"));
        await using var after = scope.NewContext(failing.Accessor);
        Assert.Equal("0000001", (await after.Documents.SingleAsync()).ExternalDocumentNumber);
        Assert.Equal(created.Cdc, (await after.Documents.SingleAsync()).Cdc);
    }

    [Fact]
    public async Task DeFlow_BuilderFailureAfterReservation_RollsBackNumber()
    {
        using var scope = new SqliteScope();
        // DV del RUC invalido: lo detecta el builder DESPUES de reservar el numero.
        var f = await SqliteFixtureAsync(scope, customizeTaxpayer: null);
        f.Use();
        var taxpayerId = await f.Db.TaxpayerProfiles.Select(t => t.Id).SingleAsync();
        await f.Db.Database.ExecuteSqlRawAsync("UPDATE TaxpayerProfiles SET RucCheckDigit = '9' WHERE Id = {0}", taxpayerId);
        f.Db.ChangeTracker.Clear();

        var ex = await Assert.ThrowsAsync<DomainException>(() => f.Service.CreateAsync(TwoItemCommand("bad-dv")));

        Assert.Contains("RUC", ex.Message, StringComparison.OrdinalIgnoreCase);
        await using var check = scope.NewContext(f.Accessor);
        Assert.Empty(await check.Documents.ToListAsync());
        Assert.Equal(1, await check.NumberingSequences.Select(s => s.NextNumber).SingleAsync());
    }

    [Fact]
    public async Task DeFlow_PersistenceError_RollsBackNumberAndLeavesNoDocument()
    {
        using var scope = new SqliteScope();
        var f = await SqliteFixtureAsync(scope);
        f.Use();
        await f.Db.Database.ExecuteSqlRawAsync(
            "CREATE TRIGGER fail_lines BEFORE INSERT ON DocumentLines BEGIN SELECT RAISE(ABORT, 'forced persistence failure'); END;");

        await Assert.ThrowsAsync<DbUpdateException>(() => f.Service.CreateAsync(TwoItemCommand("persist-fail")));

        await using var check = scope.NewContext(f.Accessor);
        Assert.Empty(await check.Documents.ToListAsync());
        Assert.Empty(await check.IdempotencyRecords.ToListAsync());
        Assert.Equal(1, await check.NumberingSequences.Select(s => s.NextNumber).SingleAsync());
    }

    private static Task<EfInvoiceService> RebuildServiceAsync(Fixture f, SqliteScope scope, SifenDeXmlBuilder builder)
    {
        var db = scope.NewContext(f.Accessor);
        var service = new EfInvoiceService(
            db, f.Accessor, builder, DeTestKit.Xsd(),
            new InvoiceKudePdfRenderer(), new ReadyTenantCertificateValidator(), new FakeXmlDocumentSigner(),
            new StubOperationalReadinessReporter(), new CountingSubmissionGateway(), new FakeResponseParser(),
            CreateConfiguration(), new NullAuditTrail(), new SystemClock(),
            new SifenInvoicing.Infrastructure.Numbering.EfNumberingService(db), new TestFiscalClock(), new FakeQrAttacher());
        return Task.FromResult(service);
    }
}
