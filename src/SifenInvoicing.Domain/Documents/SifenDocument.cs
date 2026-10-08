using SifenInvoicing.Domain.Common;
using SifenInvoicing.Domain.Tenants;

namespace SifenInvoicing.Domain.Documents;

public sealed class SifenDocument : TenantScopedEntity
{
    private SifenDocument()
    {
        Cdc = string.Empty;
        DocumentType = string.Empty;
        ExternalDocumentNumber = string.Empty;
        EstablishmentCode = string.Empty;
        ExpeditionPointCode = string.Empty;
        CurrencyCode = string.Empty;
        SaleCondition = string.Empty;
        ReceiverName = string.Empty;
        ReceiverDocument = string.Empty;
        XmlPayload = string.Empty;
        InternalStatus = FeInvoiceInternalStatus.DRAFT;
        IsFiscalPreviewValid = false;
    }

    private SifenDocument(
        Guid id,
        Guid tenantId,
        SifenEnvironmentType environment,
        SifenDocumentKind kind,
        string cdc,
        string documentType,
        string externalDocumentNumber,
        string establishmentCode,
        string expeditionPointCode,
        string currencyCode,
        string saleCondition,
        string receiverName,
        string receiverDocument,
        string? receiverAddress,
        string? receiverEmail,
        string? receiverPhone,
        string? notes,
        decimal subtotalAmount,
        decimal vat5Amount,
        decimal vat10Amount,
        decimal exemptAmount,
        decimal totalVatAmount,
        decimal totalAmount,
        string xmlPayload,
        string? testCdc,
        string? testQrText,
        bool isFiscalPreviewValid,
        DateTimeOffset issuedAt)
        : base(id, tenantId)
    {
        Environment = environment;
        Kind = kind;
        Cdc = RequireValue(cdc, nameof(cdc), 44);
        DocumentType = RequireValue(documentType, nameof(documentType), 80);
        ExternalDocumentNumber = RequireValue(externalDocumentNumber, nameof(externalDocumentNumber), 20);
        EstablishmentCode = RequireValue(establishmentCode, nameof(establishmentCode), 10);
        ExpeditionPointCode = RequireValue(expeditionPointCode, nameof(expeditionPointCode), 10);
        CurrencyCode = RequireValue(currencyCode, nameof(currencyCode), 10);
        SaleCondition = RequireValue(saleCondition, nameof(saleCondition), 80);
        ReceiverName = RequireValue(receiverName, nameof(receiverName), 250);
        ReceiverDocument = RequireValue(receiverDocument, nameof(receiverDocument), 30);
        ReceiverAddress = NormalizeWithLimit(receiverAddress, 500);
        ReceiverEmail = NormalizeWithLimit(receiverEmail, 250);
        ReceiverPhone = NormalizeWithLimit(receiverPhone, 60);
        Notes = NormalizeWithLimit(notes, 1000);
        SubtotalAmount = EnsureNonNegative(subtotalAmount, nameof(subtotalAmount));
        Vat5Amount = EnsureNonNegative(vat5Amount, nameof(vat5Amount));
        Vat10Amount = EnsureNonNegative(vat10Amount, nameof(vat10Amount));
        ExemptAmount = EnsureNonNegative(exemptAmount, nameof(exemptAmount));
        TotalVatAmount = EnsureNonNegative(totalVatAmount, nameof(totalVatAmount));
        TotalAmount = totalAmount >= 0m
            ? totalAmount
            : throw new DomainException("totalAmount must be greater than or equal to zero.");
        XmlPayload = RequireValue(xmlPayload, nameof(xmlPayload));
        TestCdc = NormalizeWithLimit(testCdc, 120);
        TestQrText = NormalizeWithLimit(testQrText, 500);
        IsFiscalPreviewValid = isFiscalPreviewValid;
        Status = SifenDocumentStatus.DraftGenerated;
        InternalStatus = FeInvoiceInternalStatus.DRAFT;
        IssuedAt = issuedAt;
    }

    public SifenEnvironmentType Environment { get; private set; }

    public SifenDocumentKind Kind { get; private set; }

    public string Cdc { get; private set; }

    public string DocumentType { get; private set; }

    public string ExternalDocumentNumber { get; private set; }

    public string EstablishmentCode { get; private set; }

    public string ExpeditionPointCode { get; private set; }

    public string CurrencyCode { get; private set; }

    public string SaleCondition { get; private set; }

    public string? Notes { get; private set; }

    public string ReceiverName { get; private set; }

    public string ReceiverDocument { get; private set; }

    public string? ReceiverAddress { get; private set; }

    public string? ReceiverEmail { get; private set; }

