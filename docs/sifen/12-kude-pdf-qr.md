<!-- scope: sifen-fe | relevant for: kude, pdf, qr, descarga -->

# KuDE PDF + QR minimo

## Purpose

Generar un PDF descargable minimo para FE TipoDoc 01 con QR basado en el CDC.

## Data model

- `GenerateKudePdfAsync(invoiceId)` genera el PDF bajo demanda.
- `InvoiceKudePdfResult` devuelve:
  - `FileName`
  - `Content`
  - `QrPayload`
  - `HasQr`

## API / endpoints

- Nuevo endpoint: `GET /invoice/{id}/kude`

## Business rules

- El KuDE incluye:
  - CDC
  - numero de factura
  - emisor
  - receptor
  - fecha
  - moneda
  - items
  - totales
  - impuestos
  - estado
  - QR
- El QR usa el `CDC` numerico como payload.
- Si KuDE falla, no afecta la emision; se registra `kude.failed`.

## Constraints

- Sin diseno avanzado.
- Solo FE TipoDoc 01.
- El PDF se genera desde la informacion persistida y el XML FE.

## Edge cases

- XML sin items.
- XML sin totales.
- documento no encontrado.
- error de generacion PDF o QR.
