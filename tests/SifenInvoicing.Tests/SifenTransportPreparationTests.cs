using System.Xml.Linq;
using SifenInvoicing.Application.Sifen;
using SifenInvoicing.Domain.Documents;
using SifenInvoicing.Infrastructure.Sifen;

namespace SifenInvoicing.Tests;

/// <summary>Serializacion SOAP 1.2, estados y consulta por CDC. Ninguna prueba abre una conexion de red.</summary>
public sealed class SifenTransportPreparationTests
{
    private const string Cdc = "01800123456001001000012311123456789202604251";
    private static readonly XNamespace Soap = "http://www.w3.org/2003/05/soap-envelope";
    private static readonly XNamespace Sifen = "http://ekuatia.set.gov.py/sifen/xsd";

    private const string SignedDe =
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<rDE xmlns=\"http://ekuatia.set.gov.py/sifen/xsd\" xmlns:ds=\"http://www.w3.org/2000/09/xmldsig#\">\n" +
        "  <dVerFor>150</dVerFor>\n  <DE Id=\"" + Cdc + "\"><gDatGralOpe /></DE>\n  <ds:Signature><ds:SignedInfo /></ds:Signature>\n</rDE>";

    [Fact]
    public void BuildReception_ShouldProduceSoap12Envelope_WithREnviDe()
    {
        var envelope = XDocument.Parse(SifenSoapEnvelopeBuilder.BuildReception("123", SignedDe));

        Assert.Equal(Soap + "Envelope", envelope.Root!.Name);
        Assert.Empty(envelope.Root.Element(Soap + "Header")!.Elements());
        var request = envelope.Root.Element(Soap + "Body")!.Element(Sifen + "rEnviDe")!;
        Assert.Equal("123", request.Element(Sifen + "dId")!.Value);
        Assert.Equal(Sifen + "rDE", request.Element(Sifen + "xDE")!.Elements().Single().Name);
        Assert.DoesNotContain("schemas.xmlsoap.org", envelope.ToString(), StringComparison.Ordinal); // no SOAP 1.1
    }