    public string? ReceiverPhone { get; private set; }

    public decimal SubtotalAmount { get; private set; }

    public decimal Vat5Amount { get; private set; }

    public decimal Vat10Amount { get; private set; }

    public decimal ExemptAmount { get; private set; }

    public decimal TotalVatAmount { get; private set; }

    public decimal TotalAmount { get; private set; }

    public string? TestCdc { get; private set; }

    public string? TestQrText { get; private set; }

    public bool IsFiscalPreviewValid { get; private set; }

    public string XmlPayload { get; private set; }

    public string? SignedXmlPayload { get; private set; }

    public string? LastSubmissionEndpoint { get; private set; }

    public string? SifenTrackingId { get; private set; }

    public string? StatusCode { get; private set; }

    public string? StatusMessage { get; private set; }

    public string? RawSifenResponse { get; private set; }

    public SifenDocumentStatus Status { get; private set; }

    public string? CorrelationId { get; private set; }

    public FeInvoiceInternalStatus InternalStatus { get; private set; }

    public int RetryCount { get; private set; }

    public bool IsRetryable { get; private set; }

    public string? LastErrorCode { get; private set; }

    public string? LastErrorMessage { get; private set; }

    public DateTimeOffset IssuedAt { get; private set; }

    /// <summary>Estado de transmision hacia SIFEN (interno de la aplicacion, independiente de Status e InternalStatus).</summary>
    public SifenTransmissionState TransmissionState { get; private set; }

    /// <summary>Estado fiscal conocido del DE en SIFEN (interno de la aplicacion).</summary>
    public SifenFiscalState FiscalState { get; private set; }

    public DateTimeOffset? SignedAt { get; private set; }

    public DateTimeOffset? SubmittedAt { get; private set; }

    public DateTimeOffset? FinalizedAt { get; private set; }

    /// <summary>dNumTim del timbrado con el que se numero el documento (null en documentos previos a la numeracion por timbrado).</summary>
    public string? StampingNumber { get; private set; }

    public Guid? NumberingSequenceId { get; private set; }

    public void SetSifenStates(SifenTransmissionState transmissionState, SifenFiscalState fiscalState)
    {
        TransmissionState = transmissionState;
        FiscalState = fiscalState;
    }

    public void SetFiscalTrace(string stampingNumber, Guid numberingSequenceId)
    {
        StampingNumber = stampingNumber;
        NumberingSequenceId = numberingSequenceId;
    }

    public static SifenDocument CreateInvoice(
        Guid tenantId,
        SifenEnvironmentType environment,
        string cdc,
        string documentType,
        string externalDocumentNumber,
        string establishmentCode,
        string expeditionPointCode,
        string currencyCode,
        string saleCondition,
        string receiverName,
        string receiverDocument,
        string? receiverAddress,
        string? receiverEmail,
        string? receiverPhone,
        string? notes,
        decimal subtotalAmount,
        decimal vat5Amount,
        decimal vat10Amount,
        decimal exemptAmount,
        decimal totalVatAmount,
        decimal totalAmount,
        string xmlPayload,
        string? testCdc,
        string? testQrText,
        bool isFiscalPreviewValid,
        DateTimeOffset issuedAt)
    {
        return new SifenDocument(
            Guid.NewGuid(),
            tenantId,
            environment,
            SifenDocumentKind.Invoice,
            cdc,
            documentType,
            externalDocumentNumber,
            establishmentCode,
            expeditionPointCode,
            currencyCode,
            saleCondition,
            receiverName,
            receiverDocument,
            receiverAddress,
            receiverEmail,
            receiverPhone,
            notes,
            subtotalAmount,
            vat5Amount,
            vat10Amount,
            exemptAmount,
            totalVatAmount,
            totalAmount,
            xmlPayload,
            testCdc,
            testQrText,
            isFiscalPreviewValid,
            issuedAt);
    }

    public void MarkSigned(string signedXmlPayload, DateTimeOffset signedAt)
    {
        SignedXmlPayload = RequireValue(signedXmlPayload, nameof(signedXmlPayload));
        SignedAt = signedAt;
        Status = SifenDocumentStatus.Signed;
    }

    public void MarkPendingSubmission(string submissionEndpoint, DateTimeOffset submittedAt)
    {
        LastSubmissionEndpoint = RequireValue(submissionEndpoint, nameof(submissionEndpoint));
        SubmittedAt = submittedAt;
        Status = SifenDocumentStatus.PendingSubmission;
    }

