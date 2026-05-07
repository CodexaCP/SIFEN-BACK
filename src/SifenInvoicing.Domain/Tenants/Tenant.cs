using SifenInvoicing.Domain.Common;

namespace SifenInvoicing.Domain.Tenants;

public sealed class Tenant : AuditableEntity
{
    private Tenant()
    {
        Slug = string.Empty;
        DisplayName = string.Empty;
        PlanName = string.Empty;
    }

    private Tenant(
        Guid id,
        string slug,
        string displayName,
        string planName,
        TenantIsolationMode isolationMode,
        int? maxInvoicesPerMonth,
        int? maxUsers)
        : base(id)
    {
        Slug = RequireValue(slug, nameof(slug));
        DisplayName = RequireValue(displayName, nameof(displayName));
        PlanName = RequireValue(planName, nameof(planName));
        IsolationMode = isolationMode;
        MaxInvoicesPerMonth = NormalizeLimit(maxInvoicesPerMonth, nameof(maxInvoicesPerMonth));
        MaxUsers = NormalizeLimit(maxUsers, nameof(maxUsers));
        Status = TenantStatus.Active;
    }

    public string Slug { get; private set; }

    public string DisplayName { get; private set; }

    public string PlanName { get; private set; }

    public TenantStatus Status { get; private set; }

    public TenantIsolationMode IsolationMode { get; private set; }

    public int? MaxInvoicesPerMonth { get; private set; }

    public int? MaxUsers { get; private set; }

    public static Tenant CreateSharedDatabaseTenant(
        string slug,
        string displayName,
        string? planName = null,
        int? maxInvoicesPerMonth = null,
        int? maxUsers = null)
    {
        return new Tenant(
            Guid.NewGuid(),
            slug,
            displayName,
            string.IsNullOrWhiteSpace(planName) ? "Plan base" : planName.Trim(),
            TenantIsolationMode.SharedDatabase,
            maxInvoicesPerMonth,
            maxUsers);
    }

    public void ConfigureCommercialPlan(string? planName, int? maxInvoicesPerMonth, int? maxUsers)
    {
        PlanName = string.IsNullOrWhiteSpace(planName) ? PlanName : RequireValue(planName, nameof(planName));
        MaxInvoicesPerMonth = NormalizeLimit(maxInvoicesPerMonth, nameof(maxInvoicesPerMonth));
        MaxUsers = NormalizeLimit(maxUsers, nameof(maxUsers));
    }

    public void Suspend()
    {
        Status = TenantStatus.Suspended;
    }

    public void Activate()
    {
        Status = TenantStatus.Active;
    }

    private static string RequireValue(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException($"{parameterName} is required.");
        }

        return value.Trim();
    }

    private static int? NormalizeLimit(int? value, string parameterName)
    {
        if (!value.HasValue)
        {
            return null;
        }

        if (value.Value <= 0)
        {
            throw new DomainException($"{parameterName} must be greater than zero when configured.");
        }

        return value.Value;
    }
}
