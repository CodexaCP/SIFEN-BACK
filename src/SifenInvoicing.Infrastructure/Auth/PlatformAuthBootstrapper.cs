using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SifenInvoicing.Application.Auth;
using SifenInvoicing.Domain.PlatformAuth;
using SifenInvoicing.Infrastructure.Persistence;

namespace SifenInvoicing.Infrastructure.Auth;

public sealed class PlatformAuthBootstrapper : IPlatformAuthBootstrapper
{
    private readonly SifenDbContext _dbContext;
    private readonly IConfiguration _configuration;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ILogger<PlatformAuthBootstrapper> _logger;

    public PlatformAuthBootstrapper(
        SifenDbContext dbContext,
        IConfiguration configuration,
        IPasswordHasher passwordHasher,
        ILogger<PlatformAuthBootstrapper> logger)
    {
        _dbContext = dbContext;
        _configuration = configuration;
        _passwordHasher = passwordHasher;
        _logger = logger;
    }

    public async Task EnsureSeededAsync(CancellationToken cancellationToken = default)
    {
        var superAdminRole = await EnsureRoleAsync("SuperAdmin", PlatformRole.CreateSuperAdmin, cancellationToken);
        await EnsureRoleAsync("TenantAdmin", PlatformRole.CreateTenantAdmin, cancellationToken);
        await EnsureRoleAsync("Operator", PlatformRole.CreateOperator, cancellationToken);
        await EnsureRoleAsync("Viewer", PlatformRole.CreateViewer, cancellationToken);

        var email = (_configuration["Auth:Bootstrap:SuperAdminEmail"] ?? "admin@sifen.local").Trim().ToLowerInvariant();
        var passwordVariable = _configuration["Auth:Bootstrap:SuperAdminPasswordEnvironmentVariable"] ?? "SIFEN_SUPERADMIN_PASSWORD";
        var password = Environment.GetEnvironmentVariable(passwordVariable);

        if (string.IsNullOrWhiteSpace(password))
        {
            _logger.LogWarning("Platform bootstrap skipped for {Email}. Environment variable {PasswordVariable} is missing.", email, passwordVariable);
            return;
        }

        var userExists = await _dbContext.PlatformUsers.AnyAsync(item => item.Email == email, cancellationToken);
        if (userExists)
        {
            _logger.LogInformation("Platform bootstrap user {Email} already exists. No duplicate created.", email);
            return;
        }

        var user = Domain.PlatformAuth.PlatformUser.Create("SuperAdmin Codexa", email, _passwordHasher.Hash(password), superAdminRole.Id);
        _dbContext.PlatformUsers.Add(user);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Platform bootstrap user {Email} created with role SuperAdmin.", email);
    }

    private async Task<Domain.PlatformAuth.PlatformRole> EnsureRoleAsync(
        string roleName,
        Func<Domain.PlatformAuth.PlatformRole> factory,
        CancellationToken cancellationToken)
    {
        var role = await _dbContext.PlatformRoles.FirstOrDefaultAsync(item => item.Name == roleName, cancellationToken);
        if (role is not null)
        {
            return role;
        }

        role = factory();
        _dbContext.PlatformRoles.Add(role);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Platform role {RoleName} created.", roleName);
        return role;
    }
}
