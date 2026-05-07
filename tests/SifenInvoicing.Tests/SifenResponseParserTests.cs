using SifenInvoicing.Infrastructure.Sifen;
using SifenInvoicing.Domain.Documents;
using SifenInvoicing.Application.Sifen;

namespace SifenInvoicing.Tests;

public sealed class SifenResponseParserTests
{
    [Fact]
    public void ParseResponse_ShouldExtractApprovalData()
    {
        var parser = new DefaultSifenResponseParser();

        var result = parser.ParseResponse("""
            <soapenv:Envelope xmlns:soapenv="http://schemas.xmlsoap.org/soap/envelope/" xmlns:sif="http://ekuatia.set.gov.py/sifen/xsd">
              <soapenv:Body>
                <sif:rRetEnviDe>
                  <sif:rProtDe>
                    <sif:Id>01234567890123456789012345678901234567890123</sif:Id>
                    <sif:dFecProc>2026-04-25T12:00:00</sif:dFecProc>
                    <sif:dEstRes>Aprobado</sif:dEstRes>
                    <sif:dProtAut>123456789012345</sif:dProtAut>
                    <sif:gResProc>
                      <sif:dCodRes>0300</sif:dCodRes>
                      <sif:dMsgRes>Aprobado</sif:dMsgRes>
                    </sif:gResProc>
                  </sif:rProtDe>
                </sif:rRetEnviDe>
              </soapenv:Body>
            </soapenv:Envelope>
            """);

        Assert.True(result.IsSuccessful);
        Assert.True(result.IsFinal);
        Assert.Equal(SifenDocumentStatus.Accepted, result.StatusHint);
        Assert.Equal("01234567890123456789012345678901234567890123", result.Cdc);
        Assert.Equal("123456789012345", result.TrackingId);
        Assert.Equal("0300", result.StatusCode);
    }

    [Fact]
    public void ParseResponse_ShouldMapRejectedXml()
    {
        var parser = new DefaultSifenResponseParser();

        var result = parser.ParseResponse("""
            <soapenv:Envelope xmlns:soapenv="http://schemas.xmlsoap.org/soap/envelope/" xmlns:sif="http://ekuatia.set.gov.py/sifen/xsd">
              <soapenv:Body>
                <sif:rRetEnviDe>
                  <sif:rProtDe>
                    <sif:Id>01800123456001001000012311123456789202604251</sif:Id>
                    <sif:dEstRes>Rechazado</sif:dEstRes>
                    <sif:gResProc>
                      <sif:dCodRes>0500</sif:dCodRes>
                      <sif:dMsgRes>Documento rechazado por inconsistencia</sif:dMsgRes>
                    </sif:gResProc>
                  </sif:rProtDe>
                </sif:rRetEnviDe>
              </soapenv:Body>
            </soapenv:Envelope>
            """);

        Assert.Equal(SifenResponseOutcome.Rejected, result.Outcome);
        Assert.Equal(SifenDocumentStatus.Rejected, result.StatusHint);
        Assert.Equal("0500", result.StatusCode);
    }

    [Fact]
    public void ParseResponse_ShouldMapObservedXml()
    {
        var parser = new DefaultSifenResponseParser();

        var result = parser.ParseResponse("""
            <soapenv:Envelope xmlns:soapenv="http://schemas.xmlsoap.org/soap/envelope/" xmlns:sif="http://ekuatia.set.gov.py/sifen/xsd">
              <soapenv:Body>
                <sif:rRetEnviDe>
                  <sif:rProtDe>
                    <sif:Id>01800123456001001000012311123456789202604251</sif:Id>
                    <sif:dEstRes>Observado</sif:dEstRes>
                    <sif:gResProc>
                      <sif:dCodRes>0600</sif:dCodRes>
                      <sif:dMsgRes>Documento observado</sif:dMsgRes>
                    </sif:gResProc>
                  </sif:rProtDe>
                </sif:rRetEnviDe>
              </soapenv:Body>
            </soapenv:Envelope>
            """);

        Assert.Equal(SifenResponseOutcome.Observed, result.Outcome);
        Assert.Equal(SifenDocumentStatus.Submitted, result.StatusHint);
        Assert.Equal("0600", result.StatusCode);
    }

    [Fact]
    public void ParseResponse_ShouldMapTransportTimeout()
    {
        var parser = new DefaultSifenResponseParser();

        var result = parser.ParseResponse("SOAP_TIMEOUT|Request timed out.");

        Assert.True(result.IsFinal);
        Assert.False(result.IsSuccessful);
        Assert.Equal(SifenResponseOutcome.TechnicalError, result.Outcome);
        Assert.Equal(SifenDocumentStatus.Failed, result.StatusHint);
        Assert.Equal("SOAP_TIMEOUT", result.StatusCode);
    }

    [Fact]
    public void ParseResponse_ShouldMapInvalidXml()
    {
        var parser = new DefaultSifenResponseParser();

        var result = parser.ParseResponse("<broken");

        Assert.Equal(SifenResponseOutcome.InvalidXml, result.Outcome);
        Assert.Equal(SifenDocumentStatus.Failed, result.StatusHint);
        Assert.Equal("UNPARSEABLE_RESPONSE", result.StatusCode);
    }

    [Fact]
    public void ParseResponse_ShouldMapEmptyResponse()
    {
        var parser = new DefaultSifenResponseParser();

        var result = parser.ParseResponse("");

        Assert.Equal(SifenResponseOutcome.EmptyResponse, result.Outcome);
        Assert.Equal(SifenDocumentStatus.Failed, result.StatusHint);
        Assert.Equal("NO_RESPONSE", result.StatusCode);
    }

    [Fact]
    public void ParseResponse_ShouldMapUnknownCodeAndMessage()
    {
        var parser = new DefaultSifenResponseParser();

        var result = parser.ParseResponse("""
            <soapenv:Envelope xmlns:soapenv="http://schemas.xmlsoap.org/soap/envelope/" xmlns:sif="http://ekuatia.set.gov.py/sifen/xsd">
              <soapenv:Body>
                <sif:rRetEnviDe>
                  <sif:rProtDe>
                    <sif:Id>01800123456001001000012311123456789202604251</sif:Id>
                    <sif:dEstRes>Procesado</sif:dEstRes>
                    <sif:gResProc>
                      <sif:dCodRes>9999</sif:dCodRes>
                      <sif:dMsgRes>Estado no documentado</sif:dMsgRes>
                    </sif:gResProc>
                  </sif:rProtDe>
                </sif:rRetEnviDe>
              </soapenv:Body>
            </soapenv:Envelope>
            """);

        Assert.Equal(SifenResponseOutcome.Unknown, result.Outcome);
        Assert.Equal(SifenDocumentStatus.Submitted, result.StatusHint);
        Assert.Equal("9999", result.StatusCode);
    }
}
