using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Configuration;
using SifenInvoicing.Domain.Tenants;
using SifenInvoicing.Infrastructure.Security;

namespace SifenInvoicing.Tests;

public sealed class LocalTenantCertificateValidatorTests
{
    [Fact]
    public async Task ValidateAsync_ShouldLoadPfxAndValidateFingerprint()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=SIFEN Test Certificate",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddDays(30));
        var password = "test-password";
        var pfx = certificate.Export(X509ContentType.Pfx, password);
        var certificatePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pfx");
        await File.WriteAllBytesAsync(certificatePath, pfx);

        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Secrets:CertificatePath"] = certificatePath,
                    ["Secrets:CertificatePassword"] = password
                })
                .Build();
            var provider = new LocalConfigurationTenantSecretProvider(configuration);
            var validator = new LocalTenantCertificateValidator(provider);
            var metadata = TenantCertificateMetadata.Create(
                Guid.NewGuid(),
                SifenEnvironmentType.Test,
                CertificatePurpose.XmlSignature,
                "test-signing",
                certificate.Subject,
                Convert.ToHexString(SHA256.HashData(certificate.RawData)),
                certificate.SerialNumber,
                "config:Secrets:CertificatePath",
                "config:Secrets:CertificatePassword",
                DateTimeOffset.UtcNow.AddMinutes(-5),
                DateTimeOffset.UtcNow.AddDays(30));

            var result = await validator.ValidateAsync(metadata);

            Assert.True(result.IsReady);
        }
        finally
        {
            File.Delete(certificatePath);
        }
    }
}
