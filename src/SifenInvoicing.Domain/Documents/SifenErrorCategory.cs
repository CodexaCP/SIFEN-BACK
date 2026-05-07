namespace SifenInvoicing.Domain.Documents;

public enum SifenErrorCategory
{
    Configuration = 1,
    InvoiceData = 2,
    CertificateSignature = 3,
    SifenTransport = 4,
    SifenRejected = 5,
    PlanLimit = 6,
    InternalSystem = 7
}
