namespace SifenInvoicing.Application.Sifen;

public sealed record SifenSubmissionResult(
    bool IsAcceptedByGateway,
    string Endpoint,
    string? TrackingId,
    string? RawResponse,
    string? RequestPayload = null,
    string? TransportCode = null,
    string? TransportMessage = null,
    int? HttpStatusCode = null,
    bool IsDiagnostic = false);
