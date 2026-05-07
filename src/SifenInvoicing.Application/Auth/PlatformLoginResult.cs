namespace SifenInvoicing.Application.Auth;

public sealed record PlatformLoginResult(
    string Token,
    bool MustChangePassword);
