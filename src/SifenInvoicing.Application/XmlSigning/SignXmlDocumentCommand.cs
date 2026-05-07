using SifenInvoicing.Domain.Tenants;

namespace SifenInvoicing.Application.XmlSigning;

public sealed record SignXmlDocumentCommand(
    Guid TenantId,
    SifenEnvironmentType Environment,
    string DocumentId,
    string Xml);
