using System.Globalization;
using System.Text;
using System.Xml.Linq;
using SifenInvoicing.Application.Invoices;
using SifenInvoicing.Domain.Common;
using SifenInvoicing.Domain.Tenants;

namespace SifenInvoicing.Infrastructure.Invoices;

public sealed class InvoiceKudePdfRenderer : IInvoiceKudePdfRenderer
{
    private static readonly XNamespace SifenNamespace = "http://ekuatia.set.gov.py/sifen/xsd";
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private const string DefaultLogoText = "Codexa";
    private const string DefaultPhone = "+595 21 123 456";
    private const string DefaultEmail = "hola@codexa.com.py";
    private const string DefaultAddress = "Av. Mariscal Lopez 1234, Asuncion";
    private const string PreviewNotice = "Vista previa con datos de prueba. No es comprobante válido.";

    public string RenderHtml(InvoiceDetail invoice, TaxpayerProfile? taxpayerProfile, TenantKudeTemplateSettings? templateSettings)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        var model = BuildModel(invoice, taxpayerProfile, templateSettings);
        return RenderHtml(model);
    }

    public InvoiceKudePdfResult Render(InvoiceDetail invoice, TaxpayerProfile? taxpayerProfile, TenantKudeTemplateSettings? templateSettings)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        var model = BuildModel(invoice, taxpayerProfile, templateSettings);
        var html = RenderHtml(model);
        var pdfBytes = BuildPdf(model);

        return new InvoiceKudePdfResult(
            $"kude-fe-{invoice.ExternalDocumentNumber}.pdf",
            pdfBytes,
            model.QrPayload,
            true);
    }

    public string RenderPreviewHtml(TenantKudeTemplateSettings? templateSettings)
    {
        var model = BuildPreviewModel(templateSettings);
        return RenderHtml(model);
    }

    public InvoiceKudePdfResult RenderPreviewPdf(TenantKudeTemplateSettings? templateSettings)
    {
        var model = BuildPreviewModel(templateSettings);
        var html = RenderHtml(model);
        var pdfBytes = BuildPdf(model);

        return new InvoiceKudePdfResult(
            "kude-codexa-standard-demo.pdf",
            pdfBytes,
            model.QrPayload,
            true);
    }

    private static KudeDocumentModel BuildModel(InvoiceDetail invoice, TaxpayerProfile? taxpayerProfile, TenantKudeTemplateSettings? templateSettings)
    {
        var summary = ParseInvoiceSummary(invoice.XmlPayload);
        var settings = ResolveTemplateSettings(templateSettings);
        var issuerName = taxpayerProfile?.LegalName ?? summary.IssuerName ?? "Emisor no informado";
        var issuerRuc = taxpayerProfile is null ? "RUC no informado" : $"{taxpayerProfile.RucNumber}-{taxpayerProfile.RucCheckDigit}";

        return new KudeDocumentModel(
            settings.TemplateCode,
            settings.LogoUrl,
            issuerName,
            issuerRuc,
            summary.IssuerAddress ?? DefaultAddress,
            settings.ShowPhone ? DefaultPhone : null,
            settings.ShowEmail ? DefaultEmail : null,
            "FACTURA ELECTRONICA",
            invoice.Cdc,
            TruncateCdc(invoice.Cdc),
            summary.StampingNumber ?? "12345678",
            invoice.EstablishmentCode,
            invoice.ExpeditionPointCode,
            invoice.ExternalDocumentNumber,
            invoice.IssuedAt.ToString("dd/MM/yyyy HH:mm:ss", Invariant),
            invoice.ReceiverName,
            invoice.ReceiverDocument,
            summary.CustomerAddress ?? "No informado",
            summary.SaleCondition ?? "Contado",
            summary.Items.Take(20).ToArray(),
            summary.Subtotal,
            summary.TotalVat10,
            summary.TotalVat5,
            summary.TotalGeneral,
            summary.TotalInWords ?? "Total sujeto a validacion del XML FE.",
            summary.Observations ?? "Documento generado desde el motor FE.",
            settings.PrimaryColor,
            settings.SecondaryColor,
            settings.FooterText,
            invoice.Cdc,
            summary.IsPreview);
    }

    private static KudeDocumentModel BuildPreviewModel(TenantKudeTemplateSettings? templateSettings)
    {
        var settings = ResolveTemplateSettings(templateSettings);
        var cdc = "0180012345678000101000001202604290000000000000001";
        return new KudeDocumentModel(
            settings.TemplateCode,
            settings.LogoUrl,
            "Empresa Demo S.A.",
            "80000000-1",
            "Av. Mariscal Lopez 1234 casi San Martin, Asuncion, Paraguay",
            settings.ShowPhone ? DefaultPhone : null,
            settings.ShowEmail ? DefaultEmail : null,
            "FACTURA ELECTRONICA",
            cdc,
            TruncateCdc(cdc),
            "12345678",
            "001",
            "001",
            "0000001",
            "29/04/2026 10:30:00",
            "Juan Perez",
            "1234567-8",
            "Calle Falsa 123 c/ Siempre Viva, Asuncion",
            "Contado",
            [
                new KudeItem("Servicio tecnico", "1", "100.000", "10%", "100.000")
            ],
            "100.000",
            "10.000",
            "0",
            "100.000",
            "Cien mil guaranies.",
            "Vista previa para seleccion de plantilla Codexa Standard.",
            settings.PrimaryColor,
            settings.SecondaryColor,
            settings.FooterText,
            cdc,
            true);
    }

    private static ResolvedTemplateSettings ResolveTemplateSettings(TenantKudeTemplateSettings? settings)
    {
        return new ResolvedTemplateSettings(
            settings?.TemplateCode ?? "codexa-standard",
            settings?.LogoUrl,
            settings?.PrimaryColor ?? "#2D9CDB",
            settings?.SecondaryColor ?? "#EAF6FD",
            settings?.FooterText ?? "Consulte este comprobante en la SET",
            settings?.ShowPhone ?? true,
            settings?.ShowEmail ?? true);
    }

    private static InvoiceSummary ParseInvoiceSummary(string xmlPayload)
    {
        var document = XDocument.Parse(xmlPayload, LoadOptions.PreserveWhitespace);
        var de = document.Root?.Element(SifenNamespace + "DE")
            ?? throw new DomainException("KuDE cannot be generated because FE XML does not contain DE.");
        var totals = de.Element(SifenNamespace + "gTotSub")
            ?? throw new DomainException("KuDE cannot be generated because FE XML does not contain totals.");
        // Estructura oficial v150 (DE_v150.xsd): gDtipDE/gCamItem*, gTimb/dNumTim, gEmis/dNomEmi, gDatRec/dDirRec (opcional).
        var typeSpecific = de.Element(SifenNamespace + "gDtipDE");
        var itemNodes = typeSpecific?.Elements(SifenNamespace + "gCamItem").ToList() ?? [];
        if (itemNodes.Count == 0)
        {
            throw new DomainException("KuDE cannot be generated because FE XML does not contain items.");
        }

        var items = itemNodes.Select(node => new KudeItem(
            node.Element(SifenNamespace + "dDesProSer")?.Value?.Trim() ?? string.Empty,
            FormatQuantity(ParseDecimal(node.Element(SifenNamespace + "dCantProSer")?.Value)),
            FormatMoney(ParseDecimal(node.Element(SifenNamespace + "gValorItem")?.Element(SifenNamespace + "dPUniProSer")?.Value)),
            $"{node.Element(SifenNamespace + "gCamIVA")?.Element(SifenNamespace + "dTasaIVA")?.Value?.Trim() ?? "0"}%",
            FormatMoney(ParseDecimal(node.Element(SifenNamespace + "gValorItem")?.Element(SifenNamespace + "gValorRestaItem")?.Element(SifenNamespace + "dTotOpeItem")?.Value)))).ToList();

        var issuer = de.Element(SifenNamespace + "gDatGralOpe")?.Element(SifenNamespace + "gEmis");
        var customer = de.Element(SifenNamespace + "gDatGralOpe")?.Element(SifenNamespace + "gDatRec");

        return new InvoiceSummary(
            issuer?.Element(SifenNamespace + "dNomEmi")?.Value?.Trim(),
            issuer?.Element(SifenNamespace + "dDirEmi")?.Value?.Trim(),
            de.Element(SifenNamespace + "gTimb")?.Element(SifenNamespace + "dNumTim")?.Value?.Trim(),
            customer?.Element(SifenNamespace + "dDirRec")?.Value?.Trim(),
            typeSpecific?.Element(SifenNamespace + "gCamCond")?.Element(SifenNamespace + "iCondOpe")?.Value?.Trim() == "1" ? "Contado" : "Credito",
            items,
            FormatMoney(ParseDecimal(totals.Element(SifenNamespace + "dSubExe")?.Value) + ParseDecimal(totals.Element(SifenNamespace + "dSub5")?.Value) + ParseDecimal(totals.Element(SifenNamespace + "dSub10")?.Value)),
            FormatMoney(ParseDecimal(totals.Element(SifenNamespace + "dIVA5")?.Value)),
            FormatMoney(ParseDecimal(totals.Element(SifenNamespace + "dIVA10")?.Value)),
            FormatMoney(ParseDecimal(totals.Element(SifenNamespace + "dTotGralOpe")?.Value)),
            "Representacion impresa del comprobante electronico generado por SIFEN.",
            "Documento procesado desde el motor FE.",
            false);
    }

    private static string RenderHtml(KudeDocumentModel model)
    {
        var qrSvg = BuildQrSvg(model.QrPayload);
        var html = new StringBuilder();
        html.Append("""
<!DOCTYPE html>
<html lang="es">
<head>
  <meta charset="utf-8" />
  <title>KuDE Codexa Standard</title>
</head>
<body style="margin:0;background:#f5f8fc;font-family:Inter,Roboto,Arial,sans-serif;color:#2F2F2F;">
""");
        html.Append("<div style=\"max-width:1120px;margin:0 auto;padding:32px;background:#ffffff;\">");
        html.Append("<div style=\"display:flex;justify-content:space-between;gap:32px;border-bottom:1px solid #E5E7EB;padding-bottom:24px;\">");
        html.Append("<div style=\"width:46%;\">");
        if (!string.IsNullOrWhiteSpace(model.LogoUrl))
        {
            html.Append("<img alt=\"Logo\" src=\"").Append(Html(model.LogoUrl!)).Append("\" style=\"max-width:160px;max-height:60px;object-fit:contain;display:block;margin-bottom:20px;\" />");
        }
        else
        {
            html.Append("<div style=\"display:inline-block;padding:12px 18px;border-radius:18px;background:linear-gradient(90deg,#2D9CDB,#7B61FF);color:#fff;font-size:28px;font-weight:800;margin-bottom:20px;\">")
                .Append(DefaultLogoText)
                .Append("</div>");
        }

        html.Append("<div style=\"font-size:32px;font-weight:800;margin-bottom:12px;\">").Append(Html(model.IssuerName)).Append("</div>");
        html.Append("<div style=\"font-size:18px;font-weight:700;margin-bottom:14px;\">RUC: ").Append(Html(model.IssuerRuc)).Append("</div>");
        html.Append("<div style=\"color:#4B5563;font-size:16px;line-height:1.55;\">").Append(Html(model.IssuerAddress)).Append("</div>");
        if (!string.IsNullOrWhiteSpace(model.Phone))
        {
            html.Append("<div style=\"margin-top:10px;color:#4B5563;font-size:16px;\">").Append(Html(model.Phone!)).Append("</div>");
        }
        if (!string.IsNullOrWhiteSpace(model.Email))
        {
            html.Append("<div style=\"margin-top:8px;color:#4B5563;font-size:16px;\">").Append(Html(model.Email!)).Append("</div>");
        }
        html.Append("</div>");

        html.Append("<div style=\"width:54%;padding-left:28px;border-left:2px solid ").Append(Html(model.PrimaryColor)).Append(";\">");
        html.Append("<div style=\"font-size:22px;font-weight:800;margin-bottom:24px;\">").Append(Html(model.DocumentTitle)).Append("</div>");
        html.Append(MetaRow("CDC", model.CdcVisual));
        html.Append(MetaRow("Timbrado", model.StampingNumber));
        html.Append("<div style=\"display:flex;gap:24px;\">");
        html.Append("<div style=\"flex:1;\">").Append(MetaRow("Establecimiento", model.EstablishmentCode)).Append("</div>");
        html.Append("<div style=\"flex:1;\">").Append(MetaRow("Punto de expedicion", model.ExpeditionPointCode)).Append("</div>");
        html.Append("</div>");
        html.Append(MetaRow("Numero de documento", model.DocumentNumber));
        html.Append(MetaRow("Fecha de emision", model.IssueDate));
        html.Append("</div></div>");

        html.Append("<div style=\"margin-top:28px;padding:24px;border-radius:24px;background:")
            .Append(Html(model.SecondaryColor))
            .Append(";\">");
        html.Append("<div style=\"font-size:16px;font-weight:800;margin-bottom:18px;\">DATOS DEL CLIENTE</div>");
        html.Append(CustomerRow("Nombre / Razon Social", model.CustomerName));
        html.Append(CustomerRow("RUC / CI", model.CustomerDocument));
        html.Append(CustomerRow("Direccion", model.CustomerAddress));
        html.Append(CustomerRow("Condicion de Venta", model.SaleCondition));
        html.Append("</div>");

        html.Append("<table style=\"width:100%;border-collapse:separate;border-spacing:0;margin-top:28px;overflow:hidden;border-radius:22px;\">");
        html.Append("<thead><tr style=\"background:linear-gradient(90deg,").Append(Html(model.PrimaryColor)).Append(",#A855F7);color:#fff;\">")
            .Append("<th style=\"padding:14px 16px;text-align:left;font-size:14px;\">Descripcion</th>")
            .Append("<th style=\"padding:14px 16px;text-align:right;font-size:14px;\">Cant</th>")
            .Append("<th style=\"padding:14px 16px;text-align:right;font-size:14px;\">Precio Unit</th>")
            .Append("<th style=\"padding:14px 16px;text-align:right;font-size:14px;\">IVA</th>")
            .Append("<th style=\"padding:14px 16px;text-align:right;font-size:14px;\">Subtotal</th>")
            .Append("</tr></thead><tbody>");
        foreach (var item in model.Items.Take(20))
        {
            html.Append("<tr style=\"background:#fff;\">")
                .Append("<td style=\"padding:18px 16px;border-bottom:1px solid #E5E7EB;font-size:12px;\">").Append(Html(item.Description)).Append("</td>")
                .Append("<td style=\"padding:18px 16px;border-bottom:1px solid #E5E7EB;font-size:12px;text-align:right;\">").Append(Html(item.Quantity)).Append("</td>")
                .Append("<td style=\"padding:18px 16px;border-bottom:1px solid #E5E7EB;font-size:12px;text-align:right;\">").Append(Html(item.UnitPrice)).Append("</td>")
                .Append("<td style=\"padding:18px 16px;border-bottom:1px solid #E5E7EB;font-size:12px;text-align:right;\">").Append(Html(item.Vat)).Append("</td>")
                .Append("<td style=\"padding:18px 16px;border-bottom:1px solid #E5E7EB;font-size:12px;text-align:right;\">").Append(Html(item.Subtotal)).Append("</td>")
                .Append("</tr>");
        }
        html.Append("</tbody></table>");

        html.Append("<div style=\"display:flex;justify-content:space-between;gap:24px;margin-top:28px;align-items:flex-start;\">");
        html.Append("<div style=\"flex:1;\">")
            .Append("<div style=\"font-weight:700;color:#7B61FF;margin-bottom:8px;\">Total en letras</div>")
            .Append("<div style=\"font-size:12px;line-height:1.6;margin-bottom:24px;\">").Append(Html(model.TotalInWords)).Append("</div>")
            .Append("<div style=\"font-weight:700;color:#7B61FF;margin-bottom:8px;\">Observaciones</div>")
            .Append("<div style=\"font-size:12px;line-height:1.6;\">").Append(Html(model.Observations)).Append("</div>")
            .Append("</div>");

        html.Append("<div style=\"width:360px;border:1px solid #D8E0EA;border-radius:20px;padding:22px 24px;\">")
            .Append(TotalRow("Subtotal", model.Subtotal, false))
            .Append(TotalRow("IVA 10%", model.Iva10, false))
            .Append(TotalRow("IVA 5%", model.Iva5, false))
            .Append("<div style=\"height:1px;background:#E5E7EB;margin:16px 0;\"></div>")
            .Append(TotalRow("TOTAL", model.Total, true))
            .Append("</div></div>");

        html.Append("<div style=\"display:flex;justify-content:space-between;gap:24px;margin-top:34px;padding-top:22px;border-top:1px solid #E5E7EB;align-items:flex-end;\">");
        html.Append("<div style=\"max-width:62%;\">")
            .Append("<div style=\"font-size:13px;font-weight:700;margin-bottom:10px;\">COMPROBANTE ELECTRONICO VALIDO SEGUN RESOLUCION N° 80/2012 DE LA SET</div>")
            .Append("<div style=\"font-size:12px;line-height:1.6;color:#4B5563;margin-bottom:8px;\">").Append(Html(model.FooterText)).Append("</div>")
            .Append("<div style=\"font-size:12px;font-weight:700;\">Consulte este comprobante en la SET</div>")
            .Append("</div>");
        html.Append("<div style=\"text-align:center;\">").Append(qrSvg)
            .Append("<div style=\"font-size:12px;font-weight:700;margin-top:8px;\">Consulte este comprobante en la SET</div>")
            .Append("</div></div>");

        html.Append("<div style=\"margin-top:20px;padding:14px;border:1px dashed #C4B5FD;border-radius:14px;text-align:center;font-size:14px;color:#6B7280;\">")
            .Append(model.IsPreview ? PreviewNotice : "Representacion impresa del comprobante electronico.")
            .Append("</div>");
        html.Append("</div></body></html>");
        return html.ToString();
    }

    private static byte[] BuildPdf(KudeDocumentModel model)
    {
        var qrMatrix = MinimalNumericQrCodeGenerator.GenerateVersion2L(model.QrPayload);
        var commands = new StringBuilder();

        DrawFilledRect(commands, 20, 775, 555, 42, model.PrimaryColor, 0.18m);
        AddText(commands, 34, 802, 18, DefaultLogoText, true, "#2D9CDB");
        AddText(commands, 34, 790, 12, model.DocumentTitle, true);
        AddText(commands, 34, 760, 22, model.IssuerName, true);
        AddText(commands, 34, 742, 12, $"RUC: {model.IssuerRuc}");
        AddWrappedText(commands, 34, 726, 11, model.IssuerAddress, 240, 14);
        if (!string.IsNullOrWhiteSpace(model.Phone))
        {
            AddText(commands, 34, 684, 10, model.Phone!);
        }
        if (!string.IsNullOrWhiteSpace(model.Email))
        {
            AddText(commands, 34, 670, 10, model.Email!);
        }

        AddText(commands, 305, 760, 11, $"CDC: {model.CdcVisual}");
        AddText(commands, 305, 744, 11, $"Timbrado: {model.StampingNumber}");
        AddText(commands, 305, 728, 11, $"Establecimiento: {model.EstablishmentCode}");
        AddText(commands, 305, 712, 11, $"Punto Exp.: {model.ExpeditionPointCode}");
        AddText(commands, 305, 696, 11, $"Numero: {model.DocumentNumber}");
        AddText(commands, 305, 680, 11, $"Fecha: {model.IssueDate}");

        DrawFilledRect(commands, 20, 592, 555, 64, model.SecondaryColor, 1m);
        AddText(commands, 32, 636, 12, "DATOS DEL CLIENTE", true);
        AddText(commands, 32, 618, 11, $"Nombre: {model.CustomerName}");
        AddText(commands, 32, 602, 11, $"RUC / CI: {model.CustomerDocument}");
        AddText(commands, 260, 618, 11, $"Direccion: {model.CustomerAddress}");
        AddText(commands, 260, 602, 11, $"Condicion de venta: {model.SaleCondition}");

        DrawFilledRect(commands, 20, 560, 555, 24, model.PrimaryColor, 1m);
        AddWhiteText(commands, 28, 568, 10, "Descripcion");
        AddWhiteText(commands, 330, 568, 10, "Cant");
        AddWhiteText(commands, 390, 568, 10, "Precio Unit");
        AddWhiteText(commands, 470, 568, 10, "IVA");
        AddWhiteText(commands, 520, 568, 10, "Subtotal");

        var y = 542m;
        foreach (var item in model.Items.Take(20))
        {
            DrawLine(commands, 20, y - 4, 575, y - 4, "#E5E7EB");
            AddText(commands, 28, y, 10, item.Description);
            AddRightAlignedText(commands, 360, y, 10, item.Quantity);
            AddRightAlignedText(commands, 445, y, 10, item.UnitPrice);
            AddRightAlignedText(commands, 500, y, 10, item.Vat);
            AddRightAlignedText(commands, 565, y, 10, item.Subtotal);
            y -= 18;
        }

        AddText(commands, 28, 210, 10, "Total en letras", true);
        AddWrappedText(commands, 28, 196, 10, model.TotalInWords, 260, 12);
        AddText(commands, 28, 164, 10, "Observaciones", true);
        AddWrappedText(commands, 28, 150, 10, model.Observations, 260, 12);

        DrawFilledRect(commands, 350, 160, 225, 88, "#FFFFFF", 1m);
        DrawLine(commands, 350, 248, 225, 0, "#D8E0EA");
        DrawLine(commands, 350, 160, 225, 0, "#D8E0EA");
        DrawLine(commands, 350, 160, 0, 88, "#D8E0EA");
        DrawLine(commands, 575, 160, 0, 88, "#D8E0EA");
        AddText(commands, 365, 230, 10, "Subtotal");
        AddRightAlignedText(commands, 560, 230, 10, model.Subtotal);
        AddText(commands, 365, 214, 10, "IVA 10%");
        AddRightAlignedText(commands, 560, 214, 10, model.Iva10);
        AddText(commands, 365, 198, 10, "IVA 5%");
        AddRightAlignedText(commands, 560, 198, 10, model.Iva5);
        DrawLine(commands, 365, 188, 190, 0, "#E5E7EB");
        AddText(commands, 365, 170, 14, "TOTAL", true, model.PrimaryColor);
        AddRightAlignedText(commands, 560, 170, 16, $"{model.Total} Gs.", true, "#7B61FF");

        DrawLine(commands, 20, 120, 555, 0, "#E5E7EB");
        AddWrappedText(commands, 28, 106, 10, model.FooterText, 280, 12);
        AddText(commands, 28, 84, 10, "Consulte este comprobante en la SET", true);
        AddQr(commands, qrMatrix, 458, 22, 4);
        AddText(commands, 434, 8, 9, "Consulte este comprobante en la SET");

        DrawLine(commands, 20, 18, 555, 0, "#C4B5FD", true);
        AddText(commands, 140, 4, 10, model.IsPreview ? PreviewNotice : "Representacion impresa del comprobante electronico.");

        return MinimalPdfDocumentBuilder.Build(commands.ToString(), 595, 842);
    }

    private static string BuildQrSvg(string payload)
    {
        var matrix = MinimalNumericQrCodeGenerator.GenerateVersion2L(payload);
        var size = matrix.GetLength(0);
        var cell = 4;
        var svg = new StringBuilder();
        svg.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"").Append(size * cell).Append("\" height=\"").Append(size * cell).Append("\" viewBox=\"0 0 ").Append(size * cell).Append(' ').Append(size * cell).Append("\">");
        svg.Append("<rect width=\"100%\" height=\"100%\" fill=\"#fff\"/>");
        for (var row = 0; row < size; row++)
        {
            for (var col = 0; col < size; col++)
            {
                if (!matrix[row, col])
                {
                    continue;
                }

                svg.Append("<rect x=\"").Append(col * cell).Append("\" y=\"").Append(row * cell).Append("\" width=\"").Append(cell).Append("\" height=\"").Append(cell).Append("\" fill=\"#111827\"/>");
            }
        }

        svg.Append("</svg>");
        return svg.ToString();
    }

    private static string MetaRow(string label, string value)
        => $"<div style=\"display:flex;gap:12px;margin-bottom:12px;font-size:14px;\"><div style=\"width:180px;font-weight:700;\">{Html(label)}:</div><div style=\"flex:1;\">{Html(value)}</div></div>";

    private static string CustomerRow(string label, string value)
        => $"<div style=\"display:flex;gap:18px;margin-bottom:10px;font-size:14px;\"><div style=\"width:220px;font-weight:700;\">{Html(label)}:</div><div style=\"flex:1;\">{Html(value)}</div></div>";

    private static string TotalRow(string label, string value, bool highlighted)
        => $"<div style=\"display:flex;justify-content:space-between;gap:12px;margin-bottom:12px;font-size:{(highlighted ? "18px" : "13px")};font-weight:{(highlighted ? "800" : "500")};color:{(highlighted ? "#7B61FF" : "#2F2F2F")};\"><span>{Html(label)}</span><span>{Html(value)}{(highlighted ? " Gs." : string.Empty)}</span></div>";

    private static string Html(string value)
        => value.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);

    private static string EscapePdf(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("(", "\\(", StringComparison.Ordinal).Replace(")", "\\)", StringComparison.Ordinal);

    private static void AddText(StringBuilder sb, decimal x, decimal y, decimal fontSize, string text, bool bold = false, string color = "#2F2F2F")
    {
        SetColor(sb, color);
        sb.Append("BT /").Append(bold ? "F2 " : "F1 ").Append(Format(fontSize)).Append(" Tf ").Append(Format(x)).Append(' ').Append(Format(y)).Append(" Td (")
            .Append(EscapePdf(text)).AppendLine(") Tj ET");
    }

    private static void AddWhiteText(StringBuilder sb, decimal x, decimal y, decimal fontSize, string text)
        => AddText(sb, x, y, fontSize, text, false, "#FFFFFF");

    private static void AddRightAlignedText(StringBuilder sb, decimal rightX, decimal y, decimal fontSize, string text, bool bold = false, string color = "#2F2F2F")
    {
        var width = ApproximateTextWidth(text, fontSize);
        AddText(sb, rightX - width, y, fontSize, text, bold, color);
    }

    private static void AddWrappedText(StringBuilder sb, decimal x, decimal y, decimal fontSize, string text, decimal maxWidth, decimal lineHeight)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var line = new StringBuilder();
        var currentY = y;
        foreach (var word in words)
        {
            var candidate = line.Length == 0 ? word : $"{line} {word}";
            if (ApproximateTextWidth(candidate, fontSize) <= maxWidth)
            {
                line.Clear();
                line.Append(candidate);
                continue;
            }

            AddText(sb, x, currentY, fontSize, line.ToString());
            currentY -= lineHeight;
            line.Clear();
            line.Append(word);
        }

        if (line.Length > 0)
        {
            AddText(sb, x, currentY, fontSize, line.ToString());
        }
    }

    private static void DrawFilledRect(StringBuilder sb, decimal x, decimal y, decimal width, decimal height, string color, decimal fillOpacity)
    {
        SetColor(sb, color, fillOpacity);
        sb.Append(Format(x)).Append(' ').Append(Format(y)).Append(' ').Append(Format(width)).Append(' ').Append(Format(height)).AppendLine(" re f");
    }

    private static void DrawLine(StringBuilder sb, decimal x, decimal y, decimal width, decimal height, string color, bool dashed = false)
    {
        SetColor(sb, color);
        if (dashed)
        {
            sb.AppendLine("[4 3] 0 d");
        }

        sb.Append("1 w ").Append(Format(x)).Append(' ').Append(Format(y)).Append(" m ")
            .Append(Format(x + width)).Append(' ').Append(Format(y + height)).AppendLine(" l S");

        if (dashed)
        {
            sb.AppendLine("[] 0 d");
        }
    }

    private static void AddQr(StringBuilder sb, bool[,] matrix, decimal startX, decimal startY, decimal moduleSize)
    {
        SetColor(sb, "#111827");
        var size = matrix.GetLength(0);
        for (var row = 0; row < size; row++)
        {
            for (var col = 0; col < size; col++)
            {
                if (!matrix[row, col])
                {
                    continue;
                }

                var x = startX + (col * moduleSize);
                var y = startY + ((size - 1 - row) * moduleSize);
                sb.Append(Format(x)).Append(' ').Append(Format(y)).Append(' ')
                    .Append(Format(moduleSize)).Append(' ').Append(Format(moduleSize)).AppendLine(" re f");
            }
        }
    }

    private static void SetColor(StringBuilder sb, string hex, decimal opacity = 1m)
    {
        var (r, g, b) = ParseColor(hex, opacity);
        sb.Append(Format(r)).Append(' ').Append(Format(g)).Append(' ').Append(Format(b)).AppendLine(" rg");
    }

    private static (decimal R, decimal G, decimal B) ParseColor(string hex, decimal opacity)
    {
        var value = hex.TrimStart('#');
        if (value.Length != 6)
        {
            return (0m, 0m, 0m);
        }

        var r = Convert.ToInt32(value[..2], 16) / 255m;
        var g = Convert.ToInt32(value.Substring(2, 2), 16) / 255m;
        var b = Convert.ToInt32(value.Substring(4, 2), 16) / 255m;
        return (r * opacity, g * opacity, b * opacity);
    }

    private static decimal ApproximateTextWidth(string text, decimal fontSize)
        => Math.Max(0, text.Length) * fontSize * 0.46m;

    private static decimal ParseDecimal(string? value)
        => decimal.TryParse(value, NumberStyles.Number, Invariant, out var parsed) ? parsed : 0m;

    private static string Format(decimal value)
        => value.ToString("0.###", Invariant);

    private static string FormatMoney(decimal value)
        => value.ToString("#,##0", new CultureInfo("es-PY"));

    private static string FormatQuantity(decimal value)
        => value % 1m == 0m ? value.ToString("0", Invariant) : value.ToString("0.##", Invariant);

    private static string TruncateCdc(string cdc)
        => cdc.Length <= 28 ? cdc : $"{cdc[..28]}...";

    private sealed record InvoiceSummary(
        string? IssuerName,
        string? IssuerAddress,
        string? StampingNumber,
        string? CustomerAddress,
        string? SaleCondition,
        IReadOnlyCollection<KudeItem> Items,
        string Subtotal,
        string TotalVat5,
        string TotalVat10,
        string TotalGeneral,
        string? TotalInWords,
        string? Observations,
        bool IsPreview);

    private sealed record ResolvedTemplateSettings(
        string TemplateCode,
        string? LogoUrl,
        string PrimaryColor,
        string SecondaryColor,
        string FooterText,
        bool ShowPhone,
        bool ShowEmail);

    private sealed record KudeDocumentModel(
        string TemplateCode,
        string? LogoUrl,
        string IssuerName,
        string IssuerRuc,
        string IssuerAddress,
        string? Phone,
        string? Email,
        string DocumentTitle,
        string Cdc,
        string CdcVisual,
        string StampingNumber,
        string EstablishmentCode,
        string ExpeditionPointCode,
        string DocumentNumber,
        string IssueDate,
        string CustomerName,
        string CustomerDocument,
        string CustomerAddress,
        string SaleCondition,
        IReadOnlyCollection<KudeItem> Items,
        string Subtotal,
        string Iva10,
        string Iva5,
        string Total,
        string TotalInWords,
        string Observations,
        string PrimaryColor,
        string SecondaryColor,
        string FooterText,
        string QrPayload,
        bool IsPreview);

    private sealed record KudeItem(
        string Description,
        string Quantity,
        string UnitPrice,
        string Vat,
        string Subtotal);
}
