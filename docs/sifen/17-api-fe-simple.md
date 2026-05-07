<!-- scope: sifen-fe | relevant for: api, invoice, simple-json, emission -->

# API FE simple

## Purpose

Exponer un endpoint simple para crear FE TipoDoc 01 con JSON reducido.

## Data model

- `CreateSimpleInvoiceRequest`
- `SimpleInvoiceCustomerRequest`

## API / endpoints

- `POST /api/fe/invoices`
- `GET /api/fe/invoices/status/{cdc}`
- `GET /api/fe/invoices/{id}/xml`
- `GET /api/fe/invoices/{id}/kude`

## Business rules

- Reutiliza el flujo FE actual:
  - generar XML
  - firmar
  - enviar o dejar preparado
  - guardar estado
- Valida antes de delegar:
  - cliente
  - items
  - total
- Usa defaults internos actuales para:
  - `SaleCondition = Cash`
  - `SistemaFacturacion = 1`
  - `TipoContribuyente = 1`
  - `TipoEmision = 1`
- El endpoint de estado mapea la respuesta interna/SIFEN a estados claros:
  - `pendiente`
  - `aprobado`
  - `rechazado`
  - `error`
- El endpoint de XML reutiliza el XML persistido.
- El endpoint de KuDE devuelve PDF cuando existe.
- Si el KuDE aun no esta disponible, devuelve placeholder controlado con `futurePath` sin romper el flujo.

## Constraints

- Solo FE `TipoDoc 01`.
- No cambia el servicio central de emision.

## Edge cases

- cliente vacio.
- items vacios.
- total distinto a la suma de items.
