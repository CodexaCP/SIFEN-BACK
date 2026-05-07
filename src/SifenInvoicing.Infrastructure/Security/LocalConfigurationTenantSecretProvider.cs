using Microsoft.Extensions.Configuration;
using SifenInvoicing.Application.Security;

namespace SifenInvoicing.Infrastructure.Security;

public sealed class LocalConfigurationTenantSecretProvider : ITenantSecretProvider
{
    private const string ConfigPrefix = "config:";
    private const string EnvPrefix = "env:";
    private const string FilePrefix = "file:";

    private readonly IConfiguration _configuration;

    public LocalConfigurationTenantSecretProvider(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task<SecretCheckResult> CheckStringSecretAsync(
        string secretReference,
        string checkName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var value = await GetStringSecretAsync(secretReference, cancellationToken);
            return string.IsNullOrWhiteSpace(value)
                ? new SecretCheckResult(checkName, SecretStatus.Missing, "Secret value is empty or missing.")
                : new SecretCheckResult(checkName, SecretStatus.Present, "Secret reference resolved.");
        }
        catch (NotSupportedException)
        {
            return new SecretCheckResult(checkName, SecretStatus.UnsupportedReference, "Secret reference scheme is not supported.");
        }
        catch (FileNotFoundException)
        {
            return new SecretCheckResult(checkName, SecretStatus.Missing, "Referenced file secret was not found.");
        }
        catch (Exception ex)
        {
            return new SecretCheckResult(checkName, SecretStatus.Error, $"Secret resolution failed: {ex.GetType().Name}.");
        }
    }

    public async Task<SecretCheckResult> CheckBinarySecretAsync(
        string secretReference,
        string checkName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var value = await GetBinarySecretAsync(secretReference, cancellationToken);
            return value.Length == 0
                ? new SecretCheckResult(checkName, SecretStatus.Missing, "Binary secret value is empty or missing.")
                : new SecretCheckResult(checkName, SecretStatus.Present, "Binary secret reference resolved.");
        }
        catch (NotSupportedException)
        {
            return new SecretCheckResult(checkName, SecretStatus.UnsupportedReference, "Secret reference scheme is not supported for binary secrets.");
        }
        catch (FileNotFoundException)
        {
            return new SecretCheckResult(checkName, SecretStatus.Missing, "Referenced file secret was not found.");
        }
        catch (Exception ex)
        {
            return new SecretCheckResult(checkName, SecretStatus.Error, $"Binary secret resolution failed: {ex.GetType().Name}.");
        }
    }

    public Task<string> GetStringSecretAsync(
        string secretReference,
        CancellationToken cancellationToken = default)
    {
        var normalized = RequireReference(secretReference);

        if (normalized.StartsWith(ConfigPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var key = normalized[ConfigPrefix.Length..];
            return Task.FromResult(_configuration[key] ?? string.Empty);
        }

        if (normalized.StartsWith(EnvPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var key = normalized[EnvPrefix.Length..];
            return Task.FromResult(Environment.GetEnvironmentVariable(key) ?? string.Empty);
        }

        if (normalized.StartsWith(FilePrefix, StringComparison.OrdinalIgnoreCase))
        {
            var path = normalized[FilePrefix.Length..];
            return File.ReadAllTextAsync(path, cancellationToken);
        }

        throw new NotSupportedException("Unsupported secret reference scheme.");
    }

    public async Task<byte[]> GetBinarySecretAsync(
        string secretReference,
        CancellationToken cancellationToken = default)
    {
        var normalized = RequireReference(secretReference);

        if (normalized.StartsWith(FilePrefix, StringComparison.OrdinalIgnoreCase))
        {
            var path = normalized[FilePrefix.Length..];
            return await File.ReadAllBytesAsync(path, cancellationToken);
        }

        if (normalized.StartsWith(ConfigPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var key = normalized[ConfigPrefix.Length..];
            var path = _configuration[key];
            if (string.IsNullOrWhiteSpace(path))
            {
                return [];
            }

            return await File.ReadAllBytesAsync(path, cancellationToken);
        }

        throw new NotSupportedException("Unsupported binary secret reference scheme.");
    }

    private static string RequireReference(string secretReference)
    {
        if (string.IsNullOrWhiteSpace(secretReference))
        {
            throw new InvalidOperationException("Secret reference is required.");
        }

        return secretReference.Trim();
    }
}
