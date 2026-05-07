namespace SifenInvoicing.Application.Security;

public interface ITenantSecretProvider
{
    Task<SecretCheckResult> CheckStringSecretAsync(
        string secretReference,
        string checkName,
        CancellationToken cancellationToken = default);

    Task<SecretCheckResult> CheckBinarySecretAsync(
        string secretReference,
        string checkName,
        CancellationToken cancellationToken = default);

    Task<string> GetStringSecretAsync(
        string secretReference,
        CancellationToken cancellationToken = default);

    Task<byte[]> GetBinarySecretAsync(
        string secretReference,
        CancellationToken cancellationToken = default);
}
