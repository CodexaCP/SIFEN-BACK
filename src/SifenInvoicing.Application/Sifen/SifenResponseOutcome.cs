namespace SifenInvoicing.Application.Sifen;

public enum SifenResponseOutcome
{
    Approved = 1,
    Rejected = 2,
    Observed = 3,
    TechnicalError = 4,
    EmptyResponse = 5,
    InvalidXml = 6,
    Unknown = 7
}
