<!-- scope: sifen-fe | relevant for: wizard, persistence, items, totals, detail, test-cdc, test-qr -->

# Persistencia completa de factura FE TEST

## Purpose

Persistir todos los datos capturados por el wizard `/sifen/invoices/new` para que la factura TEST alimente futuras capas de XML real, KuDE real, PDF, auditoria y detalle operativo sin depender de SOAP ni firma real.

## Data model

- `Documents`
  - `DocumentType`
  - `EstablishmentCode`
  - `ExpeditionPointCode`
  - `ExternalDocumentNumber`
  - `IssuedAt`
  - `SaleCondition`
  - `CurrencyCode`
  - `Notes`
  - `ReceiverName`
  - `ReceiverDocument`
  - `ReceiverAddress`
  - `ReceiverEmail`
  - `ReceiverPhone`
  - `SubtotalAmount`
  - `Vat5Amount`
  - `Vat10Amount`
  - `ExemptAmount`
  - `TotalVatAmount`
  - `TotalAmount`
  - `TestCdc`
  - `TestQrText`
  - `IsFiscalPreviewValid`
- `DocumentLines`
  - `DocumentId`
  - `LineNumber`
  - `Description`
  - `Quantity`
  - `UnitPrice`
  - `VatRate`
  - `VatAmount`
  - `ExemptAmount`
  - `SubtotalAmount`
  - `TotalAmount`
- `FeInvoiceEvents`
  - evento `InvoiceCreatedTest`
- `FeTenantLogs`
  - log `fe.invoice.create`

## API / endpoints

- `POST /api/fe/invoices`
  - acepta DTO completo del wizard
  - calcula totales en backend
  - persiste líneas
  - devuelve `invoiceId`, `internalStatus`, `correlationId`, `totalAmount`, `message`
- `GET /api/fe/invoices/{invoiceId}`
  - devuelve documento, cliente, líneas, totales, `TestCdc`, `TestQrText`, `IsFiscalPreviewValid`
- `POST /api/fe/invoices/{invoiceId}/prepare-test`
  - se mantiene sin cambios de contrato

## Business rules

- El backend no confía en totales enviados por frontend.
- `VatRate` permitido: `10`, `5`, `0`.
- Toda factura nueva queda con `CorrelationId`.
- Al crear factura se registra:
  - `InternalStatus = DRAFT`
  - evento `InvoiceCreatedTest`
  - log `INFO` por tenant
- `TestCdc` y `TestQrText` son solo visuales para entorno TEST.
- `IsFiscalPreviewValid` permanece en `false`.
- El detalle debe dejar visible:
  - documento
  - receptor
  - ítems
  - totales
  - observaciones
  - aviso de no validez fiscal

## Constraints

- No implementar SOAP.
- No implementar firma digital real.
- No validar XSD oficial.
- No generar CDC oficial.
- No generar QR fiscal real.
- No borrar datos existentes.
- Mantener compatibilidad con endpoints actuales.

## Edge cases

- tenant inactivo
- plan sin capacidad mensual
- documento sin tipo
- receptor sin nombre o documento
- factura sin ítems
- ítem con cantidad `<= 0`
- ítem con precio `< 0`
- `vatRate` distinto de `10`, `5`, `0`
- detalle sin líneas históricas para facturas creadas antes de esta base
