using System.Xml;
using System.Xml.Schema;

namespace SifenInvoicing.Tests.XmlDe;

/// <summary>Prueba la infraestructura (XsdModel/XsdPackage) con un XSD SINTETICO propio. No es el XSD oficial.</summary>
public sealed class XsdModelTests
{
    private const string Xsd = """
<xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema" targetNamespace="urn:t" xmlns="urn:t" elementFormDefault="qualified">
  <xs:element name="r"><xs:complexType><xs:sequence>
    <xs:element name="a" type="xs:string"/>
    <xs:element name="b" minOccurs="0"><xs:simpleType><xs:restriction base="xs:string"><xs:maxLength value="3"/></xs:restriction></xs:simpleType></xs:element>
    <xs:choice><xs:element name="c" type="xs:int"/><xs:element name="d" type="xs:int"/></xs:choice>
  </xs:sequence></xs:complexType></xs:element>
</xs:schema>
""";

    private static XmlSchemaSet Set()
    {
        var set = new XmlSchemaSet();
        set.Add(XmlSchema.Read(new StringReader(Xsd), null)!);
        set.Compile();
        return set;
    }

    [Fact]
    public void Children_ReturnsOrderCardinalityAndChoice()
    {
        var kids = XsdModel.Children(XsdModel.FindElement(Set(), "r")!);
        Assert.Equal(new[] { "a", "b", "c", "d" }, kids.Select(k => k.Name));
        Assert.Equal(0, kids[1].Min);
        Assert.True(kids[2].InChoice && kids[3].InChoice);
        Assert.False(kids[0].InChoice);
    }

    [Fact]
    public void Facets_ReportsMaxLength()
    {
        var b = XsdModel.FindElement(Set(), "b")!;
        Assert.Equal("3", XsdModel.Facets(b)["MaxLengthFacet"].Single());
    }

    [Fact]
    public void Closure_ResolvesRelativeIncludesAndReportsMissing()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "a.xsd"), "<xs:schema xmlns:xs=\"http://www.w3.org/2001/XMLSchema\"><xs:include schemaLocation=\"b.xsd\"/><xs:import namespace=\"x\" schemaLocation=\"http://h/x/c.xsd\"/></xs:schema>");
            File.WriteAllText(Path.Combine(dir, "b.xsd"), "<xs:schema xmlns:xs=\"http://www.w3.org/2001/XMLSchema\"/>");
            var closure = XsdPackage.Closure(dir, "a.xsd");
            Assert.Equal(3, closure.Count);
            Assert.True(closure.Single(c => c.File == "b.xsd").Exists);
            Assert.False(closure.Single(c => c.File == "c.xsd").Exists);
        }
        finally { Directory.Delete(dir, true); }
    }
}
