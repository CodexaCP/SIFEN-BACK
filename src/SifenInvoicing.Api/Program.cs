using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Serilog;
using SifenInvoicing.Api.Auth;
using SifenInvoicing.Api.Endpoints;
using SifenInvoicing.Api.Health;
using SifenInvoicing.Api.Middleware;
using SifenInvoicing.Application.Auth;
using SifenInvoicing.Domain.Documents;
using SifenInvoicing.Domain.Tenants;
using SifenInvoicing.Infrastructure.Persistence;
using SifenInvoicing.Application.Operations;
using SifenInvoicing.Application.Tenancy;
using SifenInvoicing.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Host.UseSerilog((context, services, loggerConfiguration) =>
{
    loggerConfiguration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext();
});

const string LocalFrontendCorsPolicy = "LocalFrontend";

builder.Services.AddInfrastructure(builder.Configuration);
var jwtSigningKey = Environment.GetEnvironmentVariable(builder.Configuration["Auth:Jwt:SigningKeyEnvironmentVariable"] ?? "SIFEN_JWT_SIGNING_KEY");
if (string.IsNullOrWhiteSpace(jwtSigningKey))
{
    if (!builder.Environment.IsDevelopment())
    {
        throw new InvalidOperationException(
            "JWT signing key is required outside Development. Set the environment variable named by Auth:Jwt:SigningKeyEnvironmentVariable (default SIFEN_JWT_SIGNING_KEY).");
    }

    // Development sin clave: se usa una clave efimera para que la autenticacion siga fallando cerrada
    // (ningun token externo valida) en vez de dejar los endpoints abiertos.
    jwtSigningKey = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(48));
}

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ValidIssuer = builder.Configuration["Auth:Jwt:Issuer"] ?? "SifenInvoicing.Api",
            ValidAudience = builder.Configuration["Auth:Jwt:Audience"] ?? "SifenInvoicing.Frontend",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSigningKey)),
            ClockSkew = TimeSpan.FromMinutes(2)
        };
    });
builder.Services.AddApiAuthorization();
var configuredCorsOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>();
var corsOrigins = configuredCorsOrigins is { Length: > 0 }
    ? configuredCorsOrigins
    : ["http://localhost:4200", "https://localhost:4200"];
