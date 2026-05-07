using SifenInvoicing.Domain.Documents;

namespace SifenInvoicing.Application.Sifen;

public sealed record ParsedSifenResponse(
    SifenResponseOutcome Outcome,
    SifenDocumentStatus StatusHint,
    bool IsSuccessful,
    bool IsFinal,
    string? Cdc,
    string? TrackingId,
    string? StatusCode,
    string? StatusMessage,
    string? TechnicalMessage);
