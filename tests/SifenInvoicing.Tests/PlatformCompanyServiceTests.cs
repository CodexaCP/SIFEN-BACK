using Microsoft.EntityFrameworkCore;
using SifenInvoicing.Application.Auditing;
using SifenInvoicing.Application.Auth;
using SifenInvoicing.Application.Diagnostics;
using SifenInvoicing.Application.Platform;
using SifenInvoicing.Application.Tenancy;
using SifenInvoicing.Domain.Common;
using SifenInvoicing.Domain.PlatformAuth;
using SifenInvoicing.Infrastructure.Auth;
using SifenInvoicing.Infrastructure.Diagnostics;
using SifenInvoicing.Infrastructure.Persistence;
using SifenInvoicing.Infrastructure.Platform;
using SifenInvoicing.Infrastructure.Tenancy;

namespace SifenInvoicing.Tests;

public sealed class PlatformCompanyServiceTests
{
    [Fact]
    public async Task CreateCompanyAsync_ShouldPersistTenantWithCommercialPlan()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);

        var created = await service.CreateCompanyAsync(new CreatePlatformCompanyCommand(
            "tramiya",
            "TramiYa",
            "Plan 5 usuarios",
            150,
            5));

        Assert.Equal("tramiya", created.Slug);
        Assert.Equal("TramiYa", created.DisplayName);
        Assert.Equal("Plan 5 usuarios", created.PlanName);
        Assert.Equal(150, created.MaxInvoicesPerMonth);
        Assert.Equal(5, created.MaxUsers);
        Assert.Equal("Active", created.Status);
    }

    [Fact]
    public async Task UpdatePlanAsync_ShouldSuspendTenant_WhenActiveIsFalse()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);
        var created = await service.CreateCompanyAsync(new CreatePlatformCompanyCommand(
            "tramiya-2",
            "TramiYa 2",
            "Plan base",
            150,
            5));

        var updated = await service.UpdatePlanAsync(new UpdatePlatformCompanyPlanCommand(
            created.Id,
            "Plan reducido",
            50,
            2,
            false));

        Assert.Equal("Suspended", updated.Status);
        Assert.Equal("Plan reducido", updated.PlanName);
        Assert.Equal(50, updated.MaxInvoicesPerMonth);
        Assert.Equal(2, updated.MaxUsers);
    }

    [Fact]
    public async Task CreateCompanyAdminAsync_ShouldCreateTenantAdminLinkedToTenant()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);
        var createdCompany = await service.CreateCompanyAsync(new CreatePlatformCompanyCommand(
            "tramiya-3",
            "TramiYa 3",
            "Plan base",
            150,
            5));

        var admin = await service.CreateCompanyAdminAsync(new CreatePlatformCompanyAdminCommand(
            createdCompany.Id,
            "Admin TramiYa",
            "admin@tramiya.local",
            "AdminTramiYa123!"));

        var storedUser = await dbContext.PlatformUsers
            .Include(item => item.Role)
            .SingleAsync(item => item.Id == admin.UserId);

        Assert.Equal(createdCompany.Id, admin.TenantId);
        Assert.Equal("Admin TramiYa", admin.FullName);
        Assert.Equal("TenantAdmin", admin.Role);
        Assert.Equal(createdCompany.Id, storedUser.TenantId);
        Assert.Equal("Admin TramiYa", storedUser.FullName);
        Assert.Equal("TenantAdmin", storedUser.Role!.Name);
    }

    [Fact]
    public async Task CreateCompanyAdminAsync_ShouldFail_WhenEmailAlreadyExists()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);
        var createdCompany = await service.CreateCompanyAsync(new CreatePlatformCompanyCommand(
            "tramiya-4",
            "TramiYa 4",
            "Plan base",
            150,
            5));

        await service.CreateCompanyAdminAsync(new CreatePlatformCompanyAdminCommand(
            createdCompany.Id,
            "Admin TramiYa",
            "admin@tramiya.local",
            "AdminTramiYa123!"));

        var exception = await Assert.ThrowsAsync<DomainException>(() => service.CreateCompanyAdminAsync(
            new CreatePlatformCompanyAdminCommand(
                createdCompany.Id,
                "Admin Repetido",
                "admin@tramiya.local",
                "AnotherPass123!")));

        Assert.Equal("Ya existe un usuario con ese email.", exception.Message);
    }

    [Fact]
    public async Task CreateCompanyAsync_ShouldNotPersistTenant_WhenAdminEmailAlreadyExists()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);
        var existing = await service.CreateCompanyAsync(new CreatePlatformCompanyCommand(
            "tramiya-5",
            "TramiYa 5",
            "Plan base",
            150,
            5,
            "Admin TramiYa",
            "admin@tramiya.local",
            "AdminTramiYa123!"));

        var exception = await Assert.ThrowsAsync<DomainException>(() => service.CreateCompanyAsync(
            new CreatePlatformCompanyCommand(
                "tramiya-6",
                "TramiYa 6",
                "Plan base",
                150,
                5,
                "Admin Repetido",
                "ADMIN@tramiya.local",
                "AnotherPass123!")));

        Assert.Equal("Ya existe un usuario con ese email.", exception.Message);
        Assert.False(await dbContext.Tenants.IgnoreQueryFilters().AnyAsync(item => item.Slug == "tramiya-6"));
        Assert.True(await dbContext.Tenants.IgnoreQueryFilters().AnyAsync(item => item.Id == existing.Id));
    }

    [Fact]
    public async Task CreateCompanyAsync_ShouldNotPersistTenant_WhenAdminPasswordIsMissing()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);

        var exception = await Assert.ThrowsAsync<DomainException>(() => service.CreateCompanyAsync(
            new CreatePlatformCompanyCommand(
                "tramiya-7",
                "TramiYa 7",
                "Plan base",
                150,
                5,
                "Admin TramiYa",
                "admin7@tramiya.local",
                " ")));

        Assert.Equal("La password inicial es obligatoria.", exception.Message);
        Assert.False(await dbContext.Tenants.IgnoreQueryFilters().AnyAsync(item => item.Slug == "tramiya-7"));
    }

    [Fact]
    public async Task CreateCompanyUserAsync_ShouldBlock_WhenActiveUsersReachPlanLimit()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);
        var company = await service.CreateCompanyAsync(new CreatePlatformCompanyCommand(
            "tenant-limit",
            "Tenant Limit",
            "Plan 2 usuarios",
            150,
            2));

        await service.CreateCompanyAdminAsync(new CreatePlatformCompanyAdminCommand(
            company.Id,
            "Admin Principal",
            "admin@tenantlimit.local",
            "Admin123!"));

        await service.CreateCompanyUserAsync(new CreatePlatformTenantUserCommand(
            company.Id,
            "Operador Uno",
            "operador1@tenantlimit.local",
            "Operador123!",
            "Operator",
            true));

        var exception = await Assert.ThrowsAsync<DomainException>(() => service.CreateCompanyUserAsync(
            new CreatePlatformTenantUserCommand(
                company.Id,
                "Viewer Dos",
                "viewer2@tenantlimit.local",
                "Viewer123!",
                "Viewer",
                true)));

        Assert.Equal("Tu plan permite un máximo de 2 usuarios activos. Inactiva un usuario o actualiza tu plan.", exception.Message);
    }

    [Fact]
    public async Task GetCompanyUsersAsync_ShouldReturnOnlyUsersFromRequestedTenant()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);
        var tenantA = await service.CreateCompanyAsync(new CreatePlatformCompanyCommand("tenant-a", "Tenant A", "Plan A", 100, 5));
        var tenantB = await service.CreateCompanyAsync(new CreatePlatformCompanyCommand("tenant-b", "Tenant B", "Plan B", 100, 5));

        await service.CreateCompanyAdminAsync(new CreatePlatformCompanyAdminCommand(tenantA.Id, "Admin A", "admin-a@tenant.local", "Admin123!"));
        await service.CreateCompanyUserAsync(new CreatePlatformTenantUserCommand(tenantA.Id, "Viewer A", "viewer-a@tenant.local", "Viewer123!", "Viewer", true));
        await service.CreateCompanyAdminAsync(new CreatePlatformCompanyAdminCommand(tenantB.Id, "Admin B", "admin-b@tenant.local", "Admin123!"));

        var users = await service.GetCompanyUsersAsync(tenantA.Id);

        Assert.Equal(tenantA.Id, users.TenantId);
        Assert.Equal(2, users.ActiveUsers);
        Assert.All(users.Users, item => Assert.Equal(tenantA.Id, item.TenantId));
        Assert.DoesNotContain(users.Users, item => item.Email == "admin-b@tenant.local");
    }

    [Fact]
    public async Task DeleteCompanyAsync_ShouldRemoveTenantAndItsData_WhenConfirmed()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);
        var keep = await service.CreateCompanyAsync(new CreatePlatformCompanyCommand("keep-co", "Keep", "Plan base", 150, 5));
        var target = await service.CreateCompanyAsync(new CreatePlatformCompanyCommand("drop-co", "Drop", "Plan base", 150, 5));
        dbContext.TaxpayerProfiles.Add(SifenInvoicing.Domain.Tenants.TaxpayerProfile.Create(target.Id, "80012345", "6", "Drop SA"));
        dbContext.TaxpayerProfiles.Add(SifenInvoicing.Domain.Tenants.TaxpayerProfile.Create(keep.Id, "80054321", "1", "Keep SA"));
        await dbContext.SaveChangesAsync();

        await Assert.ThrowsAsync<DomainException>(() =>
            service.DeleteCompanyAsync(new DeletePlatformCompanyCommand(target.Id, "otro", null)));
        await Assert.ThrowsAsync<DomainException>(() =>
            service.DeleteCompanyAsync(new DeletePlatformCompanyCommand(target.Id, "drop-co", target.Id)));

        await service.DeleteCompanyAsync(new DeletePlatformCompanyCommand(target.Id, "drop-co", keep.Id));

        Assert.False(await dbContext.Tenants.AnyAsync(item => item.Id == target.Id));
        Assert.True(await dbContext.Tenants.AnyAsync(item => item.Id == keep.Id));
        var profiles = await dbContext.TaxpayerProfiles.IgnoreQueryFilters().ToListAsync();
        Assert.All(profiles, item => Assert.Equal(keep.Id, item.TenantId));
    }

    private static EfPlatformCompanyService CreateService(SifenDbContext dbContext)
    {
        return new EfPlatformCompanyService(
            dbContext,
            new Pbkdf2PasswordHasher(),
            new NullAuditTrail(),
            new SystemClock());
    }

    private static SifenDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<SifenDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new SifenDbContext(options, new SystemClock(), new AsyncLocalTenantContextAccessor());
    }

    private sealed class NullAuditTrail : IAuditTrail
    {
        public Task RecordAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
