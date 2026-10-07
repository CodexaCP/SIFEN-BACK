using SifenInvoicing.Application.Cdc;
using SifenInvoicing.Domain.Common;

namespace SifenInvoicing.Tests;

public sealed class CdcGeneratorTests
{
    // Vector oficial: Manual Tecnico v150 sec. 10.1 (p.56).
    private const string ManualCdc = "01444444017001001001452822017012515873260988";

    [Fact]
    public void GenerateCDC_ShouldMatchManualV150Example()
    {
        var input = new GenerateCdcInput(
            "01", "44444401", "7", "001", "001", "0014528", "2", "1", "587326098", "20170125");

        Assert.Equal(ManualCdc, CdcGenerator.GenerateCDC(input));
        Assert.True(CdcGenerator.ValidateCDC(ManualCdc));
    }

    [Fact]
    public void ValidateCDC_ShouldRejectManualExampleWithWrongDigit()
    {
        Assert.False(CdcGenerator.ValidateCDC(ManualCdc[..43] + "4"));
    }

    [Fact]
    public void GenerateCDC_ShouldPadVariableLengthFields()
    {
        var input = new GenerateCdcInput(
            "01", "80012345", "6", "12", "3", "45", "1", "2", "987", "20260425");

        var cdc = CdcGenerator.GenerateCDC(input);

        Assert.Equal(44, cdc.Length);
        Assert.StartsWith("0180012345601200300000451" + "20260425" + "2" + "000000987", cdc[..43]);
        Assert.True(CdcGenerator.ValidateCDC(cdc));
    }

    [Fact]
    public void ValidateCDC_ShouldReturnFalse_WhenVerificationDigitIsInvalid()
    {
        var cdc = CdcGenerator.GenerateCDC(new GenerateCdcInput(
            "01", "80012345", "6", "1", "1", "123", "1", "1", "123456789", "20260425"));
        var wrong = (cdc[43] - '0' + 1) % 10;

        Assert.False(CdcGenerator.ValidateCDC(cdc[..43] + wrong));
    }

    [Fact]
    public void GenerateCDC_ShouldThrow_WhenExactLengthFieldIsInvalid()
    {
        var input = new GenerateCdcInput(
            "1",
            "80012345",
            "6",
            "1",
            "1",
            "123",
            "1",
            "1",
            "123456789",
            "20260425");

        Assert.Throws<DomainException>(() => CdcGenerator.GenerateCDC(input));
    }

    [Fact]
    public void GenerateCDC_ShouldThrow_WhenFieldContainsNonNumericCharacters()
    {
        var input = new GenerateCdcInput(
            "01",
            "80012A45",
            "6",
            "1",
            "1",
            "123",
            "1",
            "1",
            "123456789",
            "20260425");

        Assert.Throws<DomainException>(() => CdcGenerator.GenerateCDC(input));
    }

    [Fact]
    public void GenerateCDC_ShouldThrow_WhenDateIsInvalid()
    {
        var input = new GenerateCdcInput(
            "01",
            "80012345",
            "6",
            "1",
            "1",
            "123",
            "1",
            "1",
            "123456789",
            "20260230");

        Assert.Throws<DomainException>(() => CdcGenerator.GenerateCDC(input));
    }
}
