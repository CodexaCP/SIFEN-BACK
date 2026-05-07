namespace SifenInvoicing.Domain.Common;

public sealed class UserFacingException : DomainException
{
    public UserFacingException(
        string errorCode,
        string category,
        string userMessage,
        string suggestedAction,
        bool isRetryable,
        int httpStatusCode = 400,
        string? technicalMessage = null)
        : base(userMessage)
    {
        ErrorCode = errorCode;
        Category = category;
        UserMessage = userMessage;
        SuggestedAction = suggestedAction;
        IsRetryable = isRetryable;
        HttpStatusCode = httpStatusCode;
        TechnicalMessage = technicalMessage;
    }

    public string ErrorCode { get; }

    public string Category { get; }

    public string UserMessage { get; }

    public string SuggestedAction { get; }

    public bool IsRetryable { get; }

    public int HttpStatusCode { get; }

    public string? TechnicalMessage { get; }
}
