using System.Globalization;
using SifenInvoicing.Domain.Common;
using SifenInvoicing.Domain.Tenants;

namespace SifenInvoicing.Application.XmlDe;

/// <summary>
/// TaxpayerProfile -> gEmis (Fase 4.2). No rellena nada: si el perfil no tiene un dato obligatorio del Manual (D101-D132)
/// se lanza <see cref="DomainException"/> listando TODOS los faltantes. Hoy el perfil NO modela la actividad economica
/// (gActEco, D130, 1-9) ni el tipo de regimen (cTipReg, opcional): la actividad debe aportarse aparte (modelo incompleto).
/// </summary>
public static class SifenDeEmitterMapper
{
    public static SifenDeEmitter Map(TaxpayerProfile profile, IReadOnlyList<SifenDeEconomicActivity>? economicActivities)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var missing = new List<string>();
        void Need(bool ok, string field)
        {
            if (!ok) missing.Add(field);
        }

        Need(profile.TaxpayerType is 1 or 2, "iTipCont (TaxpayerProfile.TaxpayerType)");
        Need(!string.IsNullOrWhiteSpace(profile.Address), "dDirEmi (TaxpayerProfile.Address)");
        var house = ParseInt(profile.HouseNumber);
        Need(house.HasValue, "dNumCas (TaxpayerProfile.HouseNumber)");
        var department = ParseInt(profile.DepartmentCode);
        Need(department.HasValue && !string.IsNullOrWhiteSpace(profile.DepartmentDescription), "cDepEmi/dDesDepEmi (Department*)");
        var city = ParseInt(profile.CityCode);
        Need(city.HasValue && !string.IsNullOrWhiteSpace(profile.CityDescription), "cCiuEmi/dDesCiuEmi (City*)");
        Need(!string.IsNullOrWhiteSpace(profile.Phone), "dTelEmi (TaxpayerProfile.Phone)");
        Need(!string.IsNullOrWhiteSpace(profile.Email), "dEmailE (TaxpayerProfile.Email)");
        Need(economicActivities is { Count: >= 1 }, "gActEco (no existe en TaxpayerProfile)");

        var district = ParseInt(profile.DistrictCode);
        Need(string.IsNullOrWhiteSpace(profile.DistrictCode) == string.IsNullOrWhiteSpace(profile.DistrictDescription) &&
             (string.IsNullOrWhiteSpace(profile.DistrictCode) || district.HasValue), "cDisEmi/dDesDisEmi (District*) deben informarse juntos");

        if (missing.Count > 0)
        {
            throw new DomainException("Perfil fiscal del emisor incompleto para el DE: " + string.Join("; ", missing) + ".");
        }

        return new SifenDeEmitter(
            profile.RucNumber,
            profile.RucCheckDigit,
            profile.TaxpayerType!.Value,
            profile.LegalName,
            profile.Address!,
            house,
            department,
            profile.DepartmentDescription,
            city,
            profile.CityDescription,
            profile.Phone,
            profile.Email,
            economicActivities!,
            district,
            profile.DistrictDescription);
    }

    private static int? ParseInt(string? value) =>
        int.TryParse(value?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : null;
}
