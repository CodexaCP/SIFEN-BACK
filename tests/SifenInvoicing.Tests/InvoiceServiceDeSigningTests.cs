using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using SifenInvoicing.Application.XmlSigning;
using SifenInvoicing.Domain.Common;
using SifenInvoicing.Infrastructure.Diagnostics;
using SifenInvoicing.Infrastructure.Invoices;
using SifenInvoicing.Tests.XmlDe;

namespace SifenInvoicing.Tests;

/// <summary>
/// Fase 4.4 - la firma XMLDSig ocurre DENTRO de la transaccion, antes del commit. Certificado efimero solo de prueba
/// (nunca evidencia de aceptacion SIFEN). SQLite porque el proveedor InMemory no tiene transacciones.
/// </summary>
public sealed partial class InvoiceServiceTests
{
    private static EfInvoiceService ServiceWithSigner(Fixture f, SifenInvoicing.Infrastructure.Persistence.SifenDbContext db, IXmlDocumentSigner signer) =>
        new(db, f.Accessor, DeTestKit.Builder(), DeTestKit.Xsd(),
            new InvoiceKudePdfRenderer(), new ReadyTenantCertificateValidator(), signer,
            new StubOperationalReadinessReporter(), new CountingSubmissionGateway(), new FakeResponseParser(),
            CreateConfiguration(), new NullAuditTrail(), new SystemClock(),
            new SifenInvoicing.Infrastructure.Numbering.EfNumberingService(db), new TestFiscalClock());

    private sealed class ThrowingSigner : IXmlDocumentSigner
    {
        public Task<SignedXmlDocumentResult> SignAsync(SignXmlDocumentCommand command, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("forced signing failure");
    }

    private sealed class UnsignedXmlSigner : IXmlDocumentSigner
    {
        public Task<SignedXmlDocumentResult> SignAsync(SignXmlDocumentCommand command, CancellationToken cancellationToken = default)
            => Task.FromResult(new SignedXmlDocumentResult(command.Xml, command.DocumentId, "c14n", "rsa-sha256", "sha256", "enveloped"));
    }

    [XsdPackageFact(XsdPackage.ReceptionRoot)]
    public async Task DeFlow_RealSignature_IsPersistedBeforeCommit_WithOriginalXmlAndDigestEvidence()
    {
        using var scope = new SqliteScope();
        using var identity = new TestSigningIdentity();
        var f = await SqliteFixtureAsync(scope);
        f.Use();
        var reference = "config:certificate";
        var signer = SigningTestKit.Signer(f.Db, new Dictionary<string, byte[]> { [reference] = identity.Pfx });
        var service = ServiceWithSigner(f, f.Db, signer);

        var result = await service.CreateAsync(TwoItemCommand("signed-ok"));

        await using var check = scope.NewContext(f.Accessor);
        var doc = await check.Documents.SingleAsync();
        Assert.NotNull(doc.SignedAt);
        Assert.Equal(doc.SignedXmlPayload, result.SignedXmlPayload);

        // XML original (sin firma) conservado como evidencia previa; el firmado verifica con la clave publica.
        Assert.Null(XDocument.Parse(doc.XmlPayload).Root!.Element(SigningTestKit.Ds + "Signature"));
        Assert.True(SigningTestKit.CheckSignature(doc.SignedXmlPayload!, identity.Certificate));
        var signedRoot = XDocument.Parse(doc.SignedXmlPayload!).Root!;
        Assert.Equal("#" + doc.Cdc, signedRoot.Element(SigningTestKit.Ds + "Signature")!.Descendants(SigningTestKit.Ds + "Reference").Single().Attribute("URI")!.Value);
        // El DE firmado es exactamente el DE persistido sin firma.
        Assert.Equal(
            XDocument.Parse(doc.XmlPayload).Root!.Element(Sifen + "DE")!.ToString(SaveOptions.DisableFormatting),
            signedRoot.Element(Sifen + "DE")!.ToString(SaveOptions.DisableFormatting));
        Assert.Contains(await check.DocumentLogs.Select(l => l.EventType).ToListAsync(), t => t == "xml.signed");
        // Ningun secreto en la evidencia persistida.
        Assert.DoesNotContain(TestSigningIdentity.Password, doc.SignedXmlPayload!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeFlow_SigningFailure_RollsBackAndLeavesNoValidIssuance_ThenSameKeyCanBeRetried()
    {
        using var scope = new SqliteScope();
        var f = await SqliteFixtureAsync(scope);
        f.Use();
        var failing = ServiceWithSigner(f, f.Db, new ThrowingSigner());

        var ex = await Assert.ThrowsAsync<UserFacingException>(() => failing.CreateAsync(TwoItemCommand("sign-fail")));

        Assert.Equal("XML_SIGNATURE_FAILED", ex.ErrorCode);
        await using (var check = scope.NewContext(f.Accessor))
        {
            Assert.Empty(await check.Documents.ToListAsync());
            Assert.Empty(await check.DocumentLines.ToListAsync());
            Assert.Empty(await check.IdempotencyRecords.ToListAsync());
            Assert.Equal(1, await check.NumberingSequences.Select(s => s.NextNumber).SingleAsync());
        }

        var retry = await (await RebuildServiceAsync(f, scope, DeTestKit.Builder())).CreateAsync(TwoItemCommand("sign-fail"));
        Assert.NotNull(retry.Cdc);
    }

    [XsdPackageFact(XsdPackage.ReceptionRoot)]
    public async Task DeFlow_SignerReturningXmlWithoutSignature_FailsSignedValidationAndRollsBack()
    {
        using var scope = new SqliteScope();
        var f = await SqliteFixtureAsync(scope);
        f.Use();
        var service = ServiceWithSigner(f, f.Db, new UnsignedXmlSigner());

        var ex = await Assert.ThrowsAsync<UserFacingException>(() => service.CreateAsync(TwoItemCommand("no-signature")));

        Assert.Equal("XML_SIGNATURE_FAILED", ex.ErrorCode);
        await using var check = scope.NewContext(f.Accessor);
        Assert.Empty(await check.Documents.ToListAsync());
        Assert.Equal(1, await check.NumberingSequences.Select(s => s.NextNumber).SingleAsync());
    }
}
