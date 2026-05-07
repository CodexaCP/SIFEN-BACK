using System.Security.Cryptography.Xml;
using System.Xml;

namespace SifenInvoicing.Infrastructure.XmlSigning;

public sealed class SifenSignedXml : SignedXml
{
    public SifenSignedXml(XmlDocument document)
        : base(document)
    {
    }

    public override XmlElement? GetIdElement(XmlDocument? document, string idValue)
    {
        if (document is null)
        {
            return null;
        }

        var baseElement = base.GetIdElement(document, idValue);
        if (baseElement is not null)
        {
            return baseElement;
        }

        return FindElementById(document.DocumentElement, idValue);
    }

    private static XmlElement? FindElementById(XmlElement? element, string idValue)
    {
        if (element is null)
        {
            return null;
        }

        if (element.HasAttribute("Id") && element.GetAttribute("Id") == idValue)
        {
            return element;
        }

        foreach (XmlNode child in element.ChildNodes)
        {
            if (child is XmlElement childElement)
            {
                var match = FindElementById(childElement, idValue);
                if (match is not null)
                {
                    return match;
                }
            }
        }

        return null;
    }
}