builder.Services.AddCors(options =>
{
    options.AddPolicy(LocalFrontendCorsPolicy, policy =>
    {
        policy
            .WithOrigins(corsOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .WithExposedHeaders(CorrelationIdMiddleware.HeaderName, "Content-Disposition");
    });
});
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services
    .AddHealthChecks()
    .AddCheck<SifenConfigurationHealthCheck>("sifen-configuration", tags: ["ready", "configuration"])
    .AddCheck<DatabaseConnectivityHealthCheck>("sql-server", tags: ["ready", "database"]);

var app = builder.Build();
app.UseSwagger();
app.UseSwaggerUI();

app.UseSerilogRequestLogging();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseCors(LocalFrontendCorsPolicy);
app.UseAuthentication();
app.UseMiddleware<TenantResolutionMiddleware>();
app.UseMiddleware<RequestAuditMiddleware>();
app.UseAuthorization();
app.UseStaticFiles();

app.MapGet("/", () => Results.Redirect("/ops/status"));

app.MapGet("/ops/status", (
    IConfiguration configuration,
    IWebHostEnvironment environment,
    ITenantContextAccessor tenantContextAccessor) =>
{
    var activeSifenEnvironment = configuration["Sifen:ActiveEnvironment"] ?? "Test";
    var configuredBaseUrl = configuration[$"Sifen:Environments:{activeSifenEnvironment}:BaseUrl"];
    var tenant = tenantContextAccessor.Current;

    return Results.Ok(new
    {
        service = "SifenInvoicing.Api",
        environment = environment.EnvironmentName,
        utcNow = DateTimeOffset.UtcNow,
        sifen = new
        {
            activeEnvironment = activeSifenEnvironment,
            baseUrlConfigured = !string.IsNullOrWhiteSpace(configuredBaseUrl),
            connectivityChecks = "Not enabled yet. First SIFEN integration step will add mTLS SOAP checks."
        },
        audit = new
        {
            correlationHeader = CorrelationIdMiddleware.HeaderName,
            tenantHeader = TenantResolutionMiddleware.TenantHeaderName,
            clientHeader = TenantResolutionMiddleware.ClientHeaderName,
            taxpayerRucHeader = TenantResolutionMiddleware.TaxpayerRucHeaderName,
            mode = "Structured request audit enabled"
        },
        tenant = new
        {
            tenant.IsResolved,
            tenant.TenantId,
            tenant.ResolvedTenantId,
            tenant.ClientId,
            taxpayerRucPresent = !string.IsNullOrWhiteSpace(tenant.TaxpayerRuc)
        }
    });
});

app.MapGet("/ops/dependencies", async (
    IOperationalReadinessReporter readinessReporter,
    CancellationToken cancellationToken) =>
{
    var snapshot = await readinessReporter.GetSnapshotAsync(cancellationToken);
    return Results.Ok(new
    {
        utcNow = DateTimeOffset.UtcNow,
        dependencies = snapshot
    });
});

app.MapGet("/ops/fe-diagnostic", async (
    string? environment,
    IOperationalReadinessReporter readinessReporter,
    CancellationToken cancellationToken) =>
{
    if (!TryParseSifenEnvironment(environment, out var parsedEnvironment))
    {
        return Results.BadRequest(new { error = "Invalid SIFEN environment." });
    }

    var diagnostic = await readinessReporter.GetTenantFeDiagnosticAsync(
        parsedEnvironment,
        cancellationToken);
    return Results.Ok(diagnostic);
}).RequireAuthorization(ApiAuthorization.InvoicesReadPolicy);

app.MapGet("/api/fe/diagnostic/{tenantId:guid}", async (
    Guid tenantId,
    string? environment,
    IOperationalReadinessReporter readinessReporter,
    ITenantContextAccessor tenantContextAccessor,
    CancellationToken cancellationToken) =>
{
    if (tenantContextAccessor.Current.ResolvedTenantId != tenantId)
    {
        return Results.Forbid();
    }

    if (!TryParseSifenEnvironment(environment, out var parsedEnvironment))
    {
        return Results.BadRequest(new { error = "Invalid SIFEN environment." });
    }

    var diagnostic = await readinessReporter.GetTenantFeDiagnosticAsync(
        tenantId,
        parsedEnvironment,
        cancellationToken);

    return Results.Ok(new
    {
        tenantId = diagnostic.TenantId,
        environment = diagnostic.Environment,
        transportMode = diagnostic.TransportMode,
        readyForInternalValidation = diagnostic.ReadyForInternalValidation,
        readyForSifenTestAttempt = diagnostic.ReadyForSifenTestAttempt,
        ready = diagnostic.ReadyForSifenTestAttempt,
        readySummary = diagnostic.Summary,
        missing = diagnostic.Missing,
        warnings = diagnostic.Warnings,
        checks = diagnostic.Checks.Select(check => new
        {
            code = check.Code,
            status = check.Status,
            message = check.Message
        }).ToArray(),
        lastError = diagnostic.LastError is null
            ? null
            : new
            {
                diagnostic.LastError.Cdc,
                diagnostic.LastError.Status,
                diagnostic.LastError.StatusCode,
                diagnostic.LastError.StatusMessage,
                diagnostic.LastError.OccurredAt
            },
        lastSubmission = diagnostic.LastSubmission
            is null
            ? null
            : new
            {
                diagnostic.LastSubmission.Cdc,
                diagnostic.LastSubmission.Status,
                diagnostic.LastSubmission.SubmittedAt,
                diagnostic.LastSubmission.Endpoint
            }
    });
}).RequireAuthorization(ApiAuthorization.InvoicesReadPolicy);

app.MapGet("/api/fe/plan/{tenantId:guid}", async (
    Guid tenantId,
    SifenDbContext dbContext,
    ITenantContextAccessor tenantContextAccessor,
    CancellationToken cancellationToken) =>
{
    if (tenantContextAccessor.Current.ResolvedTenantId != tenantId)
    {
        return Results.Forbid();
    }

    if (tenantId == Guid.Empty)
    {
        return Results.BadRequest(new
        {
            error = "tenantId is required."
        });
    }

    var tenant = await dbContext.Tenants
        .AsNoTracking()
        .IgnoreQueryFilters()
        .FirstOrDefaultAsync(item => item.Id == tenantId, cancellationToken);

    if (tenant is null)
    {
        return Results.NotFound();
    }

    var now = DateTimeOffset.UtcNow;
    var monthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
    var nextMonthStart = monthStart.AddMonths(1);
    var usedInvoices = await dbContext.Documents
        .AsNoTracking()
        .IgnoreQueryFilters()
        .CountAsync(item =>
            item.TenantId == tenantId &&
            item.Kind == SifenDocumentKind.Invoice &&
            item.IssuedAt >= monthStart &&
            item.IssuedAt < nextMonthStart,
            cancellationToken);

    var limitReached = tenant.MaxInvoicesPerMonth.HasValue && usedInvoices >= tenant.MaxInvoicesPerMonth.Value;
    var activeUsers = await dbContext.PlatformUsers
        .AsNoTracking()
        .IgnoreQueryFilters()
        .CountAsync(item => item.TenantId == tenantId && item.IsActive, cancellationToken);

    return Results.Ok(new
    {
        tenantId = tenant.Id,
        planName = tenant.PlanName,
        active = tenant.Status == SifenInvoicing.Domain.Tenants.TenantStatus.Active,
        usedInvoicesThisMonth = usedInvoices,
        maxInvoicesPerMonth = tenant.MaxInvoicesPerMonth,
        maxUsers = tenant.MaxUsers,
        usersUsed = activeUsers,
        usersMessage = tenant.MaxUsers.HasValue
            ? $"{activeUsers} / {tenant.MaxUsers.Value} usuarios activos."
            : $"{activeUsers} usuarios activos.",
        limitReached
    });
}).RequireAuthorization(ApiAuthorization.InvoicesReadPolicy);

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = WriteHealthResponseAsync
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready"),
    ResponseWriter = WriteHealthResponseAsync
});

using (var scope = app.Services.CreateScope())
{
    var bootstrapper = scope.ServiceProvider.GetRequiredService<IPlatformAuthBootstrapper>();
    await bootstrapper.EnsureSeededAsync();
}

app.MapAuthEndpoints();
app.MapPlatformCompanyEndpoints();
app.MapOnboardingEndpoints();
app.MapInvoiceEndpoints();
app.MapXmlSigningEndpoints();

app.Run();

static Task WriteHealthResponseAsync(HttpContext context, HealthReport report)
{
    context.Response.ContentType = "application/json";

    var payload = new
    {
        status = report.Status.ToString(),
        durationMs = report.TotalDuration.TotalMilliseconds,
        checks = report.Entries.Select(entry => new
        {
            name = entry.Key,
            status = entry.Value.Status.ToString(),
            description = entry.Value.Description,
            data = entry.Value.Data
        })
    };

    return JsonSerializer.SerializeAsync(context.Response.Body, payload, new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    });
}

static bool TryParseSifenEnvironment(string? value, out SifenEnvironmentType environment)
{
    return Enum.TryParse(value ?? "Test", true, out environment);
}
public partial class Program;
