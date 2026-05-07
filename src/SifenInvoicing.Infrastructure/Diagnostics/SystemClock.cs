using SifenInvoicing.Application.Diagnostics;

namespace SifenInvoicing.Infrastructure.Diagnostics;

public sealed class SystemClock : ISystemClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
