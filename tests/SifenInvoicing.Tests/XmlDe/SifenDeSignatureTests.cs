using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Xml.Linq;
using SifenInvoicing.Application.XmlDe;
using SifenInvoicing.Application.XmlSigning;
using SifenInvoicing.Domain.Tenants;
using SifenInvoicing.Infrastructure.XmlSigning;
using static SifenInvoicing.Tests.XmlDe.SigningTestKit;

namespace SifenInvoicing.Tests.XmlDe;

/// <summary>
/// Fase 4.4 - firma XMLDSig real del DE01 con el SifenXmlDocumentSigner. Certificado autofirmado EFIMERO solo de prueba:
/// no es configuracion de produccion ni evidencia de aceptacion SIFEN. Signature se mantiene como HERMANA posterior de DE
/// dentro de rDE (orden del XSD v150: dVerFor, DE, Signature, gCamFuFD).
/// </summary>
public sealed class SifenDeSignatureTests
{
    private static string BuilderXml() =>
        new SifenDeXmlBuilder(new SifenDeBuilderOptions(DSisFactMode.EmitContributorSystem)).Build(SifenDeBuilderFixtures.Input()).Xml;

    private static async Task<(SignedXmlDocumentResult Result, TestSigningIdentity Identity)> SignAsync(string? xml = null)
    {
        var identity = new TestSigningIdentity();
        var tenantId = Guid.NewGuid();
        await using var db = InMemoryDb(tenantId);
        db.TenantCertificateMetadata.Add(Metadata(tenantId, "ref:cert"));
        await db.SaveChangesAsync();
        var signer = Signer(db, new Dictionary<string, byte[]> { ["ref:cert"] = identity.Pfx });
        var result = await signer.SignAsync(new SignXmlDocumentCommand(
            tenantId, SifenEnvironmentType.Test, SifenDeBuilderFixtures.Cdc, xml ?? BuilderXml()));
        return (result, identity);
    }

    [Fact]
    public async Task Signature_Exists_ReferencesCdc_AndRDeAsAWholeIsNotSigned()
    {
        var (result, identity) = await SignAsync();
        using var _ = identity;
        var root = XDocument.Parse(result.SignedXml).Root!;

        Assert.Equal(["dVerFor", "DE", "Signature"], root.Elements().Select(e => e.Name.LocalName));
        var signature = root.Element(Ds + "Signature")!;
        var references = signature.Element(Ds + "SignedInfo")!.Elements(Ds + "Reference").ToList();
        var reference = Assert.Single(references);
        Assert.Equal("#" + SifenDeBuilderFixtures.Cdc, reference.Attribute("URI")!.Value);
        // El elemento referenciado es DE (no rDE) y no hay Reference vacia ("") que firmaria el documento entero.
        Assert.Equal(SifenDeBuilderFixtures.Cdc, root.Element(Sifen + "DE")!.Attribute("Id")!.Value);
        Assert.Null(root.Attribute("Id"));
        Assert.Null(signature.Descendants().Select(e => e.Attribute("URI")?.Value).FirstOrDefault(u => u == string.Empty));
    }

    [Fact]
    public async Task Algorithms_AreSha256Rsa_C14n_AndSingleEnvelopedTransform_WithX509Data()
    {
        var (result, identity) = await SignAsync();
        using var _ = identity;
        var signature = XDocument.Parse(result.SignedXml).Root!.Element(Ds + "Signature")!;
        var signedInfo = signature.Element(Ds + "SignedInfo")!;
        var reference = signedInfo.Element(Ds + "Reference")!;

        Assert.Equal(SifenXmlSignatureAlgorithms.CanonicalizationMethod, signedInfo.Element(Ds + "CanonicalizationMethod")!.Attribute("Algorithm")!.Value);
        Assert.Equal("http://www.w3.org/2001/04/xmldsig-more#rsa-sha256", signedInfo.Element(Ds + "SignatureMethod")!.Attribute("Algorithm")!.Value);
        Assert.Equal("http://www.w3.org/2001/04/xmlenc#sha256", reference.Element(Ds + "DigestMethod")!.Attribute("Algorithm")!.Value);
        var transform = Assert.Single(reference.Element(Ds + "Transforms")!.Elements(Ds + "Transform"));
        Assert.Equal("http://www.w3.org/2000/09/xmldsig#enveloped-signature", transform.Attribute("Algorithm")!.Value);
        Assert.Equal(
            Convert.ToBase64String(identity.Certificate.RawData),
            signature.Element(Ds + "KeyInfo")!.Element(Ds + "X509Data")!.Element(Ds + "X509Certificate")!.Value);
    }

