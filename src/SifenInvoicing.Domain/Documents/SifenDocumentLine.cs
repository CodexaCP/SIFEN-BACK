using SifenInvoicing.Domain.Common;

namespace SifenInvoicing.Domain.Documents;

public sealed class SifenDocumentLine : TenantScopedEntity
{
    private SifenDocumentLine()
    {
        Description = string.Empty;
    }

    private SifenDocumentLine(
        Guid id,
        Guid tenantId,
        Guid documentId,
        int lineNumber,
        string description,
        decimal quantity,
        decimal unitPrice,
        int vatRate,
        decimal vatAmount,
        decimal exemptAmount,
        decimal subtotalAmount,
        decimal totalAmount)
        : base(id, tenantId)
    {
        DocumentId = documentId == Guid.Empty
            ? throw new DomainException("documentId is required.")
            : documentId;
        LineNumber = lineNumber > 0
            ? lineNumber
            : throw new DomainException("lineNumber must be greater than zero.");
        Description = RequireValue(description, nameof(description), 500);
        Quantity = quantity > 0m
            ? quantity
            : throw new DomainException("quantity must be greater than zero.");
        UnitPrice = unitPrice >= 0m
            ? unitPrice
            : throw new DomainException("unitPrice must be greater than or equal to zero.");
        VatRate = vatRate is 10 or 5 or 0
            ? vatRate
            : throw new DomainException("vatRate must be 10, 5 or 0.");
        VatAmount = vatAmount >= 0m ? vatAmount : throw new DomainException("vatAmount must be greater than or equal to zero.");
        ExemptAmount = exemptAmount >= 0m ? exemptAmount : throw new DomainException("exemptAmount must be greater than or equal to zero.");
        SubtotalAmount = subtotalAmount >= 0m ? subtotalAmount : throw new DomainException("subtotalAmount must be greater than or equal to zero.");
        TotalAmount = totalAmount >= 0m ? totalAmount : throw new DomainException("totalAmount must be greater than or equal to zero.");
    }

    public Guid DocumentId { get; private set; }

    public int LineNumber { get; private set; }

    public string Description { get; private set; }

    public decimal Quantity { get; private set; }

    public decimal UnitPrice { get; private set; }

    public int VatRate { get; private set; }

    public decimal VatAmount { get; private set; }

    public decimal ExemptAmount { get; private set; }

    public decimal SubtotalAmount { get; private set; }

    public decimal TotalAmount { get; private set; }

    public static SifenDocumentLine Create(
        Guid tenantId,
        Guid documentId,
        int lineNumber,
        string description,
        decimal quantity,
        decimal unitPrice,
        int vatRate,
        decimal vatAmount,
        decimal exemptAmount,
        decimal subtotalAmount,
        decimal totalAmount)
    {
        return new SifenDocumentLine(
            Guid.NewGuid(),
            tenantId,
            documentId,
            lineNumber,
            description,
            quantity,
            unitPrice,
            vatRate,
            vatAmount,
            exemptAmount,
            subtotalAmount,
            totalAmount);
    }

    private static string RequireValue(string value, string parameterName, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException($"{parameterName} is required.");
        }

        var normalized = value.Trim();
        if (normalized.Length > maxLength)
        {
            throw new DomainException($"{parameterName} cannot exceed {maxLength} characters.");
        }

        return normalized;
    }
}
