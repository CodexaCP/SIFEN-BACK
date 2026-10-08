using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SifenInvoicing.Application.Auditing;
using SifenInvoicing.Application.Auth;
using SifenInvoicing.Application.Diagnostics;
using SifenInvoicing.Application.Platform;
using SifenInvoicing.Domain.Common;
using SifenInvoicing.Domain.PlatformAuth;
using SifenInvoicing.Domain.Tenants;
using SifenInvoicing.Infrastructure.Persistence;

namespace SifenInvoicing.Infrastructure.Platform;

public sealed class EfPlatformCompanyService : IPlatformCompanyService
{
    private readonly SifenDbContext _dbContext;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IAuditTrail _auditTrail;
    private readonly ISystemClock _clock;

    public EfPlatformCompanyService(
        SifenDbContext dbContext,
        IPasswordHasher passwordHasher,
        IAuditTrail auditTrail,
        ISystemClock clock)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _auditTrail = auditTrail;
        _clock = clock;
    }

    public async Task<IReadOnlyCollection<PlatformCompanySummary>> GetCompaniesAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.Tenants
            .AsNoTracking()
            .IgnoreQueryFilters()
            .OrderBy(item => item.DisplayName)
            .Select(item => MapSummary(item))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<PlatformCompanySummary?> GetCompanyByIdAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Tenants
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(item => item.Id == tenantId)
            .Select(item => MapSummary(item))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<PlatformCompanySummary> CreateCompanyAsync(CreatePlatformCompanyCommand command, CancellationToken cancellationToken = default)
    {
        var slug = NormalizeSlug(command.Slug);
        var exists = await _dbContext.Tenants.IgnoreQueryFilters().AnyAsync(item => item.Slug == slug, cancellationToken);
        if (exists)
        {
            throw new DomainException($"Tenant slug '{slug}' already exists.");
        }

        // Validar el admin antes de persistir nada: la empresa y su admin se crean juntos o no se crea ninguno.
        if (!string.IsNullOrWhiteSpace(command.AdminEmail))
        {
            if (string.IsNullOrWhiteSpace(command.AdminPassword))
            {
                throw new DomainException("La password inicial es obligatoria.");
            }

            var adminEmail = NormalizeEmail(command.AdminEmail);
            var adminEmailExists = await _dbContext.PlatformUsers.IgnoreQueryFilters().AnyAsync(item => item.Email == adminEmail, cancellationToken);
            if (adminEmailExists)
            {
                throw new DomainException("Ya existe un usuario con ese email.");
            }
        }

        // (El proveedor InMemory de pruebas no soporta transacciones.)
        await using IDbContextTransaction? transaction = _dbContext.Database.IsRelational()
            ? await _dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

        var tenant = Tenant.CreateSharedDatabaseTenant(
            slug,
            command.DisplayName,
            command.PlanName,
            command.MaxInvoicesPerMonth,
            command.MaxUsers);

        _dbContext.Tenants.Add(tenant);
        await _dbContext.SaveChangesAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(command.AdminEmail))
        {
            await CreateCompanyAdminAsync(
                new CreatePlatformCompanyAdminCommand(
                    tenant.Id,
                    command.AdminFullName ?? "Admin principal",
                    command.AdminEmail,
                    command.AdminPassword ?? string.Empty),
                cancellationToken);
        }

        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        await RecordAuditAsync("platform.company.created", tenant.Id, "Created", cancellationToken);
        return MapSummary(tenant);
    }

    public async Task<PlatformCompanySummary> UpdatePlanAsync(UpdatePlatformCompanyPlanCommand command, CancellationToken cancellationToken = default)
    {
        var tenant = await _dbContext.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(item => item.Id == command.TenantId, cancellationToken);
        if (tenant is null)
        {
            throw new DomainException("Tenant not found.");
        }

        tenant.ConfigureCommercialPlan(command.PlanName, command.MaxInvoicesPerMonth, command.MaxUsers);
        if (command.Active == true)
        {
            tenant.Activate();
        }
        else if (command.Active == false)
        {
            tenant.Suspend();
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await RecordAuditAsync("platform.company.plan.updated", tenant.Id, "Updated", cancellationToken);
        return MapSummary(tenant);
    }

    public async Task<PlatformCompanyAdminResult> CreateCompanyAdminAsync(CreatePlatformCompanyAdminCommand command, CancellationToken cancellationToken = default)
    {
        var user = await CreateTenantUserInternalAsync(
            command.TenantId,
            command.FullName,
            command.Email,
            command.Password,
            "TenantAdmin",
            true,
            cancellationToken);

        return new PlatformCompanyAdminResult(user.UserId, user.TenantId, user.FullName, user.Email, user.Role, user.IsActive);
    }

    public async Task<PlatformCompanyUsersResult> GetCompanyUsersAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var tenant = await LoadTenantAsync(tenantId, cancellationToken);
        var users = await _dbContext.PlatformUsers
            .IgnoreQueryFilters()
            .Include(item => item.Role)
            .Where(item => item.TenantId == tenantId)
            .OrderByDescending(item => item.IsActive)
            .ThenBy(item => item.FullName)
            .ToArrayAsync(cancellationToken);

        var mapped = users
            .Where(item => item.Role is not null)
            .Select(MapUser)
            .ToArray();

        return new PlatformCompanyUsersResult(
            tenant.Id,
            tenant.DisplayName,
            tenant.PlanName,
            users.Count(item => item.IsActive),
            tenant.MaxUsers,
            mapped);
    }

    public async Task<PlatformCompanyUserSummary> CreateCompanyUserAsync(CreatePlatformTenantUserCommand command, CancellationToken cancellationToken = default)
    {
        return await CreateTenantUserInternalAsync(
            command.TenantId,
            command.FullName,
            command.Email,
            command.Password,
            command.Role,
            command.IsActive,
            cancellationToken);
    }

    public async Task<PlatformCompanyUserSummary> UpdateCompanyUserAsync(UpdatePlatformTenantUserCommand command, CancellationToken cancellationToken = default)
    {
        await LoadTenantAsync(command.TenantId, cancellationToken);

        var user = await _dbContext.PlatformUsers
            .IgnoreQueryFilters()
            .Include(item => item.Role)
            .FirstOrDefaultAsync(item => item.Id == command.UserId && item.TenantId == command.TenantId, cancellationToken);

        if (user is null)
        {
            throw new DomainException("Usuario no encontrado para esta compania.");
        }

        var role = await ResolveTenantRoleAsync(command.Role, cancellationToken);
        var email = NormalizeEmail(command.Email);
        var emailExists = await _dbContext.PlatformUsers
            .IgnoreQueryFilters()
            .AnyAsync(item => item.Id != user.Id && item.Email == email, cancellationToken);
        if (emailExists)
        {
            throw new DomainException("Ya existe un usuario con ese email.");
        }

        await EnsureTenantHasUserCapacityAsync(command.TenantId, command.IsActive, user, cancellationToken);

        user.UpdateProfile(command.FullName, email, role.Id);
        if (command.IsActive)
        {
            user.Activate();
        }
        else
        {
            user.Deactivate();
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await RecordAuditAsync("platform.company.user.updated", command.TenantId, "Updated", cancellationToken);

        return new PlatformCompanyUserSummary(user.Id, command.TenantId, user.FullName, user.Email, role.Name, user.IsActive);
    }

    private async Task<PlatformCompanyUserSummary> CreateTenantUserInternalAsync(
        Guid tenantId,
        string fullName,
        string email,
        string password,
        string roleName,
        bool isActive,
        CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty)
        {
            throw new DomainException("tenantId is required.");
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            throw new DomainException("La password inicial es obligatoria.");
        }

        await LoadTenantAsync(tenantId, cancellationToken);
        var normalizedEmail = NormalizeEmail(email);
        var userExists = await _dbContext.PlatformUsers.IgnoreQueryFilters().AnyAsync(item => item.Email == normalizedEmail, cancellationToken);
        if (userExists)
        {
            throw new DomainException("Ya existe un usuario con ese email.");
        }

        var role = await ResolveTenantRoleAsync(roleName, cancellationToken);
        await EnsureTenantHasUserCapacityAsync(tenantId, isActive, null, cancellationToken);

        var user = PlatformUser.CreateTenantUser(fullName, normalizedEmail, _passwordHasher.Hash(password), role.Id, tenantId);
        if (!isActive)
        {
            user.Deactivate();
        }

        _dbContext.PlatformUsers.Add(user);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await RecordAuditAsync("platform.company.user.created", tenantId, "Created", cancellationToken);

        return new PlatformCompanyUserSummary(user.Id, tenantId, user.FullName, user.Email, role.Name, user.IsActive);
    }

    private async Task<Tenant> LoadTenantAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var tenant = await _dbContext.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(item => item.Id == tenantId, cancellationToken);

        if (tenant is null)
        {
            throw new DomainException("Tenant not found.");
        }

        return tenant;
    }

    private async Task<PlatformRole> ResolveTenantRoleAsync(string roleName, CancellationToken cancellationToken)
    {
        var normalizedRoleName = NormalizeTenantRole(roleName);
        var role = await _dbContext.PlatformRoles
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(item => item.Name == normalizedRoleName, cancellationToken);

        if (role is null)
        {
            role = normalizedRoleName switch
            {
                "TenantAdmin" => PlatformRole.CreateTenantAdmin(),
                "Operator" => PlatformRole.CreateOperator(),
                "Viewer" => PlatformRole.CreateViewer(),
                _ => throw new DomainException("Rol de usuario no permitido para esta compania.")
            };

            _dbContext.PlatformRoles.Add(role);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return role;
    }

    private async Task EnsureTenantHasUserCapacityAsync(
        Guid tenantId,
        bool requestedActive,
        PlatformUser? existingUser,
        CancellationToken cancellationToken)
    {
        if (!requestedActive)
        {
            return;
        }

        var tenant = await LoadTenantAsync(tenantId, cancellationToken);
        if (!tenant.MaxUsers.HasValue)
        {
            return;
        }

        if (existingUser?.IsActive == true)
        {
            return;
        }

        var activeUsers = await _dbContext.PlatformUsers
            .IgnoreQueryFilters()
            .CountAsync(item => item.TenantId == tenantId && item.IsActive, cancellationToken);

        if (activeUsers >= tenant.MaxUsers.Value)
        {
            throw new DomainException($"Tu plan permite un máximo de {tenant.MaxUsers.Value} usuarios activos. Inactiva un usuario o actualiza tu plan.");
        }
    }

    public async Task DeleteCompanyAsync(DeletePlatformCompanyCommand command, CancellationToken cancellationToken = default)
    {
        var tenant = await _dbContext.Tenants.FirstOrDefaultAsync(item => item.Id == command.TenantId, cancellationToken)
            ?? throw new DomainException("Company not found.");

        if (command.ActorTenantId == tenant.Id)
        {
            throw new DomainException("No puedes eliminar tu propia compania.");
        }

        if (!string.Equals(command.ConfirmationSlug?.Trim(), tenant.Slug, StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainException("La confirmacion no coincide con el slug de la compania.");
        }

        var hasProductionDocuments = await _dbContext.Documents.IgnoreQueryFilters()
            .AnyAsync(item => item.TenantId == tenant.Id && item.Environment == SifenEnvironmentType.Production, cancellationToken);
        if (hasProductionDocuments)
        {
            throw new DomainException("La compania tiene documentos en produccion y no se puede eliminar.");
        }

        var tenantId = tenant.Id;
        await using IDbContextTransaction? transaction = _dbContext.Database.IsRelational()
            ? await _dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

        // Hijos antes que padres para respetar las FK Restrict.
        await DeleteTenantRowsAsync(_dbContext.DocumentLines, tenantId, cancellationToken);
        await DeleteTenantRowsAsync(_dbContext.DocumentLogs, tenantId, cancellationToken);
        await DeleteTenantRowsAsync(_dbContext.DocumentErrors, tenantId, cancellationToken);
        await DeleteTenantRowsAsync(_dbContext.FeInvoiceEvents, tenantId, cancellationToken);
        await DeleteTenantRowsAsync(_dbContext.FeTenantLogs, tenantId, cancellationToken);
        await DeleteTenantRowsAsync(_dbContext.IdempotencyRecords, tenantId, cancellationToken);
        await DeleteTenantRowsAsync(_dbContext.Documents, tenantId, cancellationToken);
        await DeleteTenantRowsAsync(_dbContext.NumberingSequences, tenantId, cancellationToken);
        await DeleteTenantRowsAsync(_dbContext.FiscalStamps, tenantId, cancellationToken);
        await DeleteTenantRowsAsync(_dbContext.TaxpayerEconomicActivities, tenantId, cancellationToken);
        await DeleteTenantRowsAsync(_dbContext.TaxpayerProfiles, tenantId, cancellationToken);
        await DeleteTenantRowsAsync(_dbContext.TenantSifenSettings, tenantId, cancellationToken);
        await DeleteTenantRowsAsync(_dbContext.TenantCertificateMetadata, tenantId, cancellationToken);
        await DeleteTenantRowsAsync(_dbContext.TenantKudeTemplateSettings, tenantId, cancellationToken);

        var users = await _dbContext.PlatformUsers.Where(item => item.TenantId == tenantId).ToListAsync(cancellationToken);
        _dbContext.PlatformUsers.RemoveRange(users);
        _dbContext.Tenants.Remove(tenant);
        await _dbContext.SaveChangesAsync(cancellationToken);

        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        await RecordAuditAsync("platform.company.deleted", tenantId, "Deleted", cancellationToken);
    }

    private async Task DeleteTenantRowsAsync<TEntity>(DbSet<TEntity> set, Guid tenantId, CancellationToken cancellationToken)
        where TEntity : TenantScopedEntity
    {
        var query = set.IgnoreQueryFilters().Where(item => item.TenantId == tenantId);
        if (_dbContext.Database.IsRelational())
        {
            await query.ExecuteDeleteAsync(cancellationToken);
            return;
        }

        set.RemoveRange(await query.ToListAsync(cancellationToken));
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private Task RecordAuditAsync(string eventName, Guid tenantId, string outcome, CancellationToken cancellationToken)
    {
        return _auditTrail.RecordAsync(new AuditEvent
        {
            EventName = eventName,
            Category = AuditCategory.TenantOperation,
            Severity = AuditSeverity.Information,
            OccurredAt = _clock.UtcNow,
            TenantId = tenantId.ToString(),
            ResourceType = "Tenant",
            ResourceId = tenantId.ToString(),
            Outcome = outcome
        }, cancellationToken);
    }

    private static string NormalizeSlug(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException("slug is required.");
        }

        return value.Trim().ToLowerInvariant();
    }

    private static string NormalizeEmail(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException("email is required.");
        }

        return value.Trim().ToLowerInvariant();
    }

    private static string NormalizeTenantRole(string? roleName)
    {
        var normalized = roleName?.Trim();
        return normalized switch
        {
            "TenantAdmin" => "TenantAdmin",
            "Operator" => "Operator",
            "Viewer" => "Viewer",
            _ => throw new DomainException("Rol de usuario no permitido para esta compania.")
        };
    }

    private static PlatformCompanySummary MapSummary(Tenant tenant)
    {
        return new PlatformCompanySummary(
            tenant.Id,
            tenant.Slug,
            tenant.DisplayName,
            tenant.PlanName,
            tenant.Status.ToString(),
            tenant.MaxInvoicesPerMonth,
            tenant.MaxUsers);
    }

    private static PlatformCompanyUserSummary MapUser(PlatformUser user)
    {
        return new PlatformCompanyUserSummary(
            user.Id,
            user.TenantId ?? Guid.Empty,
            user.FullName,
            user.Email,
            user.Role?.Name ?? "Unknown",
            user.IsActive);
    }
}
