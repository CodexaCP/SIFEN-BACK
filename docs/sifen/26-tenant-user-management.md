<!-- scope: platform-users | relevant for: multi-tenant, users, plans, tenantadmin, operator, viewer -->

# Tenant User Management

## Purpose

Definir la gestion multi-tenant de usuarios para Codexa/SIFEN, con un `TenantAdmin` principal obligatorio por compania y control de limite de usuarios activos por plan.

## Data model

- `Tenants`
  - `Id`
  - `DisplayName`
  - `PlanName`
  - `MaxUsers`
  - `MaxInvoicesPerMonth`
  - `Status`
- `PlatformUsers`
  - `Id`
  - `TenantId`
  - `FullName`
  - `Email`
  - `RoleId`
  - `IsActive`
- `PlatformRoles`
  - `SuperAdmin`
  - `TenantAdmin`
  - `Operator`
  - `Viewer`

## API / endpoints

- `POST /api/platform/companies`
- `PUT /api/platform/companies/{tenantId}/plan`
- `GET /api/platform/companies/{tenantId}/users`
- `POST /api/platform/companies/{tenantId}/users`
- `PUT /api/platform/companies/{tenantId}/users/{userId}`
- `GET /auth/me`
- `GET /api/fe/plan/{tenantId}`

## Business rules

- `SuperAdmin` puede crear compania, definir plan y dejar creado el `TenantAdmin` principal.
- El `TenantAdmin` principal queda activo y asociado al `TenantId`.
- `MaxUsers` representa el maximo total de usuarios activos del tenant, incluyendo el `TenantAdmin`.
- Solo se permiten roles `TenantAdmin`, `Operator` y `Viewer` dentro del tenant.
- `TenantAdmin` puede crear, editar, activar e inactivar usuarios de su tenant.
- `Operator` puede emitir y consultar.
- `Viewer` solo puede consultar.
- Antes de crear o reactivar un usuario se cuenta la cantidad activa del tenant.
- Si `activeUsers >= MaxUsers`, el backend bloquea con el mensaje:
  - `Tu plan permite un máximo de X usuarios activos. Inactiva un usuario o actualiza tu plan.`
- El frontend debe mostrar uso actual `X / MaxUsers`, deshabilitar creacion cuando el limite este alcanzado y no exponer errores tecnicos.
- Toda operacion de usuarios debe filtrar por `TenantId` en backend.

## Constraints

- No exponer usuarios de otro tenant.
- No permitir CRUD cruzado aunque se manipule URL o `userId`.
- No romper operacion FE existente.
- No mover logica a otros modulos ni tocar `TramiYa`.

## Edge cases

- email duplicado global
- tenant inexistente
- rol invalido para tenant
- reactivacion cuando el plan ya alcanzo el limite
- sesion sin tenant resuelto