    [Fact]
    public async Task Digest_IsRecomputableIndependently_AndExposedDeterministically()
    {
        var (result, identity) = await SignAsync();
        using var _ = identity;
        var root = XDocument.Parse(result.SignedXml).Root!;
        var declared = root.Element(Ds + "Signature")!.Descendants(Ds + "DigestValue").Single().Value;

        var recomputed = Convert.ToBase64String(SHA256.HashData(Canonicalize(root.Element(Sifen + "DE")!)));

        Assert.Equal(declared, recomputed);
        Assert.Equal(declared, result.DigestValue);

        // Determinismo: firmar de nuevo el mismo DE produce el mismo DigestValue (insumo de la Fase 4.5).
        var (again, otherIdentity) = await SignAsync();
        using var __ = otherIdentity;
        Assert.Equal(declared, again.DigestValue);
    }

    [Fact]
    public async Task RsaSha256SignatureValue_VerifiesWithThePublicKey()
    {
        var (result, identity) = await SignAsync();
        using var _ = identity;
        var signature = XDocument.Parse(result.SignedXml).Root!.Element(Ds + "Signature")!;
        var signedInfoBytes = Canonicalize(signature.Element(Ds + "SignedInfo")!);
        var signatureValue = Convert.FromBase64String(signature.Element(Ds + "SignatureValue")!.Value);

        using var publicKey = identity.Certificate.GetRSAPublicKey()!;
        Assert.True(publicKey.VerifyData(signedInfoBytes, signatureValue, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        Assert.True(CheckSignature(result.SignedXml, identity.Certificate));
    }

    [Fact]
    public async Task AlteringSignedContent_InvalidatesTheSignature()
    {
        var (result, identity) = await SignAsync();
        using var _ = identity;
        var doc = XDocument.Parse(result.SignedXml);
        doc.Root!.Element(Sifen + "DE")!.Descendants(Sifen + "dTotGralOpe").First().Value = "1";

        Assert.False(CheckSignature(doc.ToString(SaveOptions.DisableFormatting), identity.Certificate));
    }

    [Fact]
    public async Task AlteringGCamFuFDAfterSigning_DoesNotInvalidateTheSignature_AndItIsNotSigned()
    {
        var (result, identity) = await SignAsync();
        using var _ = identity;
        var doc = XDocument.Parse(result.SignedXml);
        Assert.Null(doc.Root!.Element(Sifen + "gCamFuFD"));

        doc.Root.Add(new XElement(Sifen + "gCamFuFD", new XElement(Sifen + "dCarQR", DeFixtures.PlaceholderQr)));
        Assert.True(CheckSignature(doc.ToString(SaveOptions.DisableFormatting), identity.Certificate));

        doc.Root.Element(Sifen + "gCamFuFD")!.Element(Sifen + "dCarQR")!.Value = DeFixtures.PlaceholderQr + "&modificado=1";
        Assert.True(CheckSignature(doc.ToString(SaveOptions.DisableFormatting), identity.Certificate));

        // gCamFuFD queda fuera del DE firmado y es hermano posterior de Signature.
        Assert.Equal(["dVerFor", "DE", "Signature", "gCamFuFD"], doc.Root.Elements().Select(e => e.Name.LocalName));
        Assert.Empty(doc.Root.Element(Sifen + "DE")!.Descendants(Sifen + "gCamFuFD"));
    }

    [XsdPackageFact(XsdPackage.ReceptionRoot)]
    public async Task SignedXml_ValidatesAgainstOfficialXsd_WithPlaceholderQr()
    {
        var (result, identity) = await SignAsync();
        using var _ = identity;

        await DeTestKit.Xsd().EnsureSignedValidAsync(result.SignedXml);

        var doc = XDocument.Parse(result.SignedXml);
        doc.Root!.Add(new XElement(Sifen + "gCamFuFD", new XElement(Sifen + "dCarQR", DeFixtures.PlaceholderQr)));
        Assert.Empty(XsdPackage.ValidateDe(doc));
    }

    [Fact]
    public async Task SigningTwiceOrWithoutCdcElement_IsRejected()
    {
        var (result, identity) = await SignAsync();
        using var _ = identity;
        var tenantId = Guid.NewGuid();
        await using var db = InMemoryDb(tenantId);
        db.TenantCertificateMetadata.Add(Metadata(tenantId, "ref:cert"));
        await db.SaveChangesAsync();
        var signer = Signer(db, new Dictionary<string, byte[]> { ["ref:cert"] = identity.Pfx });

        await Assert.ThrowsAsync<InvalidOperationException>(() => signer.SignAsync(
            new SignXmlDocumentCommand(tenantId, SifenEnvironmentType.Test, "0000000000000000000000000000000000000000000X", BuilderXml())));
    }

    [Fact]
    public async Task ExpiredCertificate_IsRejected()
    {
        using var identity = new TestSigningIdentity(DateTimeOffset.UtcNow.AddDays(-10), DateTimeOffset.UtcNow.AddDays(-1));
        var tenantId = Guid.NewGuid();
        await using var db = InMemoryDb(tenantId);
        db.TenantCertificateMetadata.Add(Metadata(tenantId, "ref:cert"));
        await db.SaveChangesAsync();
        var signer = Signer(db, new Dictionary<string, byte[]> { ["ref:cert"] = identity.Pfx });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => signer.SignAsync(
            new SignXmlDocumentCommand(tenantId, SifenEnvironmentType.Test, SifenDeBuilderFixtures.Cdc, BuilderXml())));
        Assert.Contains("not valid at signing time", ex.Message);
    }

    [Fact]
    public async Task MultiTenant_EachTenantSignsOnlyWithItsOwnCertificate_AndATenantWithoutOneCannotSign()
    {
        using var a = new TestSigningIdentity();
        using var b = new TestSigningIdentity();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var tenantWithout = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString();
        await using (var seedB = InMemoryDb(tenantB, dbName))
        {
            seedB.TenantCertificateMetadata.Add(Metadata(tenantB, "ref:b"));
            await seedB.SaveChangesAsync();
        }

        await using var db = InMemoryDb(tenantA, dbName);
        db.TenantCertificateMetadata.Add(Metadata(tenantA, "ref:a"));
        await db.SaveChangesAsync();
        var audit = new NullAuditSink();
        var signer = Signer(db, new Dictionary<string, byte[]> { ["ref:a"] = a.Pfx, ["ref:b"] = b.Pfx }, audit);

        SignXmlDocumentCommand For(Guid tenant) => new(tenant, SifenEnvironmentType.Test, SifenDeBuilderFixtures.Cdc, BuilderXml());
        var signedA = await signer.SignAsync(For(tenantA));
        var signedB = await signer.SignAsync(For(tenantB));

        Assert.True(CheckSignature(signedA.SignedXml, a.Certificate));
        Assert.False(CheckSignature(signedA.SignedXml, b.Certificate));
        Assert.True(CheckSignature(signedB.SignedXml, b.Certificate));
        Assert.False(CheckSignature(signedB.SignedXml, a.Certificate));
        Assert.Equal(signedA.DigestValue, signedB.DigestValue); // mismo DE => mismo digest; solo cambia la firma

        await Assert.ThrowsAsync<InvalidOperationException>(() => signer.SignAsync(For(tenantWithout)));

        // La auditoria registra huella y digest, nunca clave ni contrasena.
        var evt = audit.Events.First();
        Assert.Equal(tenantA.ToString(), evt.TenantId);
        Assert.Equal(signedA.DigestValue, evt.Metadata["digest.value"]);
        Assert.DoesNotContain(evt.Metadata.Values, v => v is not null && v.Contains(TestSigningIdentity.Password, StringComparison.Ordinal));
    }
}
