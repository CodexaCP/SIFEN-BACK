namespace SifenInvoicing.Application.Platform;

public interface IPlatformCompanyService
{
    Task<IReadOnlyCollection<PlatformCompanySummary>> GetCompaniesAsync(CancellationToken cancellationToken = default);

    Task<PlatformCompanySummary?> GetCompanyByIdAsync(Guid tenantId, CancellationToken cancellationToken = default);

    Task<PlatformCompanySummary> CreateCompanyAsync(CreatePlatformCompanyCommand command, CancellationToken cancellationToken = default);

    Task<PlatformCompanySummary> UpdatePlanAsync(UpdatePlatformCompanyPlanCommand command, CancellationToken cancellationToken = default);

    Task<PlatformCompanyAdminResult> CreateCompanyAdminAsync(CreatePlatformCompanyAdminCommand command, CancellationToken cancellationToken = default);

    Task<PlatformCompanyUsersResult> GetCompanyUsersAsync(Guid tenantId, CancellationToken cancellationToken = default);

    Task<PlatformCompanyUserSummary> CreateCompanyUserAsync(CreatePlatformTenantUserCommand command, CancellationToken cancellationToken = default);

    Task<PlatformCompanyUserSummary> UpdateCompanyUserAsync(UpdatePlatformTenantUserCommand command, CancellationToken cancellationToken = default);
}
