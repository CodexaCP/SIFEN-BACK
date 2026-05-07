using SifenInvoicing.Domain.Common;

namespace SifenInvoicing.Api.Middleware;

public sealed class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (UserFacingException ex)
        {
            _logger.LogWarning("User-facing exception. Code={ErrorCode} Category={Category} CorrelationId={CorrelationId}", ex.ErrorCode, ex.Category, ResolveCorrelationId(context));
            await WriteErrorAsync(context, ex.HttpStatusCode, ex.ErrorCode, ex.Category, ex.UserMessage, ex.SuggestedAction, ex.IsRetryable);
        }
        catch (DomainException ex)
        {
            var mapped = MapDomainException(ex);
            await WriteErrorAsync(context, mapped.HttpStatusCode, mapped.ErrorCode, mapped.Category, mapped.UserMessage, mapped.SuggestedAction, mapped.IsRetryable);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Invalid operation processing request.");
            await WriteErrorAsync(
                context,
                StatusCodes.Status409Conflict,
                "INTERNAL_OPERATION_CONFLICT",
                "InternalSystem",
                "No pudimos completar esta operacion en este momento.",
                "Vuelve a intentarlo. Si el problema continua, comparte el codigo de seguimiento con soporte.",
                false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception processing request.");
            await WriteErrorAsync(
                context,
                StatusCodes.Status500InternalServerError,
                "UNEXPECTED_ERROR",
                "InternalSystem",
                "Ocurrio un problema interno al procesar la solicitud.",
                "Vuelve a intentarlo. Si el problema continua, comparte el codigo de seguimiento con soporte.",
                false);
        }
    }

    private static Task WriteErrorAsync(
        HttpContext context,
        int statusCode,
        string errorCode,
        string category,
        string userMessage,
        string suggestedAction,
        bool isRetryable)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";

        return context.Response.WriteAsJsonAsync(new
        {
            errorCode,
            category,
            userMessage,
            suggestedAction,
            isRetryable,
            correlationId = ResolveCorrelationId(context)
        });
    }

    private static string ResolveCorrelationId(HttpContext context)
    {
        if (context.Items.TryGetValue(CorrelationIdMiddleware.ItemName, out var itemValue) &&
            itemValue is string correlationId &&
            !string.IsNullOrWhiteSpace(correlationId))
        {
            return correlationId;
        }

        if (context.Request.Headers.TryGetValue(CorrelationIdMiddleware.HeaderName, out var headerValue))
        {
            var headerCorrelationId = headerValue.ToString();
            if (!string.IsNullOrWhiteSpace(headerCorrelationId))
            {
                return headerCorrelationId.Trim();
            }
        }

        return context.TraceIdentifier;
    }

    private static (int HttpStatusCode, string ErrorCode, string Category, string UserMessage, string SuggestedAction, bool IsRetryable) MapDomainException(DomainException exception)
    {
        var message = exception.Message;

        if (ContainsAny(message, "Tenant monthly invoice limit has been reached"))
        {
            return (
                StatusCodes.Status409Conflict,
                "PLAN_MONTHLY_LIMIT_REACHED",
                "PlanLimit",
                "Tu plan ya alcanzo el maximo mensual de facturas.",
                "Actualiza tu plan o espera al siguiente periodo antes de emitir una nueva factura.",
                false);
        }

        if (ContainsAny(message, "Tenant is not active", "Tenant is inactive"))
        {
            return (
                StatusCodes.Status403Forbidden,
                "TENANT_INACTIVE",
                "Configuration",
                "Esta compania no esta habilitada para emitir.",
                "Activa la compania o revisa su estado comercial antes de continuar.",
                false);
        }

        if (ContainsAny(message, "Tenant SIFEN configuration is incomplete"))
        {
            return (
                StatusCodes.Status409Conflict,
                "SIFEN_CONFIGURATION_INCOMPLETE",
                "Configuration",
                "Faltan datos para dejar lista la emision.",
                "Revisa el diagnostico y completa los datos pendientes antes de volver a emitir.",
                false);
        }

        if (ContainsAny(message, "XML signing certificate is not ready", "certificate"))
        {
            return (
                StatusCodes.Status409Conflict,
                "CERTIFICATE_NOT_READY",
                "CertificateSignature",
                "No pudimos usar el certificado de firma de esta compania.",
                "Revisa el certificado, su clave y su vigencia antes de volver a intentar.",
                false);
        }

        if (ContainsAny(message, "Customer is required", "invoice item", "IssueDate is required", "Currency is invalid", "SaleCondition is invalid", "ReceptorTipoDocumento is invalid", "Generated FE XML totals are inconsistent", "Generated CDC is invalid", "Total does not match item amounts"))
        {
            return (
                StatusCodes.Status400BadRequest,
                "INVOICE_DATA_INVALID",
                "InvoiceData",
                "La factura tiene datos incompletos o invalidos.",
                "Corrige los datos de la factura y vuelve a intentarlo.",
                false);
        }

        if (ContainsAny(message, "Retry is only allowed", "Approved invoices cannot be retried", "Rejected invoices require explicit reprocessing", "Retry requires an existing signed XML payload"))
        {
            return (
                StatusCodes.Status409Conflict,
                "RETRY_NOT_ALLOWED",
                "InvoiceData",
                "Esta factura no puede reintentarse desde aqui.",
                "Revisa el estado actual y corrige la configuracion o los datos antes de continuar.",
                false);
        }

        if (ContainsAny(message, "Tenant context is required", "Tenant was not found", "Invoice was not found"))
        {
            return (
                StatusCodes.Status404NotFound,
                "TENANT_OR_INVOICE_NOT_FOUND",
                "Configuration",
                "No encontramos la compania o la factura solicitada.",
                "Actualiza la pantalla y vuelve a entrar desde la compania correcta.",
                false);
        }

        return (
            StatusCodes.Status400BadRequest,
            "DOMAIN_VALIDATION_ERROR",
            "InternalSystem",
            "No pudimos completar la solicitud con los datos recibidos.",
            "Revisa la informacion ingresada y vuelve a intentarlo.",
            false);
    }

    private static bool ContainsAny(string message, params string[] fragments)
        => fragments.Any(fragment => message.Contains(fragment, StringComparison.OrdinalIgnoreCase));
}
