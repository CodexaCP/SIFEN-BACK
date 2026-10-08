using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using SifenInvoicing.Application.Qr;
using SifenInvoicing.Application.Security;
using SifenInvoicing.Domain.Common;
using SifenInvoicing.Domain.Tenants;
using SifenInvoicing.Infrastructure.Persistence;

namespace SifenInvoicing.Infrastructure.Qr;

/// <summary>
/// Fase 4.5. IdCSC y referencia al secreto salen de TenantSifenSettings del tenant EFECTIVO y del ambiente del tenant;
/// el CSC se resuelve con el ITenantSecretProvider existente (nunca en BD, logs ni respuestas). Inserta
/// gCamFuFD/dCarQR como ultimo hijo de rDE (tras Signature) sin reserializar DE ni Signature (la firma sigue valida).
/// </summary>
public sealed partial class SifenDeQrAttacher : ISifenDeQrAttacher
{
    private static readonly XNamespace Sifen = "http://ekuatia.set.gov.py/sifen/xsd";

    private readonly SifenDbContext _dbContext;
    private readonly ITenantSecretProvider _secretProvider;
    private readonly ISifenQrBuilder _qrBuilder;

    public SifenDeQrAttacher(SifenDbContext dbContext, ITenantSecretProvider secretProvider, ISifenQrBuilder qrBuilder)
    {
        _dbContext = dbContext;
        _secretProvider = secretProvider;
        _qrBuilder = qrBuilder;
    }

    public async Task<string> AttachAsync(
        Guid tenantId,
        SifenEnvironmentType environment,
        string signedDeXml,
        CancellationToken cancellationToken = default)
    {
        var document = XDocument.Parse(signedDeXml);
        if (document.Root?.Element(Sifen + "gCamFuFD") is not null)
        {
            throw new DomainException("El rDE ya contiene gCamFuFD.");
        }

        var data = SifenDeQrDataExtractor.Extract(document);

        var settings = await _dbContext.TenantSifenSettings.IgnoreQueryFilters()
            .FirstOrDefaultAsync(item => item.TenantId == tenantId && item.Environment == environment && item.IsActive, cancellationToken);
        if (settings is null || string.IsNullOrWhiteSpace(settings.CscIdentifier) || string.IsNullOrWhiteSpace(settings.CscSecretReference))
        {
            throw new DomainException("El tenant no tiene IdCSC y referencia de secreto CSC configurados para este ambiente.");
        }

        // Manual 13.8.2: IdCSC longitud maxima 4; 13.8.1: CSC de 32 caracteres alfanumericos.
        if (settings.CscIdentifier.Length > 4)
        {
            throw new DomainException("IdCSC invalido: longitud maxima 4 (Manual v150 13.8.2).");
        }

        string csc;
        try
        {
            csc = (await _secretProvider.GetStringSecretAsync(settings.CscSecretReference, cancellationToken)).Trim();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new DomainException("No se pudo resolver el secreto CSC del tenant.");
        }

        if (!CscPattern().IsMatch(csc))
        {
            throw new DomainException("El CSC del tenant no cumple el formato oficial (32 caracteres alfanumericos, Manual v150 13.8.1).");
        }

        var qr = _qrBuilder.Build(new SifenQrInput(
            environment == SifenEnvironmentType.Production ? SifenQrEnvironment.Production : SifenQrEnvironment.Test,
            data.Cdc,
            data.EmissionDateTimeText,
            data.ReceiverDocument,
            data.TotalGeneral,
            data.TotalIva,
            data.ItemCount,
            data.DigestValueBase64,
            settings.CscIdentifier,
            csc,
            ReceiverParameterName: data.ReceiverParameterName));

        // "&" -> "&amp;" (Manual 13.8.4.5) lo hace el escape de texto XML.
        var element = $"<gCamFuFD><dCarQR>{new XText(qr.Url)}</dCarQR></gCamFuFD>";
        var closing = signedDeXml.LastIndexOf("</rDE>", StringComparison.Ordinal);
        if (closing < 0)
        {
            throw new DomainException("El rDE firmado debe usar el elemento raiz sin prefijo (rDE) para agregar gCamFuFD.");
        }

        return signedDeXml.Insert(closing, element);
    }

    [GeneratedRegex("^[A-Za-z0-9]{32}$")]
    private static partial Regex CscPattern();
}
