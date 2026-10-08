using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Xml;
using Microsoft.EntityFrameworkCore;
using SifenInvoicing.Application.Auditing;
using SifenInvoicing.Application.Diagnostics;
using SifenInvoicing.Application.Security;
using SifenInvoicing.Application.XmlSigning;
using SifenInvoicing.Domain.Tenants;
using SifenInvoicing.Infrastructure.Persistence;

namespace SifenInvoicing.Infrastructure.XmlSigning;

public sealed class SifenXmlDocumentSigner : IXmlDocumentSigner
{
    private readonly SifenDbContext _dbContext;
    private readonly ITenantSecretProvider _secretProvider;
    private readonly IAuditTrail _auditTrail;
    private readonly ISystemClock _clock;

    public SifenXmlDocumentSigner(
        SifenDbContext dbContext,
        ITenantSecretProvider secretProvider,
        IAuditTrail auditTrail,
        ISystemClock clock)
    {
        _dbContext = dbContext;
        _secretProvider = secretProvider;
        _auditTrail = auditTrail;
        _clock = clock;
    }

    public async Task<SignedXmlDocumentResult> SignAsync(
        SignXmlDocumentCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var metadata = await _dbContext.TenantCertificateMetadata.IgnoreQueryFilters()
            .Where(certificate => certificate.TenantId == command.TenantId &&
                                  certificate.Environment == command.Environment &&
                                  certificate.Purpose == CertificatePurpose.XmlSignature &&
                                  certificate.IsActive)
            .OrderByDescending(certificate => certificate.ValidFrom)
            .FirstOrDefaultAsync(cancellationToken);

        if (metadata is null)
        {
            throw new InvalidOperationException("Active XML signature certificate metadata was not found for this tenant and environment.");
        }

        var pfxBytes = await _secretProvider.GetBinarySecretAsync(metadata.CertificateSecretReference, cancellationToken);
        var password = await _secretProvider.GetStringSecretAsync(metadata.CertificatePasswordSecretReference, cancellationToken);

        using var certificate = new X509Certificate2(
            pfxBytes,
            password,
            X509KeyStorageFlags.EphemeralKeySet);

        var signingMoment = _clock.UtcNow;
        if (signingMoment < certificate.NotBefore.ToUniversalTime() || signingMoment > certificate.NotAfter.ToUniversalTime())
        {
            throw new InvalidOperationException("The XML signature certificate is not valid at signing time.");
        }

        if (!certificate.HasPrivateKey)
        {
            throw new InvalidOperationException("The XML signature certificate does not contain a private key.");
        }

        var xmlDocument = LoadXml(command.Xml);
        var signedElement = new SifenSignedXml(xmlDocument).GetIdElement(xmlDocument, command.DocumentId);
        if (signedElement is null)
        {
            throw new InvalidOperationException($"XML element with Id '{command.DocumentId}' was not found.");
        }

        var existingSignature = signedElement.GetElementsByTagName("Signature", SignedXml.XmlDsigNamespaceUrl);
        if (existingSignature.Count > 0)
        {
            throw new InvalidOperationException($"XML element with Id '{command.DocumentId}' is already signed.");
        }

        var signedXml = new SifenSignedXml(xmlDocument)
        {
            SigningKey = certificate.GetRSAPrivateKey()
                ?? throw new InvalidOperationException("The XML signature certificate does not expose an RSA private key.")
        };

        signedXml.SignedInfo!.CanonicalizationMethod = SifenXmlSignatureAlgorithms.CanonicalizationMethod;
        signedXml.SignedInfo.SignatureMethod = SifenXmlSignatureAlgorithms.SignatureMethod;

        var reference = new Reference($"#{command.DocumentId}")
        {
            DigestMethod = SifenXmlSignatureAlgorithms.DigestMethod
        };
        reference.AddTransform(new XmlDsigEnvelopedSignatureTransform());
        signedXml.AddReference(reference);

        var keyInfo = new KeyInfo();
        var x509Data = new KeyInfoX509Data();
        x509Data.AddCertificate(certificate);
        keyInfo.AddClause(x509Data);
        signedXml.KeyInfo = keyInfo;

        signedXml.ComputeSignature();
        var signatureElement = signedXml.GetXml();
        var digestValue = Convert.ToBase64String(((Reference)signedXml.SignedInfo.References[0]!).DigestValue!);
        signedElement.ParentNode!.InsertAfter(xmlDocument.ImportNode(signatureElement, true), signedElement);

        var signedText = ToString(xmlDocument);
        VerifyRoundTrip(signedText, command.DocumentId, certificate);
        var fingerprint = Convert.ToHexString(certificate.GetCertHash(System.Security.Cryptography.HashAlgorithmName.SHA256));

        await RecordAuditAsync(command, metadata, digestValue, cancellationToken);

        return new SignedXmlDocumentResult(
            signedText,
            command.DocumentId,
            SifenXmlSignatureAlgorithms.CanonicalizationMethod,
            SifenXmlSignatureAlgorithms.SignatureMethod,
            SifenXmlSignatureAlgorithms.DigestMethod,
            SifenXmlSignatureAlgorithms.EnvelopedSignatureTransform,
            digestValue,
            fingerprint);
    }

