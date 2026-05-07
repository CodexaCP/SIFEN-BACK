<!-- scope: sifen-fe | relevant for: tenant, plan, limits, emission -->

# Limites de plan por tenant

## Purpose

Aplicar control comercial minimo por tenant antes de emitir FE.

## Data model

- `Tenant.MaxInvoicesPerMonth`
- `Tenant.MaxUsers`
- `Tenant.Status`

## API / endpoints

- `POST /internal/onboarding/tenants`
  - acepta opcionalmente:
    - `maxInvoicesPerMonth`
    - `maxUsers`
- `GET /api/fe/plan/{tenantId}`
  - devuelve:
    - `usedInvoicesThisMonth`
    - `maxInvoicesPerMonth`
    - `maxUsers`
    - `usersUsed = null`
    - `usersMessage = TODO`
    - `active`
    - `limitReached`

## Business rules

- `Tenant.Status` existente se usa como control `activo/inactivo`.
- Si el tenant no esta `Active`, la emision FE se bloquea.
- Si `MaxInvoicesPerMonth` tiene valor y el tenant ya emitio ese maximo dentro del mes UTC actual, la emision FE se bloquea.
- Si `MaxInvoicesPerMonth` o `MaxUsers` no estan configurados, se consideran sin limite.
- `MaxUsers` queda persistido para control comercial posterior; en esta subtarea no se aplica a login/usuarios.
- El panel FE puede mostrar plan actual por `tenantId` explicito.

## Constraints

- Sin cambios fuera del modulo FE y onboarding interno.
- No cambia retry, KuDE ni otros tipos de documento.

## Edge cases

- tenant inexistente.
- tenant suspendido/archivado.
- limite mensual configurado en `1` y una factura ya emitida en el mismo mes.
- limites nulos: emision permitida.
