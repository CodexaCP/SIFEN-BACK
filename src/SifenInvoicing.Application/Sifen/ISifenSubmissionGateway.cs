using SifenInvoicing.Domain.Tenants;

namespace SifenInvoicing.Application.Sifen;

public interface ISifenSubmissionGateway
{
    Task<SifenSubmissionResult> SendToSifenAsync(
        SendToSifenCommand command,
        CancellationToken cancellationToken = default);
}
