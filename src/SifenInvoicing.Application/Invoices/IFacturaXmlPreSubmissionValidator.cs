using SifenInvoicing.Domain.Tenants;

namespace SifenInvoicing.Application.Invoices;

public interface IFacturaXmlPreSubmissionValidator
{
    Task ValidateTipoDoc01Async(
        string xml,
        string cdc,
        Guid tenantId,
        SifenEnvironmentType environment,
        CancellationToken cancellationToken = default);
}
