namespace SifenInvoicing.Application.Onboarding;

public sealed record RegisterTaxpayerProfileCommand(
    Guid TenantId,
    string RucNumber,
    string RucCheckDigit,
    string LegalName,
    string? TradeName);
