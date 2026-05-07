namespace SifenInvoicing.Application.Diagnostics;

public interface ISystemClock
{
    DateTimeOffset UtcNow { get; }
}