    [Fact]
    public void BuildReception_ShouldEmbedSignedDocumentVerbatim_WithoutXmlDeclaration()
    {
        var envelope = SifenSoapEnvelopeBuilder.BuildReception("123", SignedDe);

        var expected = SignedDe[SignedDe.IndexOf("<rDE", StringComparison.Ordinal)..];
        Assert.Contains("<xsd:xDE>" + expected + "</xsd:xDE>", envelope, StringComparison.Ordinal); // mismos bytes, firma intacta
        Assert.DoesNotContain("<?xml", envelope, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1234567890123456")]
    [InlineData("12a")]
    public void BuildReception_ShouldRejectInvalidDId(string dId)
    {
        Assert.Throws<ArgumentException>(() => SifenSoapEnvelopeBuilder.BuildReception(dId, SignedDe));
    }

    [Fact]
    public void BuildReception_ShouldRejectNonRDeRoot()
    {
        Assert.Throws<ArgumentException>(() => SifenSoapEnvelopeBuilder.BuildReception("1", "<other/>"));
    }

    [Fact]
    public void BuildCdcQuery_ShouldUseREnviConsDeRequest()
    {
        var envelope = XDocument.Parse(SifenSoapEnvelopeBuilder.BuildCdcQuery("12", Cdc));

        var request = envelope.Root!.Element(Soap + "Body")!.Element(Sifen + "rEnviConsDeRequest")!;
        Assert.Equal("12", request.Element(Sifen + "dId")!.Value);
        Assert.Equal(Cdc, request.Element(Sifen + "dCDC")!.Value);
        Assert.Throws<ArgumentException>(() => SifenSoapEnvelopeBuilder.BuildCdcQuery("12", "123"));
    }

    [Fact]
    public void ContentType_ShouldBeSoap12_AndOnlyCarryActionWhenConfirmed()
    {
        Assert.Equal("application/soap+xml; charset=utf-8", SifenSoapEnvelopeBuilder.BuildContentType(null));
        Assert.Equal("application/soap+xml; charset=utf-8", SifenSoapEnvelopeBuilder.BuildContentType("  "));
        Assert.Equal("application/soap+xml; charset=utf-8; action=\"urn:x\"", SifenSoapEnvelopeBuilder.BuildContentType("urn:x"));
    }

    [Fact]
    public async Task BuildHttpRequest_ShouldPostSoap12_WithoutSoapActionHeader()
    {
        var request = new SifenSoapRequest(new Uri("https://example.invalid/ws"), "<e/>", null, TimeSpan.FromSeconds(5), null);

        using var message = DefaultSifenSoapTransport.BuildHttpRequest(request);

        Assert.Equal(HttpMethod.Post, message.Method);
        Assert.Equal("application/soap+xml", message.Content!.Headers.ContentType!.MediaType);
        Assert.Equal("utf-8", message.Content.Headers.ContentType.CharSet);
        Assert.False(message.Headers.Contains("SOAPAction"));
        Assert.Equal("<e/>", await message.Content.ReadAsStringAsync());
    }

    // ---- Estados -----------------------------------------------------------------------------------------------

    private static SifenSubmissionResult Submission(string? transportCode, int? http = null, bool diagnostic = false) =>
        new(false, "https://x", null, null, null, transportCode, null, http, diagnostic);

    private static ParsedSifenResponse Parsed(SifenResponseOutcome outcome, string? code = null) =>
        new(outcome, SifenDocumentStatus.Failed, false, true, null, null, code, null, null);

    [Fact]
    public void Map_Diagnostic_ShouldBeNotSent()
    {
        Assert.Equal((SifenTransmissionState.NotSent, SifenFiscalState.None),
            SifenStateMapper.Map(Submission("DIAGNOSTIC_MODE_ENABLED", diagnostic: true), Parsed(SifenResponseOutcome.TechnicalError)));
    }

    [Theory]
    [InlineData(SifenResponseOutcome.Approved, SifenFiscalState.Approved)]
    [InlineData(SifenResponseOutcome.Observed, SifenFiscalState.ApprovedWithObservations)]
    [InlineData(SifenResponseOutcome.Rejected, SifenFiscalState.Rejected)]
    public void Map_FiscalOutcomes_ShouldBeDelivered(SifenResponseOutcome outcome, SifenFiscalState fiscal)
    {
        Assert.Equal((SifenTransmissionState.Delivered, fiscal), SifenStateMapper.Map(Submission("HTTP_OK", 200), Parsed(outcome)));
    }

    [Theory]
    [InlineData("SOAP_TIMEOUT", null, SifenTransmissionState.Indeterminate)]
    [InlineData("SOAP_TRANSPORT_ERROR", null, SifenTransmissionState.Indeterminate)]
    [InlineData("HTTP_ERROR", 503, SifenTransmissionState.Indeterminate)]
    [InlineData("HTTP_ERROR", 500, SifenTransmissionState.Indeterminate)]
    [InlineData("HTTP_ERROR", 403, SifenTransmissionState.NotDelivered)]
    [InlineData("HTTP_ERROR", 302, SifenTransmissionState.NotDelivered)]
    [InlineData("HTTP_OK", 200, SifenTransmissionState.Indeterminate)]
    public void Map_TransportFailures_ShouldNeverClaimDeliveryWithoutEvidence(string code, int? http, SifenTransmissionState expected)
    {
        var (transmission, fiscal) = SifenStateMapper.Map(Submission(code, http), Parsed(SifenResponseOutcome.TechnicalError, code));

        Assert.Equal(expected, transmission);
        Assert.Equal(SifenFiscalState.None, fiscal);
    }

    // ---- Consulta por CDC --------------------------------------------------------------------------------------

    private static string QueryResponse(string code, string root = "rEnviConsDeResponse") => $"""
        <env:Envelope xmlns:env="http://www.w3.org/2003/05/soap-envelope"><env:Body>
          <ns2:{root} xmlns:ns2="http://ekuatia.set.gov.py/sifen/xsd">
            <ns2:dFecProc>2026-10-08T14:51:21-03:00</ns2:dFecProc>
            <ns2:dCodRes>{code}</ns2:dCodRes><ns2:dMsgRes>msg</ns2:dMsgRes>
            {(code == "0422" ? "<ns2:xContenDE><ns2:rContDe><ns2:rDE/><ns2:dProtAut>123456789012345</ns2:dProtAut></ns2:rContDe></ns2:xContenDE>" : "")}
          </ns2:{root}>
        </env:Body></env:Envelope>
        """;

    [Theory]
    [InlineData("rEnviConsDeResponse")]
    [InlineData("rResEnviConsDe")] // la raiz difiere entre Manual y Guia de Mejores Practicas: se busca por campos
    public void QueryParser_0422_ShouldBeFound(string root)
    {
        var result = SifenCdcQueryResponseParser.Parse(QueryResponse("0422", root));

        Assert.Equal(SifenCdcQueryOutcome.Found, result.Outcome);
        Assert.Equal("123456789012345", result.ProtocolNumber);
    }

    [Fact]
    public void QueryParser_0420_ShouldBeNotFound()
    {
        Assert.Equal(SifenCdcQueryOutcome.NotFound, SifenCdcQueryResponseParser.Parse(QueryResponse("0420")).Outcome);
    }

    [Fact]
    public void QueryParser_0421_ShouldBeUnrecognized_BecauseManualContradictsItself()
    {
        Assert.Equal(SifenCdcQueryOutcome.Unrecognized, SifenCdcQueryResponseParser.Parse(QueryResponse("0421")).Outcome);
    }

    [Theory]
    [InlineData("")]
    [InlineData("<broken")]
    [InlineData("<env:Envelope xmlns:env=\"http://www.w3.org/2003/05/soap-envelope\"><env:Body><env:Fault/></env:Body></env:Envelope>")]
    public void QueryParser_EmptyBrokenOrFault_ShouldBeUnavailable(string raw)
    {
        Assert.Equal(SifenCdcQueryOutcome.Unavailable, SifenCdcQueryResponseParser.Parse(raw).Outcome);
    }

    [Theory]
    [InlineData(SifenCdcQueryOutcome.Found, SifenReconciliationDecision.ConfirmApproved)]
    [InlineData(SifenCdcQueryOutcome.NotFound, SifenReconciliationDecision.AllowResend)]
    [InlineData(SifenCdcQueryOutcome.Unrecognized, SifenReconciliationDecision.RemainIndeterminate)]
    [InlineData(SifenCdcQueryOutcome.Unavailable, SifenReconciliationDecision.RemainIndeterminate)]
    public void Decider_ShouldOnlyAllowResend_WhenSifenSaysNotFound(SifenCdcQueryOutcome outcome, SifenReconciliationDecision expected)
    {
        Assert.Equal(expected, SifenReconciliationDecider.Decide(new SifenCdcQueryResult(outcome, null, null, null, null, null)));
    }

    [Fact]
    public async Task NotConfiguredQueryGateway_ShouldReportPendingWithoutAnyCall()
    {
        var result = await new NotConfiguredSifenCdcQueryGateway()
            .QueryByCdcAsync(new QueryCdcCommand(Guid.NewGuid(), SifenInvoicing.Domain.Tenants.SifenEnvironmentType.Test, Cdc));

        Assert.Equal(SifenCdcQueryOutcome.Unavailable, result.Outcome);
        Assert.Contains("PENDIENTE", result.Detail, StringComparison.Ordinal);
    }
}
