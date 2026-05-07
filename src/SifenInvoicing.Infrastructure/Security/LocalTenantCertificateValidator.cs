using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using SifenInvoicing.Application.Security;
using SifenInvoicing.Domain.Tenants;

namespace SifenInvoicing.Infrastructure.Security;

public sealed class LocalTenantCertificateValidator : ITenantCertificateValidator
{
    private readonly ITenantSecretProvider _secretProvider;

    public LocalTenantCertificateValidator(ITenantSecretProvider secretProvider)
    {
        _secretProvider = secretProvider;
    }

    public async Task<CertificateValidationResult> ValidateAsync(
        TenantCertificateMetadata? metadata,
        CancellationToken cancellationToken = default)
    {
        if (metadata is null)
        {
            return new CertificateValidationResult(
                false,
                "Certificate metadata is missing.",
                [new SecretCheckResult("certificate.metadata", SecretStatus.Missing, "Certificate metadata is missing.")]);
        }

        var pfxCheck = await _secretProvider.CheckBinarySecretAsync(
            metadata.CertificateSecretReference,
            "certificate.pfx",
            cancellationToken);
        var passwordCheck = await _secretProvider.CheckStringSecretAsync(
            metadata.CertificatePasswordSecretReference,
            "certificate.password",
            cancellationToken);
        var checks = new List<SecretCheckResult> { pfxCheck, passwordCheck };

        if (!pfxCheck.IsReady || !passwordCheck.IsReady)
        {
            return new CertificateValidationResult(false, "Certificate secret references are not ready.", checks);
        }

        try
        {
            var pfxBytes = await _secretProvider.GetBinarySecretAsync(metadata.CertificateSecretReference, cancellationToken);
            var password = await _secretProvider.GetStringSecretAsync(metadata.CertificatePasswordSecretReference, cancellationToken);

            using var certificate = new X509Certificate2(
                pfxBytes,
                password,
                X509KeyStorageFlags.EphemeralKeySet);

            checks.Add(new SecretCheckResult(
                "certificate.private_key",
                certificate.HasPrivateKey ? SecretStatus.Present : SecretStatus.Missing,
                certificate.HasPrivateKey ? "Certificate has a private key." : "Certificate does not have a private key."));

            var fingerprintMatches = MatchesSha256Fingerprint(certificate, metadata.FingerprintSha256);
            checks.Add(new SecretCheckResult(
                "certificate.fingerprint_sha256",
                fingerprintMatches ? SecretStatus.Present : SecretStatus.Error,
                fingerprintMatches ? "Certificate SHA-256 fingerprint matches metadata." : "Certificate SHA-256 fingerprint does not match metadata."));

            var now = DateTimeOffset.UtcNow;
            var isTimeValid = new DateTimeOffset(certificate.NotBefore) <= now &&
                              new DateTimeOffset(certificate.NotAfter) > now;
            checks.Add(new SecretCheckResult(
                "certificate.validity",
                isTimeValid ? SecretStatus.Present : SecretStatus.Error,
                isTimeValid ? "Certificate is currently valid." : "Certificate is expired or not yet valid."));

            var isReady = checks.All(check => check.IsReady);
            return new CertificateValidationResult(
                isReady,
                isReady ? "Certificate loaded and validated." : "Certificate loaded but failed validation checks.",
                checks);
        }
        catch (CryptographicException)
        {
            checks.Add(new SecretCheckResult("certificate.load", SecretStatus.Error, "Certificate could not be loaded. Check PFX and password."));
        }
        catch (Exception ex)
        {
            checks.Add(new SecretCheckResult("certificate.load", SecretStatus.Error, $"Certificate validation failed: {ex.GetType().Name}."));
        }

        return new CertificateValidationResult(false, "Certificate could not be loaded or validated.", checks);
    }

    private static bool MatchesSha256Fingerprint(X509Certificate2 certificate, string expectedFingerprint)
    {
        var normalizedExpected = NormalizeFingerprint(expectedFingerprint);
        var actual = Convert.ToHexString(SHA256.HashData(certificate.RawData));

        return string.Equals(actual, normalizedExpected, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeFingerprint(string value)
    {
        return value
            .Replace(":", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Trim()
            .ToUpperInvariant();
    }
}
