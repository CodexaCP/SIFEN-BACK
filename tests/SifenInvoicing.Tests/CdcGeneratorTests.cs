using SifenInvoicing.Application.Cdc;
using SifenInvoicing.Domain.Common;

namespace SifenInvoicing.Tests;

public sealed class CdcGeneratorTests
{
    [Fact]
    public void GenerateCDC_ShouldReturnExpected44DigitValue()
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
            "20260425");

        var cdc = CdcGenerator.GenerateCDC(input);

        Assert.Equal("01800123456001001000012311123456789202604251", cdc);
    }

    [Fact]
    public void GenerateCDC_ShouldPadVariableLengthFields()
    {
        var input = new GenerateCdcInput(
            "01",
            "80012345",
            "6",
            "12",
            "3",
            "45",
            "1",
            "2",
            "987",
            "20260425");

        var cdc = CdcGenerator.GenerateCDC(input);

        Assert.StartsWith("0180012345601200300000451200000098720260425", cdc[..43]);
        Assert.Equal(44, cdc.Length);
        Assert.True(CdcGenerator.ValidateCDC(cdc));
    }

    [Fact]
    public void ValidateCDC_ShouldReturnFalse_WhenVerificationDigitIsInvalid()
    {
        const string invalidCdc = "01800123456001001000012311123456789202604250";

        Assert.False(CdcGenerator.ValidateCDC(invalidCdc));
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
