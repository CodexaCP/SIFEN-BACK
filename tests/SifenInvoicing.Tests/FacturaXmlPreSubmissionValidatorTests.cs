using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using SifenInvoicing.Application.Cdc;
using SifenInvoicing.Application.Invoices;
using SifenInvoicing.Domain.Common;
using SifenInvoicing.Domain.Tenants;
using SifenInvoicing.Infrastructure.Diagnostics;
using SifenInvoicing.Infrastructure.Invoices;
using SifenInvoicing.Infrastructure.Persistence;
using SifenInvoicing.Infrastructure.Tenancy;
using SifenInvoicing.Infrastructure.XmlValidation;

namespace SifenInvoicing.Tests;

public sealed class FacturaXmlPreSubmissionValidatorTests
{
    [Fact]
    public async Task ValidateTipoDoc01Async_ShouldPass_WhenXmlMatchesConfiguredSchema()
    {
        var tenantId = Guid.NewGuid();
        var generator = new FacturaXmlGenerator();
        var generated = generator.GenerateFacturaXML(CreateInput());
        var schemaPath = await WriteTempSchemaAsync("""
            <?xml version="1.0" encoding="utf-8"?>
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema"
                       targetNamespace="http://ekuatia.set.gov.py/sifen/xsd"
                       xmlns="http://ekuatia.set.gov.py/sifen/xsd"
                       elementFormDefault="qualified">
              <xs:element name="rDE">
                <xs:complexType>
                  <xs:sequence>
                    <xs:element name="dVerFor" type="xs:string"/>
                    <xs:element name="DE">
                      <xs:complexType>
                        <xs:sequence>
                          <xs:element name="dDVId" type="xs:string"/>
                          <xs:element name="dFecFirma" type="xs:string"/>
                          <xs:element name="dSisFact" type="xs:string"/>
                          <xs:any minOccurs="0" maxOccurs="unbounded" processContents="skip"/>
                        </xs:sequence>
                        <xs:attribute name="Id" type="xs:string" use="required"/>
                      </xs:complexType>
                    </xs:element>
                  </xs:sequence>
                </xs:complexType>
              </xs:element>
            </xs:schema>
            """);

        try
        {
            var configuration = new ConfigurationBuilder()
                .Build();
            await using var dbContext = CreateDbContext();
            dbContext.TenantSifenSettings.Add(TenantSifenSettings.Create(
                tenantId,
                SifenEnvironmentType.Test,
                "0001",
                "config:csc",
                "001",
                "001",
                "0000001",
                "12345678",
                "config:certificate",
                "config:password",
                "alias",
                schemaPath,
                null,
                null));
            await dbContext.SaveChangesAsync();
            var validator = new FacturaXmlPreSubmissionValidator(new XmlSchemaValidator(), configuration, dbContext);

            await validator.ValidateTipoDoc01Async(generated.Xml, generated.Cdc, tenantId, SifenEnvironmentType.Test);
        }
        finally
        {
            File.Delete(schemaPath);
        }
    }

    [Fact]
    public async Task ValidateTipoDoc01Async_ShouldFail_WhenRequiredFieldIsMissing()
    {
        var tenantId = Guid.NewGuid();
        var generator = new FacturaXmlGenerator();
        var generated = generator.GenerateFacturaXML(CreateInput());
        var invalidXml = generated.Xml.Replace("<dVerFor>150</dVerFor>", string.Empty, StringComparison.Ordinal);
        var schemaPath = await WriteTempSchemaAsync("""
            <?xml version="1.0" encoding="utf-8"?>
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema"
                       targetNamespace="http://ekuatia.set.gov.py/sifen/xsd"
                       xmlns="http://ekuatia.set.gov.py/sifen/xsd"
                       elementFormDefault="qualified">
              <xs:element name="rDE">
                <xs:complexType>
                  <xs:sequence>
                    <xs:element name="dVerFor" type="xs:string"/>
                    <xs:element name="DE">
                      <xs:complexType>
                        <xs:sequence>
                          <xs:element name="dDVId" type="xs:string"/>
                          <xs:element name="dFecFirma" type="xs:string"/>
                          <xs:element name="dSisFact" type="xs:string"/>
                          <xs:any minOccurs="0" maxOccurs="unbounded" processContents="skip"/>
                        </xs:sequence>
                        <xs:attribute name="Id" type="xs:string" use="required"/>
                      </xs:complexType>
                    </xs:element>
                  </xs:sequence>
                </xs:complexType>
              </xs:element>
            </xs:schema>
            """);

        try
        {
            var configuration = new ConfigurationBuilder()
                .Build();
            await using var dbContext = CreateDbContext();
            dbContext.TenantSifenSettings.Add(TenantSifenSettings.Create(
                tenantId,
                SifenEnvironmentType.Test,
                "0001",
                "config:csc",
                "001",
                "001",
                "0000001",
                "12345678",
                "config:certificate",
                "config:password",
                "alias",
                schemaPath,
                null,
                null));
            await dbContext.SaveChangesAsync();
            var validator = new FacturaXmlPreSubmissionValidator(new XmlSchemaValidator(), configuration, dbContext);

            var exception = await Assert.ThrowsAsync<DomainException>(() =>
                validator.ValidateTipoDoc01Async(invalidXml, generated.Cdc, tenantId, SifenEnvironmentType.Test));

            Assert.Contains("dVerFor", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(schemaPath);
        }
    }

    [Fact]
    public async Task ValidateTipoDoc01Async_ShouldFail_WhenOfficialSchemaIsNotConfigured()
    {
        var generator = new FacturaXmlGenerator();
        var generated = generator.GenerateFacturaXML(CreateInput());
        var configuration = new ConfigurationBuilder().Build();
        await using var dbContext = CreateDbContext();
        var validator = new FacturaXmlPreSubmissionValidator(new XmlSchemaValidator(), configuration, dbContext);

        var exception = await Assert.ThrowsAsync<DomainException>(() =>
            validator.ValidateTipoDoc01Async(generated.Xml, generated.Cdc, Guid.NewGuid(), SifenEnvironmentType.Test));

        Assert.Contains("Official XSD path", exception.Message, StringComparison.Ordinal);
    }

    private static GenerateFacturaXmlInput CreateInput()
    {
        return new GenerateFacturaXmlInput(
            new GenerateCdcInput("01", "80012345", "6", "1", "1", "123", "1", "1", "123456789", "20260425"),
            new DateTimeOffset(2026, 4, 25, 10, 30, 0, TimeSpan.FromHours(-4)),
            1,
            "ACME Paraguay SA",
            "Asuncion 123",
            "Cliente Demo",
            InvoiceReceiverDocumentType.Ruc,
            "80099999",
            InvoiceCurrency.PYG,
            InvoiceSaleCondition.Cash,
            TestFiscal.Model(("Servicio mensual", 1m, 100000m, InvoiceVatType.Vat10)),
            TestFiscal.Descriptions(("Servicio mensual", 1m, 100000m, InvoiceVatType.Vat10)));
    }

    private static async Task<string> WriteTempSchemaAsync(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.xsd");
        await File.WriteAllTextAsync(path, content);
        return path;
    }

    private static SifenDbContext CreateDbContext()
    {
        var tenantAccessor = new AsyncLocalTenantContextAccessor();
        var options = new DbContextOptionsBuilder<SifenDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new SifenDbContext(options, new SystemClock(), tenantAccessor);
    }
}