    public void MarkSubmitted(
        string? trackingId,
        string? statusCode,
        string? statusMessage,
        string? rawResponse,
        DateTimeOffset submittedAt)
    {
        SifenTrackingId = Normalize(trackingId);
        StatusCode = Normalize(statusCode);
        StatusMessage = Normalize(statusMessage);
        RawSifenResponse = Normalize(rawResponse);
        SubmittedAt = submittedAt;
        Status = SifenDocumentStatus.Submitted;
    }

    public void MarkAccepted(
        string? trackingId,
        string? statusCode,
        string? statusMessage,
        string? rawResponse,
        DateTimeOffset finalizedAt)
    {
        SifenTrackingId = Normalize(trackingId);
        StatusCode = Normalize(statusCode);
        StatusMessage = Normalize(statusMessage);
        RawSifenResponse = Normalize(rawResponse);
        FinalizedAt = finalizedAt;
        Status = SifenDocumentStatus.Accepted;
    }

    public void MarkRejected(
        string? statusCode,
        string? statusMessage,
        string? rawResponse,
        DateTimeOffset finalizedAt)
    {
        StatusCode = Normalize(statusCode);
        StatusMessage = Normalize(statusMessage);
        RawSifenResponse = Normalize(rawResponse);
        FinalizedAt = finalizedAt;
        Status = SifenDocumentStatus.Rejected;
    }

    public void MarkFailed(
        string? statusCode,
        string? statusMessage,
        string? rawResponse,
        DateTimeOffset finalizedAt)
    {
        StatusCode = Normalize(statusCode);
        StatusMessage = Normalize(statusMessage);
        RawSifenResponse = Normalize(rawResponse);
        FinalizedAt = finalizedAt;
        Status = SifenDocumentStatus.Failed;
    }

    public string EnsureCorrelationId(string? correlationId = null)
    {
        if (!string.IsNullOrWhiteSpace(CorrelationId))
        {
            return CorrelationId;
        }

        CorrelationId = RequireMaxLength(
            string.IsNullOrWhiteSpace(correlationId) ? Guid.NewGuid().ToString("N") : correlationId.Trim(),
            nameof(correlationId),
            80);
        return CorrelationId;
    }

    public void SetInternalStatus(
        FeInvoiceInternalStatus internalStatus,
        string? lastErrorCode = null,
        string? lastErrorMessage = null,
        bool isRetryable = false)
    {
        InternalStatus = internalStatus;
        LastErrorCode = NormalizeWithLimit(lastErrorCode, 80);
        LastErrorMessage = NormalizeWithLimit(lastErrorMessage, 500);
        IsRetryable = isRetryable;
    }

    public void IncrementRetryCount()
    {
        RetryCount++;
    }

    public void MarkInternalValidation(
        string? statusCode,
        string? statusMessage,
        DateTimeOffset finalizedAt)
    {
        StatusCode = Normalize(statusCode);
        StatusMessage = Normalize(statusMessage);
        RawSifenResponse = null;
        SifenTrackingId = null;
        FinalizedAt = finalizedAt;
        Status = SifenDocumentStatus.InternalValidation;
    }

    public void MarkInternalValidationFailed(
        string? statusCode,
        string? statusMessage,
        string? rawDetail,
        DateTimeOffset finalizedAt)
    {
        StatusCode = Normalize(statusCode);
        StatusMessage = Normalize(statusMessage);
        RawSifenResponse = Normalize(rawDetail);
        SifenTrackingId = null;
        FinalizedAt = finalizedAt;
        Status = SifenDocumentStatus.InternalValidationFailed;
    }

    public void MarkDraftValidatedWithoutSignature(
        string? statusCode,
        string? statusMessage,
        string? rawDetail,
        DateTimeOffset finalizedAt)
    {
        StatusCode = Normalize(statusCode);
        StatusMessage = Normalize(statusMessage);
        RawSifenResponse = Normalize(rawDetail);
        SifenTrackingId = null;
        FinalizedAt = finalizedAt;
        Status = SifenDocumentStatus.DraftValidatedWithoutSignature;
    }

    private static string RequireValue(string value, string parameterName, int? exactLength = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException($"{parameterName} is required.");
        }

        var normalized = value.Trim();
        if (exactLength.HasValue && normalized.Length != exactLength.Value)
        {
            throw new DomainException($"{parameterName} must have exactly {exactLength.Value} characters.");
        }

        return normalized;
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

    private static string RequireMaxLength(string value, string parameterName, int maxLength)
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

    private static decimal EnsureNonNegative(decimal value, string parameterName)
    {
        return value >= 0m
            ? value
            : throw new DomainException($"{parameterName} must be greater than or equal to zero.");
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string? NormalizeWithLimit(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length <= maxLength
            ? normalized
            : normalized[..maxLength];
    }
}
