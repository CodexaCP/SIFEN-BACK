using Microsoft.Extensions.Configuration;
using SifenInvoicing.Application.Diagnostics;
using SifenInvoicing.Application.Fiscal;

namespace SifenInvoicing.Infrastructure.Fiscal;

/// <summary>Hora fiscal = UTC + offset configurado (Sifen:Fiscal:UtcOffsetMinutes, por defecto -180). PENDIENTE [TEST].</summary>
public sealed class OffsetFiscalClock : IFiscalClock
{
    private readonly ISystemClock _clock;
    private readonly TimeSpan _offset;

    public OffsetFiscalClock(ISystemClock clock, IConfiguration configuration)
    {
        _clock = clock;
        _offset = TimeSpan.FromMinutes(int.TryParse(configuration["Sifen:Fiscal:UtcOffsetMinutes"], out var minutes) ? minutes : -180);
    }

    public DateTimeOffset Now => _clock.UtcNow.ToOffset(_offset);

    public DateOnly Today => DateOnly.FromDateTime(Now.DateTime);
}
