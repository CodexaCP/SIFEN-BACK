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

    [Theory]
    [InlineData("Fiscal stamp already exists for this tenant and environment.", "FISCAL_STAMP_ALREADY_EXISTS")]
    [InlineData("Taxpayer profile must be registered before its fiscal data.", "TAXPAYER_PROFILE_REQUIRED")]
    [InlineData("Some other invalid operation.", "INTERNAL_OPERATION_CONFLICT")]
    public async Task InvokeAsync_ShouldMapOnboardingConflicts(string message, string expectedErrorCode)
    {
        var middleware = new ExceptionHandlingMiddleware(
            _ => throw new InvalidOperationException(message),
            NullLogger<ExceptionHandlingMiddleware>.Instance);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.Body.Position = 0;
        using var body = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal(409, context.Response.StatusCode);
        Assert.Equal(expectedErrorCode, body.RootElement.GetProperty("errorCode").GetString());
    }

    [Theory]
    [InlineData("stampingNumber must contain exactly 8 digits (dNumTim).", "STAMPING_NUMBER_INVALID")]
    [InlineData("establishmentCode must contain exactly 3 digits.", "ESTABLISHMENT_CODE_INVALID")]
    [InlineData("taxpayerType must be 1 (persona fisica) or 2 (persona juridica).", "TAXPAYER_TYPE_INVALID")]
    public async Task InvokeAsync_ShouldMapOnboardingValidationErrors(string message, string expectedErrorCode)
    {
        var middleware = new ExceptionHandlingMiddleware(
            _ => throw new DomainException(message),
            NullLogger<ExceptionHandlingMiddleware>.Instance);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.Body.Position = 0;
        using var body = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal(400, context.Response.StatusCode);
        Assert.Equal(expectedErrorCode, body.RootElement.GetProperty("errorCode").GetString());
    }
}
