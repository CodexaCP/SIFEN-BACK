using SifenInvoicing.Application.Fiscal;

namespace SifenInvoicing.Application.XmlDe;

/// <summary>
/// Entrada del <see cref="SifenDeXmlBuilder"/> (Fase 4.2, DE tipo 01). Los montos llegan YA calculados en
/// <see cref="Fiscal"/> (FiscalCalculationEngine); el builder no calcula. Las fechas son hora fiscal local sin zona
/// (formato yyyy-MM-ddTHH:mm:ss); la zona horaria definitiva esta PENDIENTE DOC.
/// </summary>
public sealed record SifenDeBuildInput(
    string Cdc,
    DateTime FechaEmision,
    DateTime FechaFirma,
    SifenDeEnvironment Ambiente,
    SifenDeStamp Timbrado,
    SifenDeEmitter Emisor,
    SifenDeReceiver Receptor,
    SifenDeOperation Operacion,
    IReadOnlyList<SifenDeItem> Items,
    FiscalDocumentModel Fiscal);

public enum SifenDeEnvironment
{
    Test = 1,
    Production = 2,
}

/// <summary>gTimb (C001-C010). dFeFinT NO existe en el XSD v150 y no se emite.</summary>
public sealed record SifenDeStamp(
    string StampingNumber,
    string Establishment,
    string ExpeditionPoint,
    string DocumentNumber,
    DateOnly ValidFrom,
    string? Series = null);

public sealed record SifenDeEconomicActivity(string Code, string Description);

/// <summary>
/// gEmis (D100-D132). Todos los campos obligatorios del Manual deben venir informados: el builder no inventa valores
/// (ver dependencias de modelo incompleto: actividad economica no existe hoy en TaxpayerProfile).
/// </summary>
public sealed record SifenDeEmitter(
    string Ruc,
    string RucCheckDigit,
    int TaxpayerType,
    string LegalName,
    string Address,
    int? HouseNumber,
    int? DepartmentCode,
    string? DepartmentDescription,
    int? CityCode,
    string? CityDescription,
    string? Phone,
    string? Email,
    IReadOnlyList<SifenDeEconomicActivity> EconomicActivities,
    int? DistrictCode = null,
    string? DistrictDescription = null);

public enum SifenDeReceiverNature
{
    Taxpayer = 1,
    NonTaxpayer = 2,
}

public enum SifenDeOperationType
{
    B2B = 1,
    B2C = 2,
}

/// <summary>gDatRec (D200-D224). Contribuyente: RUC + DV. No contribuyente: tipo y numero de documento de identidad.</summary>
public sealed record SifenDeReceiver(
    SifenDeReceiverNature Nature,
    SifenDeOperationType OperationType,
    string Name,
    string CountryCode = "PRY",
    string CountryDescription = "Paraguay",
    int? TaxpayerKind = null,
    string? Ruc = null,
    string? RucCheckDigit = null,
    int? IdentityDocumentType = null,
    string? IdentityDocumentNumber = null);

/// <summary>Datos comerciales de la operacion (D011, E011) y condicion contado (E601).</summary>
public sealed record SifenDeOperation(
    int TransactionType,
    int PresenceIndicator,
    string CurrencyCode = "PYG",
    string CurrencyDescription = "Guarani");

/// <summary>Datos de catalogo del item; cantidades, precios e IVA salen de <see cref="FiscalLine"/> (misma posicion).</summary>
public sealed record SifenDeItem(string Code, string Description, int UnitCode, string UnitDescription);

/// <summary>Resultado del builder: DE sin Signature ni gCamFuFD (fases posteriores). Contiene el CDC recibido, no uno nuevo.</summary>
public sealed record SifenDeBuildResult(string Cdc, string Xml);
