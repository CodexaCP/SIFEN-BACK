using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using SifenInvoicing.Application.Qr;
using SifenInvoicing.Application.XmlDe;
using SifenInvoicing.Application.XmlSigning;
using SifenInvoicing.Domain.Common;
using SifenInvoicing.Domain.Tenants;
using SifenInvoicing.Infrastructure.Persistence;
using SifenInvoicing.Infrastructure.Qr;
using static SifenInvoicing.Tests.XmlDe.SigningTestKit;

namespace SifenInvoicing.Tests.XmlDe;

/// <summary>
/// Fase 4.5 - QR oficial (Manual v150 13.8) generado DESPUES de firmar, con el DigestValue de la firma definitiva y el CSC
/// del tenant efectivo. IdCSC/CSC de estas pruebas: valores genericos de Test publicados en la Guia de Pruebas (no son de
/// ningun contribuyente). PENDIENTE DE PRUEBA SIFEN: aceptacion del QR por la consulta publica de Test.
/// </summary>
public sealed class SifenDeQrTests
{
    private const string CscA = "ABCD0000000000000000000000000000";
    private const string CscB = "EFGH0000000000000000000000000000";

    private sealed record Setup(SifenDbContext Db, SifenDeQrAttacher Attacher, string SignedXml, TestSigningIdentity Identity, Guid TenantId);

    private static async Task<Setup> ArrangeAsync(
        SifenDeReceiver? receiver = null,
        SifenEnvironmentType environment = SifenEnvironmentType.Test,
        string idCsc = "0001",
        string csc = CscA)
    {
        var identity = new TestSigningIdentity();
        var tenantId = Guid.NewGuid();
        var db = InMemoryDb(tenantId);
        db.TenantCertificateMetadata.Add(Metadata(tenantId, "ref:cert"));
        db.TenantSifenSettings.Add(TenantSifenSettings.Create(
            tenantId, environment, idCsc, "csc:a", "001", "001", "1", null, null, null, null, null, null, null));
        await db.SaveChangesAsync();

        var xml = new SifenDeXmlBuilder(new SifenDeBuilderOptions(DSisFactMode.EmitContributorSystem))
            .Build(SifenDeBuilderFixtures.Input(receiver)).Xml;
        var signed = await Signer(db, new Dictionary<string, byte[]> { ["ref:cert"] = identity.Pfx }).SignAsync(
            new SignXmlDocumentCommand(tenantId, environment, SifenDeBuilderFixtures.Cdc, xml));
        var attacher = new SifenDeQrAttacher(
            db, new MapSecrets(new Dictionary<string, byte[]>(), new Dictionary<string, string> { ["csc:a"] = csc }), new SifenQrBuilder());
        return new Setup(db, attacher, signed.SignedXml, identity, tenantId);
    }

    private static string QrUrl(string finalXml) =>
        XDocument.Parse(finalXml).Root!.Element(Sifen + "gCamFuFD")!.Element(Sifen + "dCarQR")!.Value;

    private static Dictionary<string, string> Parameters(string url) =>
        url[(url.IndexOf('?') + 1)..].Split('&').Select(p => p.Split('=', 2)).ToDictionary(p => p[0], p => p[1]);

