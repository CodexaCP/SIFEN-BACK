namespace SifenInvoicing.Domain.Documents;

/// <summary>Estado FISCAL conocido del DE en SIFEN segun la ultima respuesta/consulta. Estado interno de la aplicacion.</summary>
public enum SifenFiscalState
{
    None = 0,
    Approved = 1,
    ApprovedWithObservations = 2,
    Rejected = 3,

    /// <summary>La consulta por CDC informo que el DE no existe en SIFEN (o no esta aprobado).</summary>
    NotFoundInSifen = 4
}
