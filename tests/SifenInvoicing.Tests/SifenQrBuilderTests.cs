using SifenInvoicing.Application.Qr;

namespace SifenInvoicing.Tests;

public sealed class SifenQrBuilderTests
{
    // Vector oficial: Manual Tecnico v150 sec. 13.8.4 (hash y URL). Base URL segun NT-10 (sin 'www').
    [Fact]
    public void Build_ShouldReproduceManualV150Example()
    {
        var result = new SifenQrBuilder().Build(new SifenQrInput(
            SifenQrEnvironment.Production,
            "01444444017001001001452822017012515873260988",
            "2017-01-25T09:35:17",
            "88899990",
            300000m,
            27272m,
            2,
            "yzGYhUx1/XYYzksWB+fPR3Qc50c=",
            "0001",
            "ABCD0000000000000000000000000000"));

        Assert.Equal("97ddbb3c1e7d65af03a70ffe21f2b34846ab1c89e0566c35222086766b7374ed", result.Hash);
        Assert.Equal(
            "https://ekuatia.set.gov.py/consultas/qr?nVersion=150&Id=01444444017001001001452822017012515873260988"
            + "&dFeEmiDE=323031372d30312d32355430393a33353a3137&dRucRec=88899990&dTotGralOpe=300000&dTotIVA=27272"
            + "&cItems=2&DigestValue=797a4759685578312f5859597a6b7357422b6650523351633530633d&IdCSC=0001"
            + "&cHashQR=97ddbb3c1e7d65af03a70ffe21f2b34846ab1c89e0566c35222086766b7374ed",
            result.Url);
        Assert.DoesNotContain("ABCD0000", result.Url);
    }

    [Fact]
    public void Build_ShouldUseTestUrl_AndZeroWhenReceiverMissing()
    {
        var result = new SifenQrBuilder().Build(new SifenQrInput(
            SifenQrEnvironment.Test, "01444444017001001001452822017012515873260988", "2017-01-25T09:35:17",
            "", 100m, 0m, 1, "abc=", "0001", "SECRET"));

        Assert.StartsWith(SifenQrBuilder.TestBaseUrl, result.Url);
        Assert.Contains("&dRucRec=0&", result.Url);
        Assert.Contains("&dTotIVA=0&", result.Url);
    }
}
