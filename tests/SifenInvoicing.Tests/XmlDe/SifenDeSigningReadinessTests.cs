using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Xml;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using SifenInvoicing.Application.Auditing;
using SifenInvoicing.Application.Security;
using SifenInvoicing.Application.XmlDe;
using SifenInvoicing.Application.XmlSigning;
using SifenInvoicing.Domain.Tenants;
using SifenInvoicing.Infrastructure.Diagnostics;
using SifenInvoicing.Infrastructure.Persistence;
using SifenInvoicing.Infrastructure.Tenancy;
using SifenInvoicing.Application.Tenancy;
using SifenInvoicing.Infrastructure.XmlSigning;

namespace SifenInvoicing.Tests.XmlDe;

/// <summary>
/// Fase 4.3 - preparacion de firma (NO firma real): el XML del builder se firma con el SifenXmlDocumentSigner existente
/// usando un certificado AUTOFIRMADO EFIMERO generado en la prueba (sin valor, sin vinculo con ningun contribuyente).
/// Comprueba la posicion de Signature segun el XSD, que gCamFuFD queda fuera de DE y que la firma no altera el contenido de DE.
/// </summary>
public sealed class SifenDeSigningReadinessTests
{
    private static readonly XNamespace Ns = DeReferenceStructure.Namespace;

    [XsdPackageFact(XsdPackage.ReceptionRoot)]
    public async Task BuilderXml_CanBeSignedWithoutAlteringDE_AndSignatureIsInXsdPosition()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=PRUEBA-EFIMERO", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        var pfx = certificate.Export(X509ContentType.Pfx, "pw");

        var tenantId = Guid.NewGuid();
        var accessor = new AsyncLocalTenantContextAccessor();
        accessor.SetCurrent(new TenantContext { TenantId = tenantId.ToString(), ResolvedTenantId = tenantId, IsResolved = true });
        await using var db = new SifenDbContext(
            new DbContextOptionsBuilder<SifenDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new SystemClock(), accessor);
        db.TenantCertificateMetadata.Add(TenantCertificateMetadata.Create(
            tenantId, SifenEnvironmentType.Test, CertificatePurpose.XmlSignature, "xml-signing", "CN=PRUEBA-EFIMERO", "AB", "01",
            "ref:cert", "ref:pw", DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1)));
        await db.SaveChangesAsync();

        var xml = new SifenDeXmlBuilder(new SifenDeBuilderOptions(DSisFactMode.EmitContributorSystem))
            .Build(SifenDeBuilderFixtures.Input()).Xml;
        var beforeDe = XDocument.Parse(xml).Root!.Element(Ns + "DE")!.ToString(SaveOptions.DisableFormatting);

        var signer = new SifenXmlDocumentSigner(db, new EphemeralSecrets(pfx), new NullAudit(), new SystemClock());
        var signed = await signer.SignAsync(new SignXmlDocumentCommand(tenantId, SifenEnvironmentType.Test, SifenDeBuilderFixtures.Cdc, xml));

        var signedDoc = XDocument.Parse(signed.SignedXml);
        var children = signedDoc.Root!.Elements().Select(e => e.Name.LocalName).ToList();
        // Orden de rDE segun el XSD: dVerFor, DE, Signature (hermana posterior de DE), gCamFuFD (despues de Signature, fuera de DE).
        Assert.Equal(["dVerFor", "DE", "Signature"], children);
        Assert.Equal(beforeDe, signedDoc.Root.Element(Ns + "DE")!.ToString(SaveOptions.DisableFormatting));
        Assert.Null(signedDoc.Root.Element(Ns + "DE")!.Descendants().FirstOrDefault(e => e.Name.LocalName == "Signature"));

        // La firma verifica y referencia #CDC.
        var domDoc = new XmlDocument { PreserveWhitespace = true };
        domDoc.LoadXml(signed.SignedXml);
        var signedXml = new SifenSignedXml(domDoc);
        var signatureNode = (XmlElement)domDoc.GetElementsByTagName("Signature", SignedXml.XmlDsigNamespaceUrl)[0]!;
        signedXml.LoadXml(signatureNode);
        Assert.Equal("#" + SifenDeBuilderFixtures.Cdc, signedXml.SignedInfo!.References.Cast<Reference>().Single().Uri);
        Assert.True(signedXml.CheckSignature(certificate, true));

        // Con gCamFuFD (QR de relleno) la estructura completa valida contra el XSD oficial local.
        signedDoc.Root.Add(new XElement(Ns + "gCamFuFD", new XElement(Ns + "dCarQR", DeFixtures.PlaceholderQr)));
        Assert.Empty(XsdPackage.ValidateDe(signedDoc));
    }

    private sealed class EphemeralSecrets(byte[] pfx) : ITenantSecretProvider
    {
        public Task<SecretCheckResult> CheckStringSecretAsync(string secretReference, string checkName, CancellationToken cancellationToken = default)
            => Task.FromResult(new SecretCheckResult(checkName, SecretStatus.Present, "OK"));

        public Task<SecretCheckResult> CheckBinarySecretAsync(string secretReference, string checkName, CancellationToken cancellationToken = default)
            => Task.FromResult(new SecretCheckResult(checkName, SecretStatus.Present, "OK"));

        public Task<string> GetStringSecretAsync(string secretReference, CancellationToken cancellationToken = default) => Task.FromResult("pw");

        public Task<byte[]> GetBinarySecretAsync(string secretReference, CancellationToken cancellationToken = default) => Task.FromResult(pfx);
    }

    private sealed class NullAudit : IAuditTrail
    {
        public Task RecordAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
