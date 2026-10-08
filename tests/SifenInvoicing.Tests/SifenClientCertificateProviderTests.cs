using SifenInvoicing.Domain.Tenants;
using SifenInvoicing.Infrastructure.Sifen;
using SifenInvoicing.Tests.XmlDe;

namespace SifenInvoicing.Tests;

/// <summary>Certificado de transporte por tenant. Usa certificados efimeros en memoria; no representa el certificado real.</summary>
public sealed class SifenClientCertificateProviderTests
{
    private static TenantCertificateMetadata Metadata(Guid tenantId, CertificatePurpose purpose, string reference) =>
        TenantCertificateMetadata.Create(
            tenantId, SifenEnvironmentType.Test, purpose, purpose.ToString(), "CN=PRUEBA", "AB", "01",
            reference, reference + ":pw", DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));

    [Fact]
    public async Task Get_ShouldPreferMutualTls_ThenFallBackToSignatureCertificate_ForTheSameTenantOnly()
    {
        using var mtls = new TestSigningIdentity();
        using var signing = new TestSigningIdentity();
        var tenantId = Guid.NewGuid();
        var otherTenant = Guid.NewGuid();
        await using var db = SigningTestKit.InMemoryDb(tenantId);
        db.TenantCertificateMetadata.Add(Metadata(tenantId, CertificatePurpose.XmlSignature, "ref:sign"));
        await db.SaveChangesAsync();
        var secrets = new MapSecrets(new Dictionary<string, byte[]> { ["ref:sign"] = signing.Pfx, ["ref:mtls"] = mtls.Pfx });
        var provider = new TenantSifenClientCertificateProvider(db, secrets);

        using (var fallback = await provider.GetAsync(tenantId, SifenEnvironmentType.Test))
        {
            Assert.Equal(signing.Certificate.Thumbprint, fallback!.Thumbprint); // MT 7.5: el mismo certificado sirve para ambos usos
        }

        db.TenantCertificateMetadata.Add(Metadata(tenantId, CertificatePurpose.MutualTls, "ref:mtls"));
        await db.SaveChangesAsync();
        using (var preferred = await provider.GetAsync(tenantId, SifenEnvironmentType.Test))
        {
            Assert.Equal(mtls.Certificate.Thumbprint, preferred!.Thumbprint);
        }

        Assert.Null(await provider.GetAsync(otherTenant, SifenEnvironmentType.Test)); // aislamiento por tenant
        Assert.Null(await provider.GetAsync(tenantId, SifenEnvironmentType.Production)); // aislamiento por ambiente
    }
}
