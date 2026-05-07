namespace SifenInvoicing.Application.Operations;

public enum DependencyKind
{
    InternalApi,
    SifenEndpointConfiguration,
    SifenCertificateConfiguration,
    SqlServer,
    BackgroundWorkers
}
