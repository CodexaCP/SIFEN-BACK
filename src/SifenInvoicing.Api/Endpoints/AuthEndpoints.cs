using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using SifenInvoicing.Application.Auth;
using SifenInvoicing.Domain.Common;

namespace SifenInvoicing.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        MapAuthGroup(app.MapGroup("/auth").WithTags("Auth"));
        MapAuthGroup(app.MapGroup("/api/auth").WithTags("Auth"));

        return app;
    }

    private static void MapAuthGroup(RouteGroupBuilder group)
    {
        group.MapPost("/login", async (
            LoginRequest request,
            IPlatformAuthService authService,
            CancellationToken cancellationToken) =>
        {
            var result = await authService.LoginAsync(
                new LoginPlatformUserCommand(request.Username, request.Password),
                cancellationToken);

            return Results.Ok(result);
        });

        group.MapGet("/me", [Authorize] async (
            ClaimsPrincipal user,
            IPlatformAuthService authService,
            CancellationToken cancellationToken) =>
        {
            var userIdValue = user.FindFirstValue("userId") ?? user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue(ClaimTypes.Name);
            if (!Guid.TryParse(userIdValue, out var userId))
            {
                return Results.Unauthorized();
            }

            var session = await authService.GetByIdAsync(userId, cancellationToken);
            return session is null
                ? Results.Unauthorized()
                : Results.Ok(new
                {
                    userId = session.UserId,
                    companyId = session.CompanyId,
                    tenantId = session.TenantId,
                    tenantName = session.TenantName,
                    fullName = session.FullName,
                    displayName = session.FullName,
                    role = session.Role,
                    email = session.Email,
                    permissions = session.Permissions
                });
        });
    }

    public sealed record LoginRequest(
        string Username,
        string Password);
}
