namespace SifenInvoicing.Application.Invoices;

public sealed record FeTenantInvoicePage(
    IReadOnlyCollection<FeTenantInvoiceListItem> Items,
    int TotalCount,
    int Page,
    int PageSize,
    int TotalPages);
