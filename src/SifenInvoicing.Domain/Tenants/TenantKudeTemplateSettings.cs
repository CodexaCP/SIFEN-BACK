using SifenInvoicing.Domain.Common;

namespace SifenInvoicing.Domain.Tenants;

public sealed class TenantKudeTemplateSettings : TenantScopedEntity
{
    private const string DefaultTemplateCode = "codexa-standard";
    private const string DefaultPrimaryColor = "#2D9CDB";
    private const string DefaultSecondaryColor = "#EAF6FD";
    private const string DefaultFooterText = "Consulte este comprobante en la SET";

    private TenantKudeTemplateSettings()
    {
        TemplateCode = DefaultTemplateCode;
        PrimaryColor = DefaultPrimaryColor;
        SecondaryColor = DefaultSecondaryColor;
        FooterText = DefaultFooterText;
    }

    private TenantKudeTemplateSettings(
        Guid id,
        Guid tenantId,
        string templateCode,
        string? logoUrl,
        string? primaryColor,
        string? secondaryColor,
        string? footerText,
        bool showPhone,
        bool showEmail)
        : base(id, tenantId)
    {
        TemplateCode = NormalizeTemplateCode(templateCode);
        LogoUrl = NormalizeOptional(logoUrl);
        PrimaryColor = NormalizeColor(primaryColor, DefaultPrimaryColor);
        SecondaryColor = NormalizeColor(secondaryColor, DefaultSecondaryColor);
        FooterText = NormalizeOptional(footerText) ?? DefaultFooterText;
        ShowPhone = showPhone;
        ShowEmail = showEmail;
    }

    public string TemplateCode { get; private set; }

    public string? LogoUrl { get; private set; }

    public string PrimaryColor { get; private set; }

    public string SecondaryColor { get; private set; }

    public string FooterText { get; private set; }

    public bool ShowPhone { get; private set; }

    public bool ShowEmail { get; private set; }

    public static TenantKudeTemplateSettings CreateDefault(Guid tenantId)
        => new(Guid.NewGuid(), tenantId, DefaultTemplateCode, null, DefaultPrimaryColor, DefaultSecondaryColor, DefaultFooterText, true, true);

    public static TenantKudeTemplateSettings Create(
        Guid tenantId,
        string templateCode,
        string? logoUrl,
        string? primaryColor,
        string? secondaryColor,
        string? footerText,
        bool showPhone,
        bool showEmail)
        => new(Guid.NewGuid(), tenantId, templateCode, logoUrl, primaryColor, secondaryColor, footerText, showPhone, showEmail);

    public void Update(
        string templateCode,
        string? logoUrl,
        string? primaryColor,
        string? secondaryColor,
        string? footerText,
        bool showPhone,
        bool showEmail)
    {
        TemplateCode = NormalizeTemplateCode(templateCode);
        LogoUrl = NormalizeOptional(logoUrl);
        PrimaryColor = NormalizeColor(primaryColor, DefaultPrimaryColor);
        SecondaryColor = NormalizeColor(secondaryColor, DefaultSecondaryColor);
        FooterText = NormalizeOptional(footerText) ?? DefaultFooterText;
        ShowPhone = showPhone;
        ShowEmail = showEmail;
    }

    private static string NormalizeTemplateCode(string? value)
    {
        var normalized = NormalizeOptional(value)?.ToLowerInvariant();
        return normalized switch
        {
            null => DefaultTemplateCode,
            "codexa-standard" => normalized,
            _ => throw new DomainException("KuDE template is not supported.")
        };
    }

    private static string NormalizeColor(string? value, string fallback)
    {
        var normalized = NormalizeOptional(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return fallback;
        }

        if (normalized.Length == 7 && normalized[0] == '#')
        {
            return normalized.ToUpperInvariant();
        }

        throw new DomainException("KuDE color must use #RRGGBB format.");
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
