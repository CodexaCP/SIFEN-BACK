using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Xml;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using SifenInvoicing.Application.Auditing;
using SifenInvoicing.Application.Security;
using SifenInvoicing.Application.Tenancy;
using SifenInvoicing.Domain.Tenants;
using SifenInvoicing.Infrastructure.Diagnostics;
using SifenInvoicing.Infrastructure.Persistence;
using SifenInvoicing.Infrastructure.Tenancy;
using SifenInvoicing.Infrastructure.XmlSigning;

namespace SifenInvoicing.Tests.XmlDe;

/// <summary>
/// SOLO PRUEBAS (Fase 4.4). Certificados AUTOFIRMADOS EFIMEROS generados en memoria: sin vinculo con ningun contribuyente
/// ni con SIFEN, no son configuracion de produccion ni evidencia de aceptacion SIFEN. Se descartan al terminar la prueba.
/// </summary>
internal sealed class TestSigningIdentity : IDisposable
{
    public const string Password = "ephemeral-test-password";

    private readonly RSA _rsa = RSA.Create(2048);
    public X509Certificate2 Certificate { get; }
    public byte[] Pfx { get; }

    public TestSigningIdentity(DateTimeOffset? notBefore = null, DateTimeOffset? notAfter = null)
    {
        var request = new CertificateRequest("CN=PRUEBA-EFIMERO", _rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        Certificate = request.CreateSelfSigned(notBefore ?? DateTimeOffset.UtcNow.AddDays(-1), notAfter ?? DateTimeOffset.UtcNow.AddDays(1));
        Pfx = Certificate.Export(X509ContentType.Pfx, Password);
    }

    public void Dispose()
    {
        Certificate.Dispose();
        _rsa.Dispose();
    }
}

internal sealed class MapSecrets(IReadOnlyDictionary<string, byte[]> pfxByReference) : ITenantSecretProvider
{
    public Task<SecretCheckResult> CheckStringSecretAsync(string secretReference, string checkName, CancellationToken cancellationToken = default)
        => Task.FromResult(new SecretCheckResult(checkName, SecretStatus.Present, "OK"));

    public Task<SecretCheckResult> CheckBinarySecretAsync(string secretReference, string checkName, CancellationToken cancellationToken = default)
        => Task.FromResult(new SecretCheckResult(checkName, SecretStatus.Present, "OK"));

    public Task<string> GetStringSecretAsync(string secretReference, CancellationToken cancellationToken = default)
        => Task.FromResult(TestSigningIdentity.Password);

    public Task<byte[]> GetBinarySecretAsync(string secretReference, CancellationToken cancellationToken = default)
        => Task.FromResult(pfxByReference[secretReference]);
}

internal sealed class NullAuditSink : IAuditTrail
{
    public List<AuditEvent> Events { get; } = [];

    public Task RecordAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        Events.Add(auditEvent);
        return Task.CompletedTask;
    }
}

internal static class SigningTestKit
{
    public static readonly XNamespace Sifen = DeReferenceStructure.Namespace;
    public static readonly XNamespace Ds = "http://www.w3.org/2000/09/xmldsig#";

    public static TenantCertificateMetadata Metadata(Guid tenantId, string reference) =>
        TenantCertificateMetadata.Create(
            tenantId, SifenEnvironmentType.Test, CertificatePurpose.XmlSignature, "xml-signing", "CN=PRUEBA-EFIMERO", "AB", "01",
            reference, reference + ":pw", DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));

    public static SifenDbContext InMemoryDb(Guid tenantId, string? databaseName = null)
    {
        var accessor = new AsyncLocalTenantContextAccessor();
        accessor.SetCurrent(new TenantContext { TenantId = tenantId.ToString(), ResolvedTenantId = tenantId, IsResolved = true });
        return new SifenDbContext(
            new DbContextOptionsBuilder<SifenDbContext>().UseInMemoryDatabase(databaseName ?? Guid.NewGuid().ToString()).Options,
            new SystemClock(), accessor);
    }

    public static SifenXmlDocumentSigner Signer(SifenDbContext db, IReadOnlyDictionary<string, byte[]> secrets, NullAuditSink? audit = null) =>
        new(db, new MapSecrets(secrets), audit ?? new NullAuditSink(), new SystemClock());

    /// <summary>Verifica la firma con la clave publica (SignedXml). gCamFuFD no participa.</summary>
    public static bool CheckSignature(string signedXml, X509Certificate2 certificate)
    {
        var dom = new XmlDocument { PreserveWhitespace = true };
        dom.LoadXml(signedXml);
        var verifier = new SifenSignedXml(dom);
        verifier.LoadXml((XmlElement)dom.GetElementsByTagName("Signature", SignedXml.XmlDsigNamespaceUrl)[0]!);
        return verifier.CheckSignature(certificate, true);
    }

    /// <summary>C14N inclusiva 1.0 de un elemento aislado (sus namespaces en alcance son los mismos en el documento).</summary>
    public static byte[] Canonicalize(XElement element)
    {
        var dom = new XmlDocument { PreserveWhitespace = true };
        dom.LoadXml(new XDocument(new XElement(element)).ToString(SaveOptions.DisableFormatting));
        var transform = new XmlDsigC14NTransform();
        transform.LoadInput(dom);
        using var stream = (Stream)transform.GetOutput(typeof(Stream));
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
