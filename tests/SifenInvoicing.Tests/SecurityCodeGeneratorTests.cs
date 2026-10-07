using SifenInvoicing.Application.Security;
using SifenInvoicing.Domain.Common;

namespace SifenInvoicing.Tests;

public sealed class SecurityCodeGeneratorTests
{
    [Fact]
    public void Generate_ShouldReturnNineDigitNumericCode()
    {
        for (var i = 0; i < 200; i++)
        {
            var code = SecurityCodeGenerator.Generate("0000123");
            Assert.Equal(9, code.Length);
            Assert.All(code, c => Assert.True(char.IsAsciiDigit(c)));
            Assert.NotEqual("000000000", code);
        }
    }

    [Fact]
    public void Generate_ShouldNotRepeatAcrossManyCalls()
    {
        var codes = Enumerable.Range(0, 500).Select(_ => SecurityCodeGenerator.Generate("1")).ToHashSet();
        Assert.True(codes.Count > 495);
    }

    [Fact]
    public void Generate_ShouldRetryWhenEqualToDocumentNumber()
    {
        var values = new Queue<int>([123, 0, 456]);
        Assert.Equal("000000456", SecurityCodeGenerator.Generate("0000123", values.Dequeue));
    }

    [Fact]
    public void Generate_ShouldThrow_WhenSourceNeverProducesValidCode()
    {
        Assert.Throws<DomainException>(() => SecurityCodeGenerator.Generate("123", () => 123));
    }

    [Fact]
    public void Generate_ShouldRejectNonNumericDocumentNumber()
    {
        Assert.Throws<DomainException>(() => SecurityCodeGenerator.Generate("12a"));
    }

    [Fact]
    public void Generate_ShouldBeSafeUnderParallelCalls()
    {
        var codes = new System.Collections.Concurrent.ConcurrentBag<string>();
        Parallel.For(0, 2000, _ => codes.Add(SecurityCodeGenerator.Generate("0000001")));

        Assert.Equal(2000, codes.Count);
        Assert.All(codes, c => Assert.Equal(9, c.Length));
        Assert.True(codes.Distinct().Count() > 1990);
    }
}
