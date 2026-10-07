namespace SifenInvoicing.Application.Fiscal;

/// <summary>
/// Hora fiscal (fecha/hora de emision dFeEmiDE). PENDIENTE [TEST]: zona horaria que SIFEN valida
/// (Manual v150: ventana -720 h / +120 h respecto de la transmision). Offset configurable, no fijo en codigo.
/// </summary>
public interface IFiscalClock
{
    DateTimeOffset Now { get; }

    DateOnly Today { get; }
}
