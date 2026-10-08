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
        catch (InvalidOperationException ex) when (TryMapOnboardingMessage(ex.Message, out var onboarding))
        {
            _logger.LogWarning(ex, "Onboarding conflict processing request.");
            await WriteErrorAsync(context, onboarding.HttpStatusCode, onboarding.ErrorCode, onboarding.Category, onboarding.UserMessage, onboarding.SuggestedAction, onboarding.IsRetryable);
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

        if (ContainsAny(message, "Ya existe un usuario con ese email"))
        {
            return (
                StatusCodes.Status409Conflict,
                "USER_EMAIL_ALREADY_EXISTS",
                "Configuration",
                "Ya existe un usuario con ese email.",
                "Usa otro email para el usuario o administrador de la compania.",
                false);
        }

        if (ContainsAny(message, "Tenant slug") && ContainsAny(message, "already exists"))
        {
            return (
                StatusCodes.Status409Conflict,
                "TENANT_SLUG_ALREADY_EXISTS",
                "Configuration",
                "Ya existe una compania con ese slug.",
                "Usa otro slug para la nueva compania.",
                false);
        }

        if (ContainsAny(message, "La password inicial es obligatoria"))
        {
            return (
                StatusCodes.Status400BadRequest,
                "USER_PASSWORD_REQUIRED",
                "Configuration",
                "La password inicial es obligatoria.",
                "Ingresa una password inicial para el usuario.",
                false);
        }

        if (ContainsAny(message, "No puedes eliminar tu propia compania"))
        {
            return (
                StatusCodes.Status409Conflict,
                "COMPANY_DELETE_OWN_TENANT",
                "Configuration",
                "No puedes eliminar la compania a la que pertenece tu usuario.",
                "Elimina la compania desde otro usuario de plataforma.",
                false);
        }

        if (ContainsAny(message, "La confirmacion no coincide con el slug"))
        {
            return (
                StatusCodes.Status400BadRequest,
                "COMPANY_DELETE_CONFIRMATION_MISMATCH",
                "Configuration",
                "La confirmacion no coincide con el slug de la compania.",
                "Escribe el slug exacto de la compania para confirmar.",
                false);
        }

        if (ContainsAny(message, "documentos en produccion"))
        {
            return (
                StatusCodes.Status409Conflict,
                "COMPANY_DELETE_HAS_PRODUCTION_DOCUMENTS",
                "Configuration",
                "La compania tiene documentos emitidos en produccion y no se puede eliminar.",
                "Suspende la compania desde su plan en lugar de eliminarla.",
                false);
        }

        if (ContainsAny(message, "usuarios activos"))
        {
            return (
                StatusCodes.Status409Conflict,
                "PLAN_USER_LIMIT_REACHED",
                "PlanLimit",
                "Tu plan ya alcanzo el maximo de usuarios activos.",
                "Inactiva un usuario o actualiza el plan de la compania.",
                false);
        }

        if (TryMapOnboardingMessage(message, out var onboarding))
        {
            return onboarding;
        }

        return (
            StatusCodes.Status400BadRequest,
            "DOMAIN_VALIDATION_ERROR",
            "InternalSystem",
            "No pudimos completar la solicitud con los datos recibidos.",
            "Revisa la informacion ingresada y vuelve a intentarlo.",
            false);
    }

    // Mensajes del alta fiscal (OnboardingEndpoints / EfTenantOnboardingService / entidades de Tenants).
    private static readonly (string Fragment, int HttpStatusCode, string ErrorCode, string UserMessage, string SuggestedAction)[] OnboardingMessages =
    [
        ("Taxpayer profile must be registered before", StatusCodes.Status409Conflict, "TAXPAYER_PROFILE_REQUIRED", "Primero hay que guardar RUC y razon social de la compania.", "Completa Config SIFEN y vuelve a intentarlo."),
        ("Taxpayer profile already exists", StatusCodes.Status409Conflict, "TAXPAYER_PROFILE_ALREADY_EXISTS", "Ya existe un perfil de contribuyente para este RUC.", "Edita los datos desde Config SIFEN."),
        ("Fiscal stamp already exists", StatusCodes.Status409Conflict, "FISCAL_STAMP_ALREADY_EXISTS", "Ya hay un timbrado registrado para este ambiente.", "No hace falta registrarlo de nuevo."),
        ("Fiscal stamp was not found", StatusCodes.Status409Conflict, "FISCAL_STAMP_REQUIRED", "No hay un timbrado registrado con ese numero para este ambiente.", "Registra primero el timbrado y despues la numeracion."),
        ("Numbering sequence already exists", StatusCodes.Status409Conflict, "NUMBERING_SEQUENCE_ALREADY_EXISTS", "Ya existe esa numeracion para el timbrado, establecimiento y punto de expedicion.", "No hace falta registrarla de nuevo."),
        ("SIFEN settings already exist", StatusCodes.Status409Conflict, "SIFEN_SETTINGS_ALREADY_EXIST", "La configuracion SIFEN de este ambiente ya existe.", "Editala desde Config SIFEN."),
        ("Certificate metadata already exists", StatusCodes.Status409Conflict, "CERTIFICATE_ALREADY_EXISTS", "Ya existe un certificado con ese alias para este ambiente y uso.", "Usa otro alias o revisa el certificado registrado."),
        ("stampingNumber must contain exactly 8 digits", StatusCodes.Status400BadRequest, "STAMPING_NUMBER_INVALID", "El timbrado debe tener exactamente 8 digitos.", "Revisa el numero de timbrado."),
        ("validTo cannot be earlier than validFrom", StatusCodes.Status400BadRequest, "STAMP_VALIDITY_INVALID", "La fecha de fin de vigencia no puede ser anterior a la de inicio.", "Revisa las fechas del timbrado."),
        ("establishmentCode must contain exactly 3 digits", StatusCodes.Status400BadRequest, "ESTABLISHMENT_CODE_INVALID", "El establecimiento debe tener exactamente 3 digitos.", "Ejemplo: 001."),
        ("expeditionPointCode must contain exactly 3 digits", StatusCodes.Status400BadRequest, "EXPEDITION_POINT_CODE_INVALID", "El punto de expedicion debe tener exactamente 3 digitos.", "Ejemplo: 001."),
        ("documentTypeCode must contain exactly 2 digits", StatusCodes.Status400BadRequest, "DOCUMENT_TYPE_CODE_INVALID", "El tipo de documento debe tener exactamente 2 digitos.", "Revisa el tipo de documento."),
        ("nextNumber must be between", StatusCodes.Status400BadRequest, "FIRST_NUMBER_INVALID", "El primer numero de la secuencia esta fuera de rango.", "Usa un numero entre 1 y 9999999."),
        ("taxpayerType must be 1", StatusCodes.Status400BadRequest, "TAXPAYER_TYPE_INVALID", "El tipo de contribuyente debe ser persona fisica o persona juridica.", "Selecciona el tipo de contribuyente."),
        ("economic activities are allowed", StatusCodes.Status400BadRequest, "ECONOMIC_ACTIVITIES_LIMIT", "Se supero la cantidad maxima de actividades economicas.", "Quita actividades y vuelve a intentarlo.")
    ];

    private static bool TryMapOnboardingMessage(
        string message,
        out (int HttpStatusCode, string ErrorCode, string Category, string UserMessage, string SuggestedAction, bool IsRetryable) mapped)
    {
        foreach (var entry in OnboardingMessages)
        {
            if (message.Contains(entry.Fragment, StringComparison.OrdinalIgnoreCase))
            {
                mapped = (entry.HttpStatusCode, entry.ErrorCode, "Configuration", entry.UserMessage, entry.SuggestedAction, false);
                return true;
            }
        }

        mapped = default;
        return false;
    }

    private static bool ContainsAny(string message, params string[] fragments)
        => fragments.Any(fragment => message.Contains(fragment, StringComparison.OrdinalIgnoreCase));
}
