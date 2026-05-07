namespace SifenInvoicing.Application.Sifen;

public interface ISifenResponseParser
{
    ParsedSifenResponse ParseResponse(string? rawResponse);
}
