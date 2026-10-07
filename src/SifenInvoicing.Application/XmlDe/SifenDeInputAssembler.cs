using System.Globalization;
using SifenInvoicing.Application.Fiscal;
using SifenInvoicing.Application.Invoices;
using SifenInvoicing.Application.Numbering;
using SifenInvoicing.Domain.Common;

namespace SifenInvoicing.Application.XmlDe;

/// <summary>Datos del DE que no dependen del numero reservado: se validan ANTES de reservar numeracion.</summary>
public sealed record SifenDeRequestPlan(
    SifenDeReceiver Receiver,
    SifenDeOperation Operation,
    IReadOnlyList<SifenDeItem> Items);

/// <summary>
/// Domino -> <see cref="SifenDeBuildInput"/> (Fase 4.3). Solo transforma y exige: cualquier dato obligatorio ausente se
/// informa por nombre de campo del Manual y se rechaza; nada se completa con valores por defecto del codigo.
/// </summary>
public static class SifenDeInputAssembler
{
    /// <summary>
    /// Receptor, operacion e items a partir del comando comercial. <paramref name="defaultTransactionType"/> y
    /// <paramref name="defaultPresenceIndicator"/> son decisiones de configuracion del operador (no del codigo).
    /// </summary>
    public static SifenDeRequestPlan PlanRequest(
        CreateInvoiceCommand command,
        int? defaultTransactionType,
        int? defaultPresenceIndicator)
    {
        ArgumentNullException.ThrowIfNull(command);
        var problems = new List<string>();

        if (command.Currency != InvoiceCurrency.PYG)
        {
            problems.Add("moneda distinta de PYG (fuera de alcance del DE01 minimo)");
        }

        if (command.SaleCondition != InvoiceSaleCondition.Cash)
        {
            problems.Add("condicion de venta distinta de contado (fuera de alcance del DE01 minimo)");
        }

        var transactionType = command.TransactionType ?? defaultTransactionType;
        if (transactionType is not (1 or 2))
        {
            problems.Add("iTipTra (D011) no informado: enviar transactionType o configurar Sifen:De:DefaultTransactionType (1 o 2)");
        }

        var presence = command.PresenceIndicator ?? defaultPresenceIndicator;
        if (presence is not (1 or 2))
        {
            problems.Add("iIndPres (E011) no informado: enviar presenceIndicator o configurar Sifen:De:DefaultPresenceIndicator (1 o 2)");
        }

        var receiver = PlanReceiver(command, problems);

        var items = new List<SifenDeItem>();
        var number = 0;
        foreach (var item in command.Items)
        {
            number++;
            if (string.IsNullOrWhiteSpace(item.Code))
            {
                problems.Add($"item {number}: dCodInt (E701) es obligatorio");
            }

            string unitDescription = string.Empty;
            if (item.UnitCode is not { } unit)
            {
                problems.Add($"item {number}: cUniMed (E709, Tabla 5) es obligatorio");
            }
            else if (!SifenDeUnitsOfMeasure.TryGetRepresentation(unit, out unitDescription))
            {
                problems.Add($"item {number}: cUniMed {unit} no existe en la Tabla 5 del Manual v150");
            }

            if (!string.IsNullOrWhiteSpace(item.Code) && item.UnitCode.HasValue && unitDescription.Length > 0)
            {
                items.Add(new SifenDeItem(item.Code.Trim(), item.Description.Trim(), item.UnitCode.Value, unitDescription));
            }
        }

        if (problems.Count > 0)
        {
            throw new DomainException("Datos insuficientes o no soportados para el DE01: " + string.Join("; ", problems) + ".");
        }

        return new SifenDeRequestPlan(
            receiver!,
            new SifenDeOperation(transactionType!.Value, presence!.Value),
            items);
    }

    private static SifenDeReceiver? PlanReceiver(CreateInvoiceCommand command, List<string> problems)
    {
        var name = command.ReceptorNombre?.Trim() ?? string.Empty;
        var document = command.ReceptorDocumento?.Trim() ?? string.Empty;

        if (command.ReceptorTipoDocumento == InvoiceReceiverDocumentType.Ruc)
        {
            var parts = document.Split('-', 2);
            if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0)
            {
                problems.Add("receptor contribuyente: el RUC debe informarse como 'numero-DV' (D206/D207)");
                return null;
            }

            if (command.ReceptorTaxpayerKind is not (1 or 2))
            {
                problems.Add("receptor contribuyente: iTiContRec (D205) es obligatorio (1 = persona fisica, 2 = persona juridica)");
                return null;
            }

            return new SifenDeReceiver(
                SifenDeReceiverNature.Taxpayer,
                SifenDeOperationType.B2B,
                name,
                TaxpayerKind: command.ReceptorTaxpayerKind,
                Ruc: parts[0],
                RucCheckDigit: parts[1]);
        }

        // Cedula de identidad -> iTipIDRec 1 (Cedula paraguaya, Manual D210).
        return new SifenDeReceiver(
            SifenDeReceiverNature.NonTaxpayer,
            SifenDeOperationType.B2C,
            name,
            IdentityDocumentType: 1,
            IdentityDocumentNumber: document);
    }

    /// <summary>Arma la entrada del builder con el CDC YA calculado (el builder nunca lo regenera).</summary>
    public static SifenDeBuildInput Assemble(
        SifenDeRequestPlan plan,
        SifenDeEmitter emitter,
        ReservedNumber reserved,
        string cdc,
        DateTime fiscalLocalNow,
        SifenDeEnvironment environment,
        FiscalDocumentModel fiscal)
    {
        // dFecFirma: se fija aqui, antes del digest (esta dentro de DE); la firma real es de una fase posterior.
        // PENDIENTE: zona horaria definitiva y relacion con el instante real de firma (Manual A004: AAAA-MM-DDThh:mm:ss).
        var withoutFraction = new DateTime(fiscalLocalNow.Ticks - fiscalLocalNow.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Unspecified);

        return new SifenDeBuildInput(
            cdc,
            withoutFraction,
            withoutFraction,
            environment,
            new SifenDeStamp(
                reserved.StampingNumber,
                reserved.EstablishmentCode,
                reserved.ExpeditionPointCode,
                reserved.FormattedNumber,
                reserved.StampValidFrom,
                string.IsNullOrWhiteSpace(reserved.Series) ? null : reserved.Series),
            emitter,
            plan.Receiver,
            plan.Operation,
            plan.Items,
            fiscal);
    }

    public static int? ParseOptionalInt(string? value) =>
        int.TryParse(value?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : null;
}
