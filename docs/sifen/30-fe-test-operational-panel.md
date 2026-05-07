<!-- scope: sifen-fe | relevant for: test-panel, invoices, list, filters, detail, events, logs -->

# Panel operativo FE TEST

## Purpose

Definir el panel operativo de facturas FE en `TEST_INTERNAL` para listar, filtrar y revisar comprobantes por tenant sin usar SIFEN real.

## Data model

- `Documents`
  - `ExternalDocumentNumber`
  - `ReceiverName`
  - `TotalAmount`
  - `CurrencyCode`
  - `InternalStatus`
  - `CorrelationId`
  - `RetryCount`
  - `IsRetryable`
  - `LastErrorMessage`
  - `CreatedAt`
  - `UpdatedAt`
- `FeInvoiceEvents`
  - historial reciente mostrado en detalle
- `FeTenantLogs`
  - logs tecnicos recientes mostrados en detalle

## API / endpoints

- `GET /api/fe/tenants/{tenantId}/invoices`
- `GET /api/fe/invoices/{invoiceId}`
- `GET /api/fe/invoices/{invoiceId}/events`
- `GET /api/fe/tenants/{tenantId}/logs`
- `POST /api/fe/invoices/{invoiceId}/prepare-test`

## Business rules

- El listado devuelve solo facturas del tenant solicitado.
- Si el `tenantId` del request no coincide con el tenant resuelto, se bloquea el acceso.
- El filtro `status` trabaja sobre `InternalStatus`.
- El filtro `customerName` trabaja sobre `ReceiverName`.
- El listado devuelve `page`, `pageSize`, `totalCount` y `totalPages`.
- La UI mantiene filtros activos al paginar.
- El detalle FE expone eventos recientes y logs tecnicos recientes.
- `TechnicalDetail` solo debe mostrarse en seccion de soporte/admin.
- La seccion de soporte tecnico debe iniciar colapsada por defecto.
- El panel no usa SOAP, firma real ni validacion XSD real.

## Constraints

- No se exponen secretos, CSC, passwords ni certificados.
- Se mantiene compatibilidad con rutas legacy `/invoice`.
- El panel FE TEST no reemplaza el flujo real SIFEN; solo prepara observabilidad y operacion interna.

## Edge cases

- tenant sin contexto
- tenant cruzado
- filtro por estado inexistente
- factura inexistente
- factura sin eventos
- factura sin logs tecnicos
