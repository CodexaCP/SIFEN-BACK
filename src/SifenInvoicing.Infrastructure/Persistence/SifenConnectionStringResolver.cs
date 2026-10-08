using Microsoft.Extensions.Configuration;

namespace SifenInvoicing.Infrastructure.Persistence;

/// <summary>
/// Conexion SQL Server efectiva del runtime: SIFEN_CONNECTION_STRING (si existe y no esta vacia)
/// y, como fallback, ConnectionStrings:DefaultConnection.
/// </summary>
public static class SifenConnectionStringResolver
{
    public const string EnvironmentVariableName = "SIFEN_CONNECTION_STRING";
    public const string DefaultConnectionName = "DefaultConnection";

    public static string? Resolve(IConfiguration configuration)
    {
        var fromEnvironment = configuration[EnvironmentVariableName];
        return string.IsNullOrWhiteSpace(fromEnvironment)
            ? configuration.GetConnectionString(DefaultConnectionName)
            : fromEnvironment;
    }
}
