using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using SifenInvoicing.Application.Auth;
using SifenInvoicing.Domain.Common;
using SifenInvoicing.Infrastructure.Persistence;

namespace SifenInvoicing.Infrastructure.Auth;

public sealed class PlatformAuthService : IPlatformAuthService
{
    private readonly SifenDbContext _dbContext;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IConfiguration _configuration;

    public PlatformAuthService(
        SifenDbContext dbContext,
        IPasswordHasher passwordHasher,
        IConfiguration configuration)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _configuration = configuration;
    }

    public async Task<PlatformLoginResult> LoginAsync(LoginPlatformUserCommand command, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.Username) || string.IsNullOrWhiteSpace(command.Password))
        {
            throw new DomainException("Username and password are required.");
        }

        var normalizedEmail = command.Username.Trim().ToLowerInvariant();
        var user = await _dbContext.PlatformUsers
            .Include(item => item.Role)
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Email == normalizedEmail, cancellationToken);

        if (user is null || !user.IsActive || user.Role is null || !_passwordHasher.Verify(command.Password, user.PasswordHash))
        {
            throw new DomainException("Invalid credentials.");
        }

        return new PlatformLoginResult(CreateToken(user), false);
    }

    public async Task<PlatformSessionUser?> GetByIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.PlatformUsers
            .Include(item => item.Role)
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == userId && item.IsActive, cancellationToken);

        if (user?.Role is null)
        {
            return null;
        }

        var tenantName = user.TenantId.HasValue
            ? await _dbContext.Tenants.AsNoTracking()
                .Where(tenant => tenant.Id == user.TenantId.Value)
                .Select(tenant => tenant.DisplayName)
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        return new PlatformSessionUser(
            user.Id,
            0,
            user.TenantId,
            user.FullName,
            user.Email,
            user.Role.Name,
            user.Role.Permissions,
            tenantName);
    }

    private string CreateToken(Domain.PlatformAuth.PlatformUser user)
    {
        if (user.Role is null)
        {
            throw new InvalidOperationException("Platform role is required to create JWT token.");
        }

        var signingKey = ResolveSigningKey();
        var issuer = _configuration["Auth:Jwt:Issuer"] ?? "SifenInvoicing.Api";
        var audience = _configuration["Auth:Jwt:Audience"] ?? "SifenInvoicing.Frontend";
        var expires = DateTime.UtcNow.AddHours(8);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new("userId", user.Id.ToString()),
            new("companyId", user.TenantId?.ToString() ?? "0"),
            new(ClaimTypes.Role, user.Role.Name),
            new("role", user.Role.Name)
        };

        if (user.TenantId.HasValue)
        {
            claims.Add(new Claim("tenantId", user.TenantId.Value.ToString()));
        }

        claims.AddRange(user.Role.Permissions.Select(permission => new Claim("permission", permission)));

        var credentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expires,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private string ResolveSigningKey()
    {
        var environmentVariableName = _configuration["Auth:Jwt:SigningKeyEnvironmentVariable"] ?? "SIFEN_JWT_SIGNING_KEY";
        var signingKey = Environment.GetEnvironmentVariable(environmentVariableName);

        if (string.IsNullOrWhiteSpace(signingKey))
        {
            throw new InvalidOperationException($"JWT signing key environment variable '{environmentVariableName}' is missing.");
        }

        return signingKey;
    }
}
