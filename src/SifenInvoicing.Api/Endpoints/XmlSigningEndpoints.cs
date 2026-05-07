using SifenInvoicing.Application.XmlSigning;
using SifenInvoicing.Domain.Tenants;

namespace SifenInvoicing.Api.Endpoints;

public static class XmlSigningEndpoints
{
    public static IEndpointRouteBuilder MapXmlSigningEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/internal/xml-signing")
            .WithTags("Internal XML Signing");

        group.MapPost("/sign", async (
            SignXmlRequest request,
            IXmlDocumentSigner signer,
            CancellationToken cancellationToken) =>
        {
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