    [Fact]
    public async Task Qr_IsBuiltFromTheSignedDocument_WithTheDefinitiveDigest_AndIndependentlyRecomputableHash()
    {
        var s = await ArrangeAsync();
        using var _ = s.Identity;
        await using var __ = s.Db;

        var finalXml = await s.Attacher.AttachAsync(s.TenantId, SifenEnvironmentType.Test, s.SignedXml);

        var root = XDocument.Parse(finalXml).Root!;
        var url = QrUrl(finalXml);
        var p = Parameters(url);
        var digestInSignature = root.Element(Ds + "Signature")!.Descendants(Ds + "DigestValue").Single().Value;

        Assert.StartsWith("https://ekuatia.set.gov.py/consultas-test/qr?", url);
        Assert.Equal(new[] { "nVersion", "Id", "dFeEmiDE", "dRucRec", "dTotGralOpe", "dTotIVA", "cItems", "DigestValue", "IdCSC", "cHashQR" }, p.Keys.ToArray());
        Assert.Equal("150", p["nVersion"]);
        Assert.Equal(SifenDeBuilderFixtures.Cdc, p["Id"]);
        Assert.Equal(Convert.ToHexString(Encoding.ASCII.GetBytes(root.Descendants(Sifen + "dFeEmiDE").Single().Value)).ToLowerInvariant(), p["dFeEmiDE"]);
        Assert.Equal("80000002", p["dRucRec"]);
        Assert.Equal("2200000", p["dTotGralOpe"]);
        Assert.Equal("200000", p["dTotIVA"]);
        Assert.Equal("2", p["cItems"]);
        // DigestValue del QR = hex del texto base64 del DigestValue de la firma DEFINITIVA.
        Assert.Equal(Convert.ToHexString(Encoding.ASCII.GetBytes(digestInSignature)).ToLowerInvariant(), p["DigestValue"]);
        Assert.Equal("0001", p["IdCSC"]);

        var data = url[(url.IndexOf('?') + 1)..url.IndexOf("&cHashQR=", StringComparison.Ordinal)];
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(data + CscA))).ToLowerInvariant(),
            p["cHashQR"]);
        Assert.DoesNotContain(CscA, finalXml);
        Assert.InRange(url.Length, 100, 600);
    }

    [Fact]
    public async Task Qr_IsAddedOutsideTheSignedPart_WithoutChangingDeOrSignature_AndModifyingItKeepsTheSignatureValid()
    {
        var s = await ArrangeAsync();
        using var _ = s.Identity;
        await using var __ = s.Db;

        var finalXml = await s.Attacher.AttachAsync(s.TenantId, SifenEnvironmentType.Test, s.SignedXml);

        var root = XDocument.Parse(finalXml).Root!;
        Assert.Equal(["dVerFor", "DE", "Signature", "gCamFuFD"], root.Elements().Select(e => e.Name.LocalName));
        Assert.Empty(root.Element(Sifen + "DE")!.Descendants(Sifen + "gCamFuFD"));
        Assert.Equal(s.SignedXml.Replace("</rDE>", string.Empty), finalXml[..finalXml.IndexOf("<gCamFuFD>", StringComparison.Ordinal)]);
        Assert.True(CheckSignature(finalXml, s.Identity.Certificate));

        root.Element(Sifen + "gCamFuFD")!.Element(Sifen + "dCarQR")!.Value += "&alterado=1";
        Assert.True(CheckSignature(root.Document!.ToString(SaveOptions.DisableFormatting), s.Identity.Certificate));
        // Los "&" se serializan como &amp; dentro de dCarQR (Manual 13.8.4.5).
        Assert.Contains("&amp;IdCSC=0001&amp;cHashQR=", finalXml);
    }

    [XsdPackageFact(XsdPackage.ReceptionRoot)]
    public async Task FinalXml_ValidatesAgainstTheOfficialXsd_WithoutPlaceholders()
    {
        var s = await ArrangeAsync();
        using var _ = s.Identity;
        await using var __ = s.Db;

        var finalXml = await s.Attacher.AttachAsync(s.TenantId, SifenEnvironmentType.Test, s.SignedXml);

        await DeTestKit.Xsd().EnsureFinalValidAsync(finalXml);
        Assert.Empty(XsdPackage.ValidateDe(XDocument.Parse(finalXml)));
    }

    [Fact]
    public async Task Qr_IsDeterministic_ForTheSameSignedDocument()
    {
        var s = await ArrangeAsync();
        using var _ = s.Identity;
        await using var __ = s.Db;

        var first = await s.Attacher.AttachAsync(s.TenantId, SifenEnvironmentType.Test, s.SignedXml);
        var second = await s.Attacher.AttachAsync(s.TenantId, SifenEnvironmentType.Test, s.SignedXml);

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task NonTaxpayerReceiver_UsesDNumIDRec()
    {
        var s = await ArrangeAsync(SifenDeBuilderFixtures.NonTaxpayerReceiver());
        using var _ = s.Identity;
        await using var __ = s.Db;

        var p = Parameters(QrUrl(await s.Attacher.AttachAsync(s.TenantId, SifenEnvironmentType.Test, s.SignedXml)));

        Assert.True(p.ContainsKey("dNumIDRec"));
        Assert.False(p.ContainsKey("dRucRec"));
    }

    [Fact]
    public async Task Tenant_UsesItsOwnCsc_AndAnotherTenantsConfigurationIsNeverReused()
    {
        var a = await ArrangeAsync(idCsc: "0001", csc: CscA);
        var b = await ArrangeAsync(idCsc: "0002", csc: CscB);
        using var _a = a.Identity;
        using var _b = b.Identity;
        await using var _da = a.Db;
        await using var _db = b.Db;

        var qrA = QrUrl(await a.Attacher.AttachAsync(a.TenantId, SifenEnvironmentType.Test, a.SignedXml));
        var qrB = QrUrl(await b.Attacher.AttachAsync(b.TenantId, SifenEnvironmentType.Test, b.SignedXml));

        Assert.Equal("0001", Parameters(qrA)["IdCSC"]);
        Assert.Equal("0002", Parameters(qrB)["IdCSC"]);
        Assert.NotEqual(Parameters(qrA)["cHashQR"], Parameters(qrB)["cHashQR"]);
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(qrB[(qrB.IndexOf('?') + 1)..qrB.IndexOf("&cHashQR=", StringComparison.Ordinal)] + CscB))).ToLowerInvariant(),
            Parameters(qrB)["cHashQR"]);

        // El attacher de A, sobre el contexto de A, no encuentra configuracion para el tenant B ni para un tenant ajeno.
        await Assert.ThrowsAsync<DomainException>(() => a.Attacher.AttachAsync(b.TenantId, SifenEnvironmentType.Test, a.SignedXml));
        await Assert.ThrowsAsync<DomainException>(() => a.Attacher.AttachAsync(Guid.NewGuid(), SifenEnvironmentType.Test, a.SignedXml));
    }

    [Fact]
    public async Task Environment_IsDecidedByTheTenantSettings_NoSettingsForTheEnvironmentMeansNoQr()
    {
        var s = await ArrangeAsync(environment: SifenEnvironmentType.Test);
        using var _ = s.Identity;
        await using var __ = s.Db;

        await Assert.ThrowsAsync<DomainException>(() => s.Attacher.AttachAsync(s.TenantId, SifenEnvironmentType.Production, s.SignedXml));
    }

    [Theory]
    [InlineData("SHORT")]
    [InlineData("ABCD00000000000000000000000000-0")]
    public async Task InvalidCsc_IsRejected_WithoutLeakingTheSecret(string badCsc)
    {
        var s = await ArrangeAsync(csc: badCsc);
        using var _ = s.Identity;
        await using var __ = s.Db;

        var ex = await Assert.ThrowsAsync<DomainException>(() => s.Attacher.AttachAsync(s.TenantId, SifenEnvironmentType.Test, s.SignedXml));
        Assert.DoesNotContain(badCsc, ex.Message);
    }

    [Fact]
    public async Task UnsignedOrAlreadyQrDocument_IsRejected()
    {
        var s = await ArrangeAsync();
        using var _ = s.Identity;
        await using var __ = s.Db;
        var unsigned = new SifenDeXmlBuilder(new SifenDeBuilderOptions(DSisFactMode.EmitContributorSystem)).Build(SifenDeBuilderFixtures.Input()).Xml;

        await Assert.ThrowsAsync<DomainException>(() => s.Attacher.AttachAsync(s.TenantId, SifenEnvironmentType.Test, unsigned));
        var withQr = await s.Attacher.AttachAsync(s.TenantId, SifenEnvironmentType.Test, s.SignedXml);
        await Assert.ThrowsAsync<DomainException>(() => s.Attacher.AttachAsync(s.TenantId, SifenEnvironmentType.Test, withQr));
    }

    // PENDIENTE DE FUENTE OFICIAL: no existe vector oficial completo (XML firmado + QR) del DE01; el unico vector publicado
    // (Manual 13.8.4) se prueba en SifenQrBuilderTests.Build_ShouldReproduceManualV150Example.
}
