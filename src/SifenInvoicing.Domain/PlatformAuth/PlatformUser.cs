using SifenInvoicing.Domain.Common;

namespace SifenInvoicing.Domain.PlatformAuth;

public sealed class PlatformUser : AuditableEntity
{
    private PlatformUser()
    {
        FullName = string.Empty;
        Email = string.Empty;
        PasswordHash = string.Empty;
    }

    private PlatformUser(Guid id, string fullName, string email, string passwordHash, Guid roleId)
        : base(id)
    {
        FullName = Require(fullName, nameof(fullName), 200);
        Email = NormalizeEmail(email);
        PasswordHash = Require(passwordHash, nameof(passwordHash), 4000);
        RoleId = roleId == Guid.Empty ? throw new DomainException("roleId is required.") : roleId;
        IsActive = true;
    }

    public string FullName { get; private set; }

    public string Email { get; private set; }

    public string PasswordHash { get; private set; }

    public Guid RoleId { get; private set; }

    public Guid? TenantId { get; private set; }

    public bool IsActive { get; private set; }

    public PlatformRole? Role { get; private set; }

    public Tenants.Tenant? Tenant { get; private set; }

    public static PlatformUser Create(string fullName, string email, string passwordHash, Guid roleId)
    {
        return new PlatformUser(Guid.NewGuid(), fullName, email, passwordHash, roleId);
    }

    public static PlatformUser CreateTenantUser(string fullName, string email, string passwordHash, Guid roleId, Guid tenantId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new DomainException("tenantId is required.");
        }

        return new PlatformUser(Guid.NewGuid(), fullName, email, passwordHash, roleId)
        {
            TenantId = tenantId
        };
    }

    public void UpdateProfile(string fullName, string email, Guid roleId)
    {
        FullName = Require(fullName, nameof(fullName), 200);
        Email = NormalizeEmail(email);
        RoleId = roleId == Guid.Empty ? throw new DomainException("roleId is required.") : roleId;
    }

    public void Activate()
    {
        IsActive = true;
    }

    public void Deactivate()
    {
        IsActive = false;
    }

    private static string NormalizeEmail(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException("email is required.");
        }

        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.Length > 200)
        {
            throw new DomainException("email exceeds max length 200.");
        }

        return normalized;
    }

    private static string Require(string value, string parameterName, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException($"{parameterName} is required.");
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new DomainException($"{parameterName} exceeds max length {maxLength}.");
        }

        return trimmed;
    }
}
