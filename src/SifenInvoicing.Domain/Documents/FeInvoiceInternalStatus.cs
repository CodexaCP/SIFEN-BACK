namespace SifenInvoicing.Domain.Documents;

public enum FeInvoiceInternalStatus
{
    DRAFT = 1,
    GENERATED = 2,
    VALIDATED_TEST = 3,
    TEST_ERROR = 4,
    READY_FOR_REAL = 5,
    BLOCKED_BY_CONFIG = 6
}
