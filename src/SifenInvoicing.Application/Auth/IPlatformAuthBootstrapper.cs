namespace SifenInvoicing.Application.Auth;

public interface IPlatformAuthBootstrapper
{
    Task EnsureSeededAsync(CancellationToken cancellationToken = default);
}
