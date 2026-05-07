using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SifenInvoicing.Application.Auth;
using SifenInvoicing.Application.Diagnostics;
using SifenInvoicing.Application.Tenancy;
using SifenInvoicing.Domain.PlatformAuth;
using SifenInvoicing.Infrastructure.Auth;
using SifenInvoicing.Infrastructure.Diagnostics;
using SifenInvoicing.Infrastructure.Persistence;
using SifenInvoicing.Infrastructure.Tenancy;

namespace SifenInvoicing.Tests;

public sealed class PlatformAuthTests
{
    [Fact]
    public async Task EnsureSeededAsync_ShouldCreateRoleAndUser_WhenPasswordVariableIsPresent()
    {
        const string passwordVariable = "SIFEN_SUPERADMIN_PASSWORD_TEST";
        const string signingKeyVariable = "SIFEN_JWT_SIGNING_KEY_TEST";
        Environment.SetEnvironmentVariable(passwordVariable, "Sup3rAdmin!");
        Environment.SetEnvironmentVariable(signingKeyVariable, "this-is-a-long-test-signing-key-for-sifen-auth");

        try
        {
            var tenantAccessor = new AsyncLocalTenantContextAccessor();
            await using var dbContext = CreateDbContext(tenantAccessor);
            var configuration = CreateConfiguration(passwordVariable, signingKeyVariable);
            var hasher = new Pbkdf2PasswordHasher();
            var bootstrapper = new PlatformAuthBootstrapper(dbContext, configuration, hasher, NullLogger<PlatformAuthBootstrapper>.Instance);

            await bootstrapper.EnsureSeededAsync();

            var role = await dbContext.PlatformRoles.SingleAsync(item => item.Name == "SuperAdmin");
            var user = await dbContext.PlatformUsers.SingleAsync();

            Assert.Equal("SuperAdmin", role.Name);
            Assert.Equal("SuperAdmin Codexa", user.FullName);
            Assert.Equal("admin@sifen.local", user.Email);
            Assert.True(hasher.Verify("Sup3rAdmin!", user.PasswordHash));
        }
        finally
        {
            Environment.SetEnvironmentVariable(passwordVariable, null);
            Environment.SetEnvironmentVariable(signingKeyVariable, null);
        }
    }

    [Fact]
    public async Task EnsureSeededAsync_ShouldNotDuplicateUser_WhenRunTwice()
    {
        const string passwordVariable = "SIFEN_SUPERADMIN_PASSWORD_TEST";
        const string signingKeyVariable = "SIFEN_JWT_SIGNING_KEY_TEST";
        Environment.SetEnvironmentVariable(passwordVariable, "Sup3rAdmin!");
        Environment.SetEnvironmentVariable(signingKeyVariable, "this-is-a-long-test-signing-key-for-sifen-auth");

        try
        {
            var tenantAccessor = new AsyncLocalTenantContextAccessor();
            await using var dbContext = CreateDbContext(tenantAccessor);
            var configuration = CreateConfiguration(passwordVariable, signingKeyVariable);
            var bootstrapper = new PlatformAuthBootstrapper(dbContext, configuration, new Pbkdf2PasswordHasher(), NullLogger<PlatformAuthBootstrapper>.Instance);

            await bootstrapper.EnsureSeededAsync();
            await bootstrapper.EnsureSeededAsync();

            Assert.Equal(4, await dbContext.PlatformRoles.CountAsync());
            Assert.Equal(1, await dbContext.PlatformUsers.CountAsync());
        }
        finally
        {
            Environment.SetEnvironmentVariable(passwordVariable, null);
            Environment.SetEnvironmentVariable(signingKeyVariable, null);
        }
    }

    [Fact]
    public async Task LoginAsync_ShouldReturnJwt_WhenCredentialsAreValid()
    {
        const string passwordVariable = "SIFEN_SUPERADMIN_PASSWORD_TEST";
        const string signingKeyVariable = "SIFEN_JWT_SIGNING_KEY_TEST";
        Environment.SetEnvironmentVariable(passwordVariable, "Sup3rAdmin!");
        Environment.SetEnvironmentVariable(signingKeyVariable, "this-is-a-long-test-signing-key-for-sifen-auth");

        try
        {
            var tenantAccessor = new AsyncLocalTenantContextAccessor();
            await using var dbContext = CreateDbContext(tenantAccessor);
            var configuration = CreateConfiguration(passwordVariable, signingKeyVariable);
            var hasher = new Pbkdf2PasswordHasher();
            var bootstrapper = new PlatformAuthBootstrapper(dbContext, configuration, hasher, NullLogger<PlatformAuthBootstrapper>.Instance);
            await bootstrapper.EnsureSeededAsync();

            var authService = new PlatformAuthService(dbContext, hasher, configuration);
            var result = await authService.LoginAsync(new LoginPlatformUserCommand("admin@sifen.local", "Sup3rAdmin!"));

            Assert.False(string.IsNullOrWhiteSpace(result.Token));
            Assert.False(result.MustChangePassword);
        }
        finally
        {
            Environment.SetEnvironmentVariable(passwordVariable, null);
            Environment.SetEnvironmentVariable(signingKeyVariable, null);
        }
    }

    [Fact]
    public async Task LoginAsync_ShouldFail_WhenPasswordIsInvalid()
    {
        const string passwordVariable = "SIFEN_SUPERADMIN_PASSWORD_TEST";
        const string signingKeyVariable = "SIFEN_JWT_SIGNING_KEY_TEST";
        Environment.SetEnvironmentVariable(passwordVariable, "Sup3rAdmin!");
        Environment.SetEnvironmentVariable(signingKeyVariable, "this-is-a-long-test-signing-key-for-sifen-auth");

        try
        {
            var tenantAccessor = new AsyncLocalTenantContextAccessor();
            await using var dbContext = CreateDbContext(tenantAccessor);
            var configuration = CreateConfiguration(passwordVariable, signingKeyVariable);
            var hasher = new Pbkdf2PasswordHasher();
            var bootstrapper = new PlatformAuthBootstrapper(dbContext, configuration, hasher, NullLogger<PlatformAuthBootstrapper>.Instance);
            await bootstrapper.EnsureSeededAsync();

            var authService = new PlatformAuthService(dbContext, hasher, configuration);

            await Assert.ThrowsAsync<SifenInvoicing.Domain.Common.DomainException>(() =>
                authService.LoginAsync(new LoginPlatformUserCommand("admin@sifen.local", "wrong-password")));
        }
        finally
        {
            Environment.SetEnvironmentVariable(passwordVariable, null);
            Environment.SetEnvironmentVariable(signingKeyVariable, null);
        }
    }

    private static IConfiguration CreateConfiguration(string passwordVariable, string signingKeyVariable)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:Jwt:Issuer"] = "SifenInvoicing.Api",
                ["Auth:Jwt:Audience"] = "SifenInvoicing.Frontend",
                ["Auth:Jwt:SigningKeyEnvironmentVariable"] = signingKeyVariable,
                ["Auth:Bootstrap:SuperAdminEmail"] = "admin@sifen.local",
                ["Auth:Bootstrap:SuperAdminPasswordEnvironmentVariable"] = passwordVariable
            })
            .Build();
    }

    private static SifenDbContext CreateDbContext(ITenantContextAccessor tenantAccessor)
    {
        var options = new DbContextOptionsBuilder<SifenDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new SifenDbContext(options, new SystemClock(), tenantAccessor);
    }
}
