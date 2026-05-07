namespace SifenInvoicing.Domain.PlatformAuth;

public static class PlatformPermissions
{
    public const string CompaniesReadAll = "companies.read.all";
    public const string CompaniesCreate = "companies.create";
    public const string CompaniesPlanManage = "companies.plan.manage";
    public const string CompaniesUsersManage = "companies.users.manage";
    public const string CompaniesUsersReadOwnTenant = "companies.users.read.own-tenant";
    public const string SifenConfigure = "sifen.configure";
    public const string InvoicesIssueAnyTenant = "invoices.issue.any-tenant";
    public const string InvoicesIssueOwnTenant = "invoices.issue.own-tenant";
    public const string InvoicesReadOwnTenant = "invoices.read.own-tenant";

    public static readonly IReadOnlyCollection<string> SuperAdminPermissions =
    [
        CompaniesReadAll,
        CompaniesCreate,
        CompaniesPlanManage,
        CompaniesUsersManage,
        SifenConfigure,
        InvoicesIssueAnyTenant
    ];

    public static readonly IReadOnlyCollection<string> TenantAdminPermissions =
    [
        CompaniesUsersManage,
        CompaniesUsersReadOwnTenant,
        SifenConfigure,
        InvoicesIssueOwnTenant,
        InvoicesReadOwnTenant
    ];

    public static readonly IReadOnlyCollection<string> OperatorPermissions =
    [
        InvoicesIssueOwnTenant,
        InvoicesReadOwnTenant
    ];

    public static readonly IReadOnlyCollection<string> ViewerPermissions =
    [
        InvoicesReadOwnTenant
    ];
}
