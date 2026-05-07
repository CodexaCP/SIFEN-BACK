using SifenInvoicing.Infrastructure.XmlValidation;

namespace SifenInvoicing.Tests;

public sealed class XmlSchemaValidatorTests
{
    [Fact]
    public async Task ValidateAsync_ShouldReturnValid_WhenXmlMatchesSchema()
    {
        var schemaPath = await WriteTempSchemaAsync("""
            <?xml version="1.0" encoding="utf-8"?>
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema" elementFormDefault="qualified">
              <xs:element name="Invoice">
                <xs:complexType>
                  <xs:sequence>
                    <xs:element name="Id" type="xs:string"/>
                  </xs:sequence>
                </xs:complexType>
              </xs:element>
            </xs:schema>
            """);

        try
        {
            var validator = new XmlSchemaValidator();
            var result = await validator.ValidateAsync("<Invoice><Id>123</Id></Invoice>", schemaPath);

            Assert.True(result.IsValid);
            Assert.Empty(result.Errors);
        }
        finally
        {
            File.Delete(schemaPath);
        }
    }

    [Fact]
    public async Task ValidateAsync_ShouldReturnErrors_WhenXmlViolatesSchema()
    {
        var schemaPath = await WriteTempSchemaAsync("""
            <?xml version="1.0" encoding="utf-8"?>
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema" elementFormDefault="qualified">
              <xs:element name="Invoice">
                <xs:complexType>
                  <xs:sequence>
                    <xs:element name="Id" type="xs:string"/>
                  </xs:sequence>
                </xs:complexType>
              </xs:element>
            </xs:schema>
            """);

        try
        {
            var validator = new XmlSchemaValidator();
            var result = await validator.ValidateAsync("<Invoice><Missing>123</Missing></Invoice>", schemaPath);

            Assert.False(result.IsValid);
            Assert.NotEmpty(result.Errors);
        }
        finally
        {
            File.Delete(schemaPath);
        }
    }

    private static async Task<string> WriteTempSchemaAsync(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.xsd");
        await File.WriteAllTextAsync(path, content);
        return path;
    }
}
