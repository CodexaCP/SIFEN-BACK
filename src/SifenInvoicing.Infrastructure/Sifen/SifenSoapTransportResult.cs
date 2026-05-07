namespace SifenInvoicing.Infrastructure.Sifen;

public sealed record SifenSoapTransportResult(
    bool IsSuccessStatusCode,
    int? HttpStatusCode,
    string? ResponseBody);
