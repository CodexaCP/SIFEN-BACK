<!-- scope: sifen-fe | relevant for: panel, listado, detalle, xml, kude, operador -->

# Panel operativo FE minimo

## Purpose

Permitir consulta operativa basica de facturas electronicas TipoDoc 01 desde la UI FE existente.

## Data model

- `InvoiceSearchQuery` filtra por estado, CDC, fecha desde/hasta y numero externo.
- `InvoiceListItem` expone datos de listado y banderas de descarga.
- `InvoiceDetail` sigue siendo la fuente del panel de detalle.

## API / endpoints

- `GET /invoice`
- `GET /invoice/{id}`
- `GET /invoice/{id}/xml`
- `GET /invoice/{id}/kude`
- `GET /invoice/app`

## Business rules

- El listado muestra solo documentos FE persistidos del tenant actual.
- Los filtros disponibles son estado, CDC, rango de fecha y numero de factura.
- El detalle muestra estado SIFEN, mensaje, tracking, XML resumido y logs relevantes.
- La descarga de XML usa el payload persistido.
- La descarga de KuDE es opcional; si no existe o falla, la UI informa el problema sin romper el flujo.

## Constraints

- No cambia el layout global.
- No agrega dashboards ni documentos fuera de FE.
- Reutiliza la pagina FE estatica actual.

## Edge cases

- tenant ausente o invalido.
- filtro por estado desconocido.
- factura inexistente.
- factura sin KuDE disponible.
- XML persistido vacio o inconsistente.
