using SifenInvoicing.Api.Endpoints;
using SifenInvoicing.Application.XmlDe;

namespace SifenInvoicing.Tests;

public sealed class FeInvoiceCatalogsEndpointTests
{
    [Fact]
    public void GetInvoiceCatalogs_ShouldExposeTheSameTablesTheBackValidates()
    {
        var catalogs = InvoiceEndpoints.GetInvoiceCatalogs();

        Assert.Equal(new[] { 1, 2 }, catalogs.TransactionTypes.Select(item => item.Code));
        Assert.Equal(new[] { 1, 2 }, catalogs.PresenceIndicators.Select(item => item.Code));
        Assert.Equal(new[] { 1, 2 }, catalogs.ReceiverTaxpayerKinds.Select(item => item.Code));
        Assert.Equal(SifenDeUnitsOfMeasure.All.Count, catalogs.UnitsOfMeasure.Count);
        Assert.Contains(catalogs.UnitsOfMeasure, item => item.Code == 77 && item.Description == "UNI");
        Assert.All(catalogs.UnitsOfMeasure, item => Assert.True(SifenDeUnitsOfMeasure.TryGetRepresentation(item.Code, out _)));
        Assert.Equal(new[] { "Ruc", "Ci" }, catalogs.ReceiverDocumentTypes);
        Assert.Equal(new[] { "Vat10", "Vat5", "Exempt" }, catalogs.VatTypes);
    }
}
