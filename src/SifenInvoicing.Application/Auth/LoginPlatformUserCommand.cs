namespace SifenInvoicing.Application.Auth;

public sealed record LoginPlatformUserCommand(
    string Username,
    string Password);
