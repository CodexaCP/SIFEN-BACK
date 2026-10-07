using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SifenInvoicing.Application.Invoices;

/// <summary>
/// Datos COMERCIALES que puede enviar el cliente. Todo dato fiscal (ambiente, timbrado, establecimiento,
/// punto de expedicion, numero, codigo de seguridad, CDC, tipo de contribuyente, tipo de emision, fecha de
/// emision, datos del emisor) lo decide el backend a partir de la configuracion fiscal del tenant.
/// </summary>
public sealed record CreateInvoiceCommand(
    string IdempotencyKey,
    string? Notes,
    string ReceptorNombre,
    InvoiceReceiverDocumentType ReceptorTipoDocumento,
    string ReceptorDocumento,
    string? ReceptorDireccion,
    string? ReceptorEmail,
    string? ReceptorPhone,
    InvoiceCurrency Currency,
    InvoiceSaleCondition SaleCondition,
    IReadOnlyCollection<CreateInvoiceItemCommand> Items,
    int? TransactionType = null,
    int? PresenceIndicator = null,
    int? ReceptorTaxpayerKind = null)
{
    /// <summary>SHA-256 (hex) del contenido comercial normalizado, sin la Idempotency-Key.</summary>
    public string ComputeRequestHash()
    {
        var canonical = JsonSerializer.Serialize(new
        {
            Notes = Normalize(Notes),
            ReceptorNombre = Normalize(ReceptorNombre),
            ReceptorTipoDocumento,
            ReceptorDocumento = Normalize(ReceptorDocumento),
            ReceptorDireccion = Normalize(ReceptorDireccion),
            ReceptorEmail = Normalize(ReceptorEmail),
            ReceptorPhone = Normalize(ReceptorPhone),
            Currency,
            SaleCondition,
            TransactionType,
            PresenceIndicator,
            ReceptorTaxpayerKind,
            Items = Items?.Select(item => new
            {
                Description = Normalize(item.Description),
                item.Quantity,
                item.UnitPrice,
                item.VatRate,
                Code = Normalize(item.Code),
                item.UnitCode
            })
        });

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
