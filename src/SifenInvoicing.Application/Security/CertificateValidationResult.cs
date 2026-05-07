namespace SifenInvoicing.Application.Security;

public sealed record CertificateValidationResult(
    bool IsReady,
    string Summary,
    IReadOnlyCollection<SecretCheckResult> Checks);
