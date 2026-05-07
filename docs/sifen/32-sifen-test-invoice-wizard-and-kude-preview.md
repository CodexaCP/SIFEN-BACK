<!-- scope: sifen-fe | relevant for: wizard, emitir-factura, kude-preview, test-internal, frontend -->

# Wizard Emitir Factura TEST + Preview KuDE

## Purpose

Definir la experiencia frontend de emisión FE en entorno `TEST_INTERNAL` con wizard paso a paso y vista previa KuDE premium reutilizable.

## Data model

- Wizard frontend:
  - datos de factura
  - datos del cliente
  - ítems con IVA `10%`, `5%`, `EXENTA`
- Preview frontend:
  - `SifenKudePreviewModel`
  - `SifenKudePreviewItem`
- Resultado:
  - factura creada con endpoint actual
  - preparación TEST con `prepare-test`

## API / endpoints

- `POST /api/fe/invoices`
- `POST /api/fe/invoices/{invoiceId}/prepare-test`
- `GET /api/fe/plan/{tenantId}`
- `GET /api/fe/tenants/{tenantId}/diagnostic`

## Business rules

- La ruta principal del wizard es `/sifen/invoices/new`.
- `/sifen/invoices/emit` se mantiene por compatibilidad.
- El flujo visual tiene 4 pasos:
  - datos factura
  - ítems
  - vista previa KuDE TEST
  - confirmación TEST
- El preview KuDE usa datos de prueba y nunca representa un comprobante fiscal válido.
- El paso final crea la factura con el endpoint actual y luego ejecuta `prepare-test`.
- Los estados esperados en esta fase son:
  - `DRAFT`
  - `GENERATED`
  - `VALIDATED_TEST`
  - `BLOCKED_BY_CONFIG`
  - `TEST_ERROR`
- El preview debe dejar explícito:
  - `QR TEST — no válido para SET`
  - `Vista previa con datos de prueba. No es comprobante válido.`

## Constraints

- No implementar SOAP.
- No implementar firma digital real.
- No implementar XSD oficial.
- No usar QR fiscal real.
- No usar CDC real oficial.
- No afectar backend ni otros módulos.
- Mantener estilos encapsulados en `fe-module`.

## Edge cases

- tenant no resuelto
- plan vencido
- diagnóstico bloqueante
- formulario incompleto
- ítems sin cantidad o precio válido
- preparación TEST con respuesta no exitosa
