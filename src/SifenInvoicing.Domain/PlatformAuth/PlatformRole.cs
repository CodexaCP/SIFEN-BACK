using SifenInvoicing.Domain.Common;

namespace SifenInvoicing.Domain.PlatformAuth;

public sealed class PlatformRole : AuditableEntity
{
    private PlatformRole()
    {
        Name = string.Empty;
        Description = string.Empty;
        Permissions = [];
    }

    private PlatformRole(Guid id, string name, string description, IReadOnlyCollection<string> permissions)
        : base(id)
    {
        Name = Require(name, nameof(name), 100);
        Description = Require(description, nameof(description), 250);
        Permissions = NormalizePermissions(permissions);
    }

    public string Name { get; private set; }

    public string Description { get; private set; }

    public IReadOnlyCollection<string> Permissions { get; private set; }

    public static PlatformRole CreateSuperAdmin()
    {
        return new PlatformRole(
            Guid.NewGuid(),
            "SuperAdmin",
            "Global FE operator with cross-tenant access.",
            PlatformPermissions.SuperAdminPermissions);
    }

    public static PlatformRole CreateTenantAdmin()
    {
        return new PlatformRole(
            Guid.NewGuid(),
            "TenantAdmin",
            "Tenant FE operator with company-scoped access.",
            PlatformPermissions.TenantAdminPermissions);
    }

    public static PlatformRole CreateOperator()
    {
        return new PlatformRole(
            Guid.NewGuid(),
            "Operator",
            "Tenant operator with invoice issuance access.",
            PlatformPermissions.OperatorPermissions);
    }

    public static PlatformRole CreateViewer()
    {
        return new PlatformRole(
            Guid.NewGuid(),
            "Viewer",
            "Tenant user with read-only invoice access.",
            PlatformPermissions.ViewerPermissions);
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

    private static IReadOnlyCollection<string> NormalizePermissions(IReadOnlyCollection<string> permissions)
    {
        if (permissions is null || permissions.Count == 0)
        {
            throw new DomainException("permissions are required.");
        }

        return permissions
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
