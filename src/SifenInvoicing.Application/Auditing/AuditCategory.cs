namespace SifenInvoicing.Application.Auditing;

public enum AuditCategory
{
    ApiRequest,
    Authentication,
    Authorization,
    TenantOperation,
    SifenTransmission,
    SifenQuery,
    Certificate,
    XmlGeneration,
    XmlSignature,
    Persistence,
    BackgroundJob,
    ExternalDependency,
    Configuration
}
