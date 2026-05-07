namespace SifenInvoicing.Application.Cdc;

public sealed record GenerateCdcInput(
    string TipoDoc,
    string Ruc,
    string DvRuc,
    string Establecimiento,
    string PuntoExpedicion,
    string NumeroDe,
    string TipoContribuyente,
    string TipoEmision,
    string CodigoSeguridad,
    string FechaEmision);
