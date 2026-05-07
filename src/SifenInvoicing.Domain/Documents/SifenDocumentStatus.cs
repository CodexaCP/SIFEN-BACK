namespace SifenInvoicing.Domain.Documents;

public enum SifenDocumentStatus
{
    Draft = 1,
    DraftGenerated = 1,
    ReadyForSifenTest = 2,
    Signed = 2,
    PendingSubmission = 3,
    Submitted = 4,
    Approved = 5,
    Accepted = 5,
    Rejected = 6,
    RetryableError = 7,
    Failed = 7,
    InternalValidation = 8,
    BlockedByConfiguration = 9,
    InternalValidationFailed = 9,
    DraftValidatedWithoutSignature = 10,
    BlockedByPlan = 11,
    SystemError = 12
}
