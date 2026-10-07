using SifenInvoicing.Api.Auth;
using SifenInvoicing.Application.XmlSigning;
using SifenInvoicing.Application.Tenancy;
using SifenInvoicing.Domain.Tenants;

namespace SifenInvoicing.Api.Endpoints;

public static class XmlSigningEndpoints
{
    public static IEndpointRouteBuilder MapXmlSigningEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/internal/xml-signing")
            .WithTags("Internal XML Signing")
            .RequireAuthorization(ApiAuthorization.SifenConfigurePolicy);

        group.MapPost("/sign", async (
            SignXmlRequest request,
            IXmlDocumentSigner signer,
            ITenantContextAccessor tenantContextAccessor,
            CancellationToken cancellationToken) =>
        {
            // El tenant efectivo sale de la identidad; el TenantId del body solo se acepta si coincide.
            var effectiveTenant = tenantContextAccessor.Current.ResolvedTenantId;
            if (!effectiveTenant.HasValue || effectiveTenant.Value != request.TenantId)
            {
                return Results.Forbid();
            }

            if (!Enum.TryParse<SifenEnvironmentType>(request.Environment, ignoreCase: true, out var environment))
            {
                return Results.BadRequest(new { error = "Invalid SIFEN environment." });
            }

            var result = await signer.SignAsync(
                new SignXmlDocumentCommand(
                    request.TenantId,
                    environment,
                    request.DocumentId,
                    request.Xml),
                cancellationToken);

            return Results.Ok(result);
        });

        return app;
    }

    public sealed record SignXmlRequest(
        Guid TenantId,
        string Environment,
        string DocumentId,
        string Xml);
}
