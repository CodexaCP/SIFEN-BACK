using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using SifenInvoicing.Infrastructure.Diagnostics;
using SifenInvoicing.Infrastructure.Tenancy;

namespace SifenInvoicing.Infrastructure.Persistence;

public sealed class SifenDbContextFactory : IDesignTimeDbContextFactory<SifenDbContext>
{
    public SifenDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<SifenDbContext>();
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";
        var connectionString = Environment.GetEnvironmentVariable("SIFEN_CONNECTION_STRING")
            ?? TryReadConnectionString(environment)
            ?? throw new InvalidOperationException("DefaultConnection is not configured for design-time migrations.");

        optionsBuilder.UseSqlServer(connectionString);

        return new SifenDbContext(
            optionsBuilder.Options,
            new SystemClock(),
            new AsyncLocalTenantContextAccessor());
    }

    private static string? TryReadConnectionString(string environment)
    {
        var basePath = ResolveApiBasePath();

        return ReadConnectionString(Path.Combine(basePath, $"appsettings.{environment}.json"))
            ?? ReadConnectionString(Path.Combine(basePath, "appsettings.json"));
    }

    private static string ResolveApiBasePath()
    {
        var currentDirectory = Directory.GetCurrentDirectory();
        var candidates = new[]
        {
            Path.Combine(currentDirectory, "src", "SifenInvoicing.Api"),
            Path.Combine(currentDirectory, "..", "SifenInvoicing.Api"),
            currentDirectory
        };

        foreach (var candidate in candidates.Select(Path.GetFullPath))
        {
            if (File.Exists(Path.Combine(candidate, "appsettings.json")))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("Could not resolve SifenInvoicing.Api configuration path for design-time migrations.");
    }

    private static string? ReadConnectionString(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (!document.RootElement.TryGetProperty("ConnectionStrings", out var connectionStrings))
        {
            return null;
        }

        return connectionStrings.TryGetProperty("DefaultConnection", out var value)
            ? value.GetString()
            : null;
    }
}
