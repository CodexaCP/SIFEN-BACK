using SifenInvoicing.Domain.Tenants;
using SifenInvoicing.Application.Invoices;

namespace SifenInvoicing.Infrastructure.Invoices;

public interface IInvoiceKudePdfRenderer
{
    string RenderHtml(InvoiceDetail invoice, TaxpayerProfile? taxpayerProfile, TenantKudeTemplateSettings? templateSettings);

    InvoiceKudePdfResult Render(InvoiceDetail invoice, TaxpayerProfile? taxpayerProfile, TenantKudeTemplateSettings? templateSettings);

    string RenderPreviewHtml(TenantKudeTemplateSettings? templateSettings);

    InvoiceKudePdfResult RenderPreviewPdf(TenantKudeTemplateSettings? templateSettings);
}
