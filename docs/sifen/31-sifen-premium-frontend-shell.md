<!-- scope: sifen-fe | relevant for: frontend, shell, dashboard, coming-soon, navigation, premium-ui -->

# Rediseño premium del módulo SIFEN

## Purpose

Aplicar una capa visual SaaS premium al módulo SIFEN sin tocar backend FE TEST ni romper rutas existentes.

## Data model

- No agrega modelo de datos nuevo.
- Reutiliza:
  - listado FE por tenant
  - detalle FE
  - diagnóstico FE
  - estados internos `DRAFT`, `GENERATED`, `VALIDATED_TEST`, `TEST_ERROR`, `READY_FOR_REAL`, `BLOCKED_BY_CONFIG`

## API / endpoints

- No crea endpoints nuevos.
- Consume los endpoints ya existentes del módulo FE TEST:
  - `GET /api/fe/tenants/{tenantId}/invoices`
  - `GET /api/fe/invoices/{invoiceId}`
  - `GET /api/fe/invoices/{invoiceId}/events`
  - `GET /api/fe/tenants/{tenantId}/diagnostic`
  - `POST /api/fe/invoices/{invoiceId}/prepare-test`

## Business rules

- El branding visible del módulo es `SIFEN` con marca visual `SF`.
- El menú lateral debe exponer navegación SaaS consistente para:
  - dashboard
  - emitir factura
  - facturas
  - clientes
  - productos / servicios
  - notas de crédito
  - plantillas KuDE
  - configuración SIFEN
  - diagnóstico
  - integración API
  - reportes
- Solo deben funcionar con lógica real:
  - `/sifen/dashboard`
  - `/sifen/invoices`
  - `/sifen/invoices/:id`
  - ruta de diagnóstico tenant ya existente
- Las rutas todavía no implementadas deben resolver a `SifenComingSoonComponent`.
- `SifenComingSoonComponent` debe permitir:
  - volver atrás
  - ir al dashboard
- El dashboard muestra métricas TEST basadas en `InternalStatus`, nunca estados reales de emisión SIFEN.
- El detalle FE separa mensaje para usuario y soporte técnico.
- Los detalles técnicos quedan colapsados por defecto.

## Constraints

- No cambiar backend FE.
- No llamar SOAP.
- No usar firma digital real.
- No usar certificados reales.
- No afectar landing global ni otros módulos.
- Mantener estilos encapsulados dentro de `fe-module`.

## Edge cases

- tenant no resuelto
- sesión sin permisos de emisión
- dashboard sin datos
- diagnóstico sin `tenantId` explícito
- rutas placeholder navegadas desde sidebar
