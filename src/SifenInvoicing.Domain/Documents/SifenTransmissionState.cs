namespace SifenInvoicing.Domain.Documents;

/// <summary>
/// Estado INTERNO de la transmision del DE hacia SIFEN. No es un estado oficial de SIFEN y es independiente del
/// estado interno de la factura (<see cref="FeInvoiceInternalStatus"/>) y del estado fiscal (<see cref="SifenFiscalState"/>).
/// </summary>
public enum SifenTransmissionState
{
    NotSent = 0,
    Sending = 1,

    /// <summary>SIFEN respondio con un mensaje interpretable (aprobado, rechazado u otro resultado de procesamiento).</summary>
    Delivered = 2,

    /// <summary>Hay evidencia de que SIFEN no proceso el DE (p. ej. conexion rechazada o HTTP 4xx); el reenvio es seguro.</summary>
    NotDelivered = 3,

    /// <summary>No se sabe si SIFEN recibio el DE (timeout, corte, 5xx, respuesta ilegible). Prohibido reenviar sin consultar por CDC.</summary>
    Indeterminate = 4
}
