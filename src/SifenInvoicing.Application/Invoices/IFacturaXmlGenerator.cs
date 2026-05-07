namespace SifenInvoicing.Application.Invoices;

public interface IFacturaXmlGenerator
{
    GeneratedFacturaXmlResult GenerateFacturaXML(GenerateFacturaXmlInput input);
}
