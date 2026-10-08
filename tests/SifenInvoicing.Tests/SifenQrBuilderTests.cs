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

    private const string Cdc = "01444444017001001001452822017012515873260988";
    private const string Digest = "yzGYhUx1/XYYzksWB+fPR3Qc50c=";
    private const string Csc = "ABCD0000000000000000000000000000"; // CSC generico de Test (Guia de Pruebas p.4)

    private static SifenQrInput Base(
        string? cdc = null, string? date = null, decimal total = 300000m, decimal iva = 27272m, int items = 2,
        string? digest = null, string idCsc = "0001", string csc = Csc, string receiver = "88899990",
        string receiverName = "dRucRec", SifenQrEnvironment env = SifenQrEnvironment.Test) =>
        new(env, cdc ?? Cdc, date ?? "2017-01-25T09:35:17", receiver, total, iva, items, digest ?? Digest, idCsc, csc,
            ReceiverParameterName: receiverName);

    private static string Hash(SifenQrInput input) => new SifenQrBuilder().Build(input).Hash;

    [Fact]
    public void Hash_IsSha256HexOfParametersPlusCsc_AndChangesWithEachInput()
    {
        var baseline = new SifenQrBuilder().Build(Base());
        var data = baseline.Url[SifenQrBuilder.TestBaseUrl.Length..baseline.Url.IndexOf("&cHashQR=", StringComparison.Ordinal)];
        var expected = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(data + Csc))).ToLowerInvariant();

        Assert.Equal(expected, baseline.Hash);
        Assert.Matches("^[0-9a-f]{64}$", baseline.Hash);
        Assert.NotEqual(baseline.Hash, Hash(Base(total: 300001m)));
        Assert.NotEqual(baseline.Hash, Hash(Base(iva: 1m)));
        Assert.NotEqual(baseline.Hash, Hash(Base(digest: "AAAAAAAAAAAAAAAAAAAAAAAAAAA=")));
        Assert.NotEqual(baseline.Hash, Hash(Base(cdc: "01444444017001001001452822017012515873260989")));
        Assert.NotEqual(baseline.Hash, Hash(Base(csc: "EFGH0000000000000000000000000000")));
        Assert.NotEqual(baseline.Hash, Hash(Base(idCsc: "0002")));
        Assert.NotEqual(baseline.Hash, Hash(Base(items: 3)));
        Assert.NotEqual(baseline.Hash, Hash(Base(receiver: "88899991")));
    }

    [Fact]
    public void Build_IsDeterministic_ForTheSameInputs()
    {
        Assert.Equal(new SifenQrBuilder().Build(Base()), new SifenQrBuilder().Build(Base()));
    }

    [Fact]
    public void Serialization_DateAndDigestAreHexOfTheirText_ItemsAreCount_ReceiverNameFollowsFieldKind()
    {
        var url = new SifenQrBuilder().Build(Base(receiverName: "dNumIDRec", receiver: "1234567")).Url;

        Assert.Contains("&dFeEmiDE=323031372d30312d32355430393a33353a3137&", url);
        Assert.Contains("&DigestValue=797a4759685578312f5859597a6b7357422b6650523351633530633d&", url);
        Assert.Contains("&dNumIDRec=1234567&", url);
        Assert.DoesNotContain("dRucRec", url);
        Assert.Contains("&cItems=2&", url);
        Assert.StartsWith("https://ekuatia.set.gov.py/consultas-test/qr?nVersion=150&Id=" + Cdc, url);
        Assert.DoesNotContain(Csc, url);
    }

    [Fact]
    public void ProductionUrl_IsOnlyUsedWhenTheEnvironmentIsProduction()
    {
        Assert.StartsWith(SifenQrBuilder.ProductionBaseUrl, new SifenQrBuilder().Build(Base(env: SifenQrEnvironment.Production)).Url);
        Assert.StartsWith(SifenQrBuilder.TestBaseUrl, new SifenQrBuilder().Build(Base()).Url);
    }

    [Fact]
    public void ReceiverParameterName_MustBeDRucRecOrDNumIDRec()
    {
        Assert.ThrowsAny<Exception>(() => new SifenQrBuilder().Build(Base(receiverName: "dOtro")));
    }

    [Fact]
    public void Url_WithTypicalDocument_FitsTheXsdLimitOf100To600Characters()
    {
        var length = new SifenQrBuilder().Build(Base()).Url.Length;
        Assert.InRange(length, 100, 600);
    }
}