    /// <summary>Re-parsea el XML firmado y verifica la firma con la clave publica del certificado (autoverificacion).</summary>
    private static void VerifyRoundTrip(string signedText, string documentId, X509Certificate2 certificate)
    {
        var reloaded = LoadXml(signedText);
        var verifier = new SifenSignedXml(reloaded);
        var signatureNode = reloaded.GetElementsByTagName("Signature", SignedXml.XmlDsigNamespaceUrl);
        if (signatureNode.Count != 1)
        {
            throw new InvalidOperationException("The signed XML must contain exactly one ds:Signature.");
        }

        verifier.LoadXml((XmlElement)signatureNode[0]!);
        if (!verifier.CheckSignature(certificate, true))
        {
            throw new InvalidOperationException($"The generated signature for '{documentId}' did not verify.");
        }
    }

    private async Task RecordAuditAsync(
        SignXmlDocumentCommand command,
        TenantCertificateMetadata metadata,
        string digestValue,
        CancellationToken cancellationToken)
    {
        await _auditTrail.RecordAsync(new AuditEvent
        {
            EventName = "xml.signed",
            Category = AuditCategory.XmlSignature,
            Severity = AuditSeverity.Information,
            OccurredAt = _clock.UtcNow,
            TenantId = command.TenantId.ToString(),
            ResourceType = "XmlDocument",
            ResourceId = command.DocumentId,
            Outcome = "Signed",
            Metadata = new Dictionary<string, string?>
            {
                ["sifen.environment"] = command.Environment.ToString(),
                ["certificate.alias"] = metadata.Alias,
                ["certificate.fingerprint_sha256"] = metadata.FingerprintSha256,
                ["signature.method"] = SifenXmlSignatureAlgorithms.SignatureMethod,
                ["digest.method"] = SifenXmlSignatureAlgorithms.DigestMethod,
                ["digest.value"] = digestValue
            }
        }, cancellationToken);
    }

    private static XmlDocument LoadXml(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            throw new InvalidOperationException("XML content is required.");
        }

        var document = new XmlDocument
        {
            PreserveWhitespace = true
        };
        document.LoadXml(xml);
        return document;
    }

    private static string ToString(XmlDocument document)
    {
        using var stringWriter = new StringWriterWithEncoding(Encoding.UTF8);
        using var xmlWriter = XmlWriter.Create(stringWriter, new XmlWriterSettings
        {
            Encoding = Encoding.UTF8,
            OmitXmlDeclaration = document.FirstChild is not XmlDeclaration,
            Indent = false
        });
        document.Save(xmlWriter);
        return stringWriter.ToString();
    }

    private sealed class StringWriterWithEncoding : StringWriter
    {
        private readonly Encoding _encoding;

        public StringWriterWithEncoding(Encoding encoding)
        {
            _encoding = encoding;
        }

        public override Encoding Encoding => _encoding;
    }
}
