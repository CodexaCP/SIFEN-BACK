using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Xml;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SifenInvoicing.Application.Auditing;
using SifenInvoicing.Application.Diagnostics;
using SifenInvoicing.Application.Security;
using SifenInvoicing.Application.Tenancy;
using SifenInvoicing.Application.XmlSigning;
using SifenInvoicing.Domain.Tenants;
using SifenInvoicing.Infrastructure.Diagnostics;
using SifenInvoicing.Infrastructure.Persistence;
using SifenInvoicing.Infrastructure.Security;
using SifenInvoicing.Infrastructure.Tenancy;
using SifenInvoicing.Infrastructure.XmlSigning;

namespace SifenInvoicing.Tests;

public sealed class SifenXmlDocumentSignerTests
{
    [Fact]
    public async Task SignAsync_ShouldCreateSifenV150Nt16Signature()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=SIFEN XML Signature Test",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddDays(30));
        var password = "test-password";
        var pfxBytes = certificate.Export(X509ContentType.Pfx, password);
        var certificatePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pfx");
        await File.WriteAllBytesAsync(certificatePath, pfxBytes);

        try
        {
            var tenantId = Guid.NewGuid();
            const string documentId = "12345678901234567890123456789012345678901234";
            var dbContext = CreateDbContext();
            dbContext.TenantCertificateMetadata.Add(TenantCertificateMetadata.Create(
                tenantId,
                SifenEnvironmentType.Test,
                CertificatePurpose.XmlSignature,
                "test-signing",
                certificate.Subject,
                Convert.ToHexString(SHA256.HashData(certificate.RawData)),
                certificate.SerialNumber,
                "config:Secrets:CertificatePath",
                "config:Secrets:CertificatePassword",
                DateTimeOffset.UtcNow.AddMinutes(-5),
                DateTimeOffset.UtcNow.AddDays(30)));
            await dbContext.SaveChangesAsync();

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Secrets:CertificatePath"] = certificatePath,
                    ["Secrets:CertificatePassword"] = password
                })
                .Build();
            var signer = new SifenXmlDocumentSigner(
                dbContext,
                new LocalConfigurationTenantSecretProvider(configuration),
                new NoopAuditTrail(),
                new SystemClock());
            var xml = $"<rDE xmlns=\"http://ekuatia.set.gov.py/sifen/xsd\"><dVerFor>150</dVerFor><DE Id=\"{documentId}\"><gDatGralOpe><dTest>OK</dTest></gDatGralOpe></DE></rDE>";

            var result = await signer.SignAsync(new SignXmlDocumentCommand(
                tenantId,
                SifenEnvironmentType.Test,
                documentId,
                xml));

            var document = new XmlDocument { PreserveWhitespace = true };
            document.LoadXml(result.SignedXml);
            var ns = new XmlNamespaceManager(document.NameTable);
            ns.AddNamespace("ds", SignedXml.XmlDsigNamespaceUrl);

            Assert.Equal(SifenXmlSignatureAlgorithms.CanonicalizationMethod, SelectAlgorithm(document, ns, "//ds:CanonicalizationMethod"));
            Assert.Equal(SifenXmlSignatureAlgorithms.SignatureMethod, SelectAlgorithm(document, ns, "//ds:SignatureMethod"));
            Assert.Equal($"#{documentId}", document.SelectSingleNode("//ds:Reference", ns)!.Attributes!["URI"]!.Value);
            Assert.Equal(SifenXmlSignatureAlgorithms.DigestMethod, SelectAlgorithm(document, ns, "//ds:DigestMethod"));

            var transforms = document.SelectNodes("//ds:Reference/ds:Transforms/ds:Transform", ns)!;
            Assert.Equal(1, transforms.Count);
            Assert.Equal(SifenXmlSignatureAlgorithms.EnvelopedSignatureTransform, transforms[0]!.Attributes!["Algorithm"]!.Value);

            Assert.NotNull(document.SelectSingleNode("//ds:KeyInfo/ds:X509Data/ds:X509Certificate", ns));
            Assert.Null(document.SelectSingleNode("//ds:KeyInfo//ds:X509SubjectName", ns));
            Assert.Null(document.SelectSingleNode("//ds:KeyInfo//ds:X509IssuerSerial", ns));
            Assert.Null(document.SelectSingleNode("//ds:KeyInfo//ds:KeyValue", ns));

            var signedXml = new SifenSignedXml(document);
            signedXml.LoadXml((XmlElement)document.GetElementsByTagName("Signature", SignedXml.XmlDsigNamespaceUrl)[0]!);

            Assert.True(signedXml.CheckSignature(certificate, verifySignatureOnly: true));
        }
        finally
        {
            File.Delete(certificatePath);
        }
    }

    private static string SelectAlgorithm(XmlDocument document, XmlNamespaceManager ns, string xpath)
    {
        return document.SelectSingleNode(xpath, ns)!.Attributes!["Algorithm"]!.Value;
    }

    private static SifenDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<SifenDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new SifenDbContext(options, new SystemClock(), new AsyncLocalTenantContextAccessor());
    }

    private sealed class NoopAuditTrail : IAuditTrail
    {
        public Task RecordAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
