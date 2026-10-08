using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using SifenInvoicing.Api.Middleware;
using SifenInvoicing.Domain.Common;

namespace SifenInvoicing.Tests;

public sealed class ExceptionHandlingMiddlewareTests
{
    [Theory]
    [InlineData("Ya existe un usuario con ese email.", 409, "USER_EMAIL_ALREADY_EXISTS", "Ya existe un usuario con ese email.")]
    [InlineData("Tenant slug 'codexa' already exists.", 409, "TENANT_SLUG_ALREADY_EXISTS", "Ya existe una compania con ese slug.")]
    [InlineData("La password inicial es obligatoria.", 400, "USER_PASSWORD_REQUIRED", "La password inicial es obligatoria.")]
    [InlineData("Tu plan permite un máximo de 5 usuarios activos. Inactiva un usuario o actualiza tu plan.", 409, "PLAN_USER_LIMIT_REACHED", "Tu plan ya alcanzo el maximo de usuarios activos.")]
    [InlineData("tenantId is required.", 400, "DOMAIN_VALIDATION_ERROR", "No pudimos completar la solicitud con los datos recibidos.")]
    public async Task InvokeAsync_ShouldMapPlatformDomainErrorsToSpecificUserMessages(
        string domainMessage,
        int expectedStatus,
        string expectedErrorCode,
        string expectedUserMessage)
    {
        var middleware = new ExceptionHandlingMiddleware(
            _ => throw new DomainException(domainMessage),
            NullLogger<ExceptionHandlingMiddleware>.Instance);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.Body.Position = 0;
        using var body = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal(expectedStatus, context.Response.StatusCode);
        Assert.Equal(expectedErrorCode, body.RootElement.GetProperty("errorCode").GetString());
        Assert.Equal(expectedUserMessage, body.RootElement.GetProperty("userMessage").GetString());
    }
}
