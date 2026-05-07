namespace SifenInvoicing.Application.Security;

public sealed record SecretCheckResult(
    string Name,
    SecretStatus Status,
    string Summary)
{
    public bool IsReady => Status == SecretStatus.Present;
}
