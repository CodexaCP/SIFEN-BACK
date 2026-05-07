namespace SifenInvoicing.Application.Auth;

public interface IPlatformAuthService
{
    Task<PlatformLoginResult> LoginAsync(LoginPlatformUserCommand command, CancellationToken cancellationToken = default);

    Task<PlatformSessionUser?> GetByIdAsync(Guid userId, CancellationToken cancellationToken = default);
}
