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

    private const string Cdc = "01800123456001001000012311123456789202604251";

    // Estructura rRetEnviDe/rProtDe del Manual Tecnico v150 (PP01-PP051); valores de prueba, no respuestas reales de SIFEN.
    private static string Response(string estado, string? protocol = "123456789012345", string code = "0260", string message = "Autorizacion del DE satisfactoria", string? digest = "AAAA") => $"""
        <env:Envelope xmlns:env="http://www.w3.org/2003/05/soap-envelope">
          <env:Header/>
          <env:Body>
            <ns2:rRetEnviDe xmlns:ns2="http://ekuatia.set.gov.py/sifen/xsd">
              <ns2:rProtDe>
                <ns2:Id>{Cdc}</ns2:Id>
                <ns2:dFecProc>2026-10-08T14:51:21-03:00</ns2:dFecProc>
                {(digest is null ? "" : $"<ns2:dDigVal>{digest}</ns2:dDigVal>")}
                <ns2:dEstRes>{estado}</ns2:dEstRes>
                {(protocol is null ? "" : $"<ns2:dProtAut>{protocol}</ns2:dProtAut>")}
                <ns2:gResProc>
                  <ns2:dCodRes>{code}</ns2:dCodRes>
                  <ns2:dMsgRes>{message}</ns2:dMsgRes>
                </ns2:gResProc>
              </ns2:rProtDe>
            </ns2:rRetEnviDe>
          </env:Body>
        </env:Envelope>
        """;

    [Fact]
    public void ParseResponse_ShouldMapApprovedWithObservations()
    {
        var result = new DefaultSifenResponseParser().ParseResponse(Response("Aprobado con observación", code: "1005", message: "Extemporaneo"));

        Assert.Equal(SifenResponseOutcome.Observed, result.Outcome);
        Assert.Equal(SifenDocumentStatus.Accepted, result.StatusHint);
        Assert.True(result.IsSuccessful);
        Assert.Equal("1005", result.StatusCode);
    }

    [Theory]
    [InlineData("Aprobado")]
    [InlineData("APROBADO")]
    [InlineData("  aprobado ")]
    public void ParseResponse_ShouldApprove_OnlyFromDEstRes(string estado)
    {
        var result = new DefaultSifenResponseParser().ParseResponse(Response(estado));

        Assert.Equal(SifenResponseOutcome.Approved, result.Outcome);
        Assert.Equal(SifenDocumentStatus.Accepted, result.StatusHint);
        Assert.Equal("0260", result.StatusCode);
        Assert.Equal("AAAA", result.DigestValue);
        Assert.Equal(Cdc, result.Cdc);
    }

    [Theory]
    [InlineData("Observado")]
    [InlineData("En proceso")]
    [InlineData("")]
    public void ParseResponse_ShouldNotApprove_WhenDEstResIsNotDocumented_EvenWithProtocolAndSuccessCode(string estado)
    {
        var result = new DefaultSifenResponseParser().ParseResponse(Response(estado, protocol: "123456789012345", code: "0260"));

        Assert.Equal(SifenResponseOutcome.Unknown, result.Outcome);
        Assert.Equal(SifenDocumentStatus.Submitted, result.StatusHint);
        Assert.False(result.IsSuccessful);
        Assert.False(result.IsFinal);
    }

    [Fact]
    public void ParseResponse_ShouldNotApprove_BecauseOfCode0300_ThatBelongsToTheBatchService()
    {
        var result = new DefaultSifenResponseParser().ParseResponse(Response("Rechazado", protocol: null, code: "0300", message: "Lote recibido con exito"));

        Assert.Equal(SifenResponseOutcome.Rejected, result.Outcome);
        Assert.False(result.IsSuccessful);
    }

    [Fact]
    public void ParseResponse_ShouldReject_EvenWhenProtocolIsPresent()
    {
        var result = new DefaultSifenResponseParser().ParseResponse(Response("Rechazado", protocol: "123456789012345", code: "1002", message: "Documento duplicado"));

        Assert.Equal(SifenResponseOutcome.Rejected, result.Outcome);
        Assert.Equal(SifenDocumentStatus.Rejected, result.StatusHint);
        Assert.Equal("1002", result.StatusCode);
    }

    [Fact]
    public void ParseResponse_ShouldMapSoap12Fault_AsTechnicalErrorNotAsFiscalResult()
    {
        var result = new DefaultSifenResponseParser().ParseResponse("""
            <env:Envelope xmlns:env="http://www.w3.org/2003/05/soap-envelope">
              <env:Body>
                <env:Fault>
                  <env:Code><env:Value>env:Sender</env:Value></env:Code>
                  <env:Reason><env:Text xml:lang="es">Mensaje invalido</env:Text></env:Reason>
                </env:Fault>
              </env:Body>
            </env:Envelope>
            """);

        Assert.Equal(SifenResponseOutcome.TechnicalError, result.Outcome);
        Assert.Equal("SOAP_FAULT", result.StatusCode);
        Assert.False(result.IsSuccessful);
        Assert.Contains("Mensaje invalido", result.TechnicalMessage, StringComparison.Ordinal);
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
