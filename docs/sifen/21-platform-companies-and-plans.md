<!-- scope: platform-companies | relevant for: companies, tenants, plans, admins, superadmin -->

# Platform Companies And Plans

## Purpose

Permitir que `SuperAdmin` gestione companias/tenants dentro de la plataforma Codexa y prepare clientes FE como `TramiYa`.

## Data model

- `Tenants`
  - `Id`
  - `Slug`
  - `DisplayName`
  - `Status`
  - `MaxInvoicesPerMonth`
  - `MaxUsers`
- `PlatformUsers`
  - `Email`
  - `RoleId`
  - `TenantId?`
- `PlatformRoles`
  - `SuperAdmin`
  - `TenantAdmin`

## API / endpoints

- `GET /api/platform/companies`
- `GET /api/platform/companies/{tenantId}`
- `POST /api/platform/companies`
- `PUT /api/platform/companies/{tenantId}/plan`
- `POST /api/platform/companies/{tenantId}/admins`

## Business rules

- Solo `SuperAdmin` puede listar, crear y modificar companias.
- Una compania FE se representa como un `Tenant`.
- El plan comercial se aplica sobre el `Tenant`:
  - `MaxInvoicesPerMonth`
  - `MaxUsers`
  - `Status` activo/inactivo
- Si el frontend omite `active` al actualizar plan, el backend conserva el estado actual del tenant.
- El backend acepta tanto `maxInvoicesPerMonth` / `maxUsers` como `invoiceLimitPerMonth` / `userLimit` para compatibilidad con `Codexa-WEB`.
- El admin general de compania se crea como `PlatformUser` con rol `TenantAdmin`.
- `TenantAdmin` queda asociado a un `TenantId`.
- No se duplican slugs ni emails.

## Constraints

- Sin RBAC completo en esta fase.
- No crea configuracion SIFEN automaticamente; solo prepara la compania y su admin.

## Edge cases

- slug duplicado
- email duplicado
- tenant inexistente al crear admin
- plan con limites invalidos
