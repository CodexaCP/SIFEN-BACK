<!-- scope: codexa-platform-runbook | relevant for: superadmin, companies, tramiya, plans, sifen, execution, onboarding -->

# Codexa SuperAdmin Companies Runbook

## Purpose

Explicar como debe operar Codexa como plataforma madre, como crear `TramiYa` como compania cliente de `SIFEN`, como asignar planes y que falta para pasar de base tecnica a operacion real.

## Data model

- `PlatformUser`
  - usuario global de plataforma
  - `SuperAdmin` o `TenantAdmin`
- `PlatformRole`
  - `SuperAdmin`
  - `TenantAdmin`
- `Tenant`
  - compania/cliente dentro de Codexa
  - `Slug`
  - `DisplayName`
  - `Status`
  - `MaxInvoicesPerMonth`
  - `MaxUsers`
- `TaxpayerProfile`
  - perfil tributario real del tenant FE
- `TenantSifenSettings`
  - ambiente, CSC y configuracion FE
- `TenantCertificateMetadata`
  - certificados de firma/transporte

## API / endpoints

- autenticacion global
  - `POST /api/auth/login`
  - `GET /api/auth/me`
- companias / tenants
  - `GET /api/platform/companies`
  - `GET /api/platform/companies/{tenantId}`
  - `POST /api/platform/companies`
  - `PUT /api/platform/companies/{tenantId}/plan`
  - `POST /api/platform/companies/{tenantId}/admins`
- preparacion FE
  - `POST /internal/onboarding/tenants/{tenantId}/taxpayer-profile`
  - `POST /internal/onboarding/tenants/{tenantId}/sifen-settings`
  - `POST /internal/onboarding/tenants/{tenantId}/certificates`
  - `GET /api/fe/diagnostic/{tenantId}`
- operacion FE
  - `POST /api/fe/invoices`
  - `GET /api/fe/invoices/status/{cdc}`
  - `GET /api/fe/invoices/{id}/xml`
  - `GET /api/fe/invoices/{id}/kude`

## Business rules

- `SuperAdmin` es el usuario global que entra a los modulos activos de Codexa.
- Cada compania que uses en `SIFEN` es un `Tenant`.
- `TramiYa` puede ser una compania cliente de `SIFEN`.
- El plan comercial se define por tenant:
  - `MaxInvoicesPerMonth`
  - `MaxUsers`
  - `Status`
- `TenantAdmin` administra una compania concreta.
- No se debe emitir FE si el tenant esta inactivo o supera limite mensual.
- No se debe considerar `SIFEN` vendible en FE real sin al menos una respuesta real desde SIFEN test.

## Constraints

- El backend FE ya soporta companias, planes y admins.
- El frontend `Codexa-WEB` todavia no cierra todo el flujo SaaS de companias desde UI.
- El estilo visual azul cielo pedido para `SIFEN` pertenece a `Codexa-WEB`, no a este workspace FE.
- La homologacion real SIFEN test sigue pendiente de credenciales/configuracion reales.

## Edge cases

- falta `SIFEN_SUPERADMIN_PASSWORD`
- falta `SIFEN_JWT_SIGNING_KEY`
- login correcto pero sin tenant seleccionado en UI
- tenant creado sin configuracion fiscal
- tenant creado sin CSC/certificados
- tenant activo pero sin numeracion/punto/establecimiento
- tenant con plan vencido o limite mensual alcanzado

## Flujo objetivo de plataforma

### 1. SuperAdmin global

Debe existir un `SuperAdmin` global de Codexa que pueda:

- ver todas las companias
- crear companias
- activar/inactivar companias
- definir planes
- crear admins de compania
- entrar a los modulos activos

En backend FE eso ya existe de forma minima.

### 2. Crear `TramiYa` como compania cliente

`TramiYa` debe crearse como `Tenant` dentro de la plataforma.

Ejemplo:

```http
POST /api/platform/companies
Authorization: Bearer <token-superadmin>
Content-Type: application/json

{
  "slug": "tramiya",
  "displayName": "TramiYa",
  "maxInvoicesPerMonth": 150,
  "maxUsers": 5
}
```

Resultado esperado:

- se crea un `Tenant`
- queda activo
- tiene limites comerciales iniciales

### 3. Definir plan de `TramiYa`

Si deseas cambiar el plan despues:

```http
PUT /api/platform/companies/{tenantId}/plan
Authorization: Bearer <token-superadmin>
Content-Type: application/json

{
  "maxInvoicesPerMonth": 150,
  "maxUsers": 5,
  "active": true
}
```

Con esto defines:

- tope mensual de facturas
- tope de usuarios
- si la compania puede operar o queda suspendida

### 4. Crear admin general para `TramiYa`

```http
POST /api/platform/companies/{tenantId}/admins
Authorization: Bearer <token-superadmin>
Content-Type: application/json

{
  "email": "admin@tramiya.local",
  "password": "Admin123!"
}
```

Resultado esperado:

- se crea `PlatformUser`
- se asocia a `TenantId`
- obtiene rol `TenantAdmin`

### 5. Preparar `TramiYa` para FE

Antes de emitir, el tenant necesita:

- RUC real o de test
- razon social
- CSC
- ambiente `Test` o `Production`
- establecimiento
- punto de expedicion
- numeracion
- certificado de firma
- certificado de transporte si aplica

Esto hoy se completa por onboarding/backend, no por UI SaaS cerrada.

### 6. Validar readiness

```http
GET /api/fe/diagnostic/{tenantId}
Authorization: Bearer <token-superadmin o token-tenantadmin>
```

Debe devolver:

- `ready: true` solo si el tenant esta listo
- `missing[]` con lo que falte

### 7. Emitir factura FE

Cuando el tenant ya este listo:

- login
- resolver tenant
- emitir
- guardar XML
- firmar
- enviar
- persistir respuesta

## Estado real actual

### Ya funciona en backend FE

- login global minimo
- rol `SuperAdmin`
- rol `TenantAdmin`
- crear companias
- definir planes
- crear admin por compania
- diagnostico FE por tenant
- bloqueo por tenant inactivo
- bloqueo por limite de facturas
- flujo tecnico FE base

### Todavia no esta cerrado

- seleccion de compania desde UI Codexa
- resolver automaticamente el `tenantId` al entrar al modulo
- CRUD visual de companias
- configuracion visual SIFEN por compania
- homologacion real SIFEN test
- tema visual `SIFEN` con gradiente azul cielo como `TramiYa`

## Como ejecutar el sistema

### Backend FE

Definir variables:

```powershell
$env:SIFEN_SUPERADMIN_PASSWORD="Admin123!"
$env:SIFEN_JWT_SIGNING_KEY="dev-signing-key-for-sifen-local-tests-123456789"
```

Aplicar migraciones:

```powershell
dotnet ef database update --project C:\Users\Tony\Documents\GitKtraken\FE\src\SifenInvoicing.Infrastructure --startup-project C:\Users\Tony\Documents\GitKtraken\FE\src\SifenInvoicing.Api
```

Levantar API:

```powershell
dotnet run --project C:\Users\Tony\Documents\GitKtraken\FE\src\SifenInvoicing.Api --launch-profile https
```

URLs:

- `http://localhost:5261`
- `https://localhost:7207`

### Frontend Codexa

Este paso pertenece al proyecto `Codexa-WEB`.

```powershell
cd C:\Users\Tony\Documents\GitKtraken\Codexa-WEB
npm start
```

URLs:

- landing: `http://localhost:4200/#/landing`
- SIFEN login: `http://localhost:4200/#/sifen/login`

## Como entrar y probar

### 1. Login global

Entrar con:

- usuario: `admin@sifen.local`
- password: valor de `SIFEN_SUPERADMIN_PASSWORD`

### 2. Crear `TramiYa`

Usar el token del login y llamar:

- `POST /api/platform/companies`

### 3. Poner plan

Usar:

- `PUT /api/platform/companies/{tenantId}/plan`

### 4. Crear admin de compania

Usar:

- `POST /api/platform/companies/{tenantId}/admins`

### 5. Preparar FE

Completar onboarding FE para ese tenant.

### 6. Validar readiness

Usar:

- `GET /api/fe/diagnostic/{tenantId}`

### 7. Emitir

Usar:

- `POST /api/fe/invoices`

Con tenant context correcto.

## Que necesitas para probar con el RUC de tu esposa

### En test

Necesitas:

- RUC habilitado para pruebas o contribuyente valido para el escenario
- CSC de test
- certificado de firma valido
- XSD oficial configurado
- ambiente `Test`
- establecimiento
- punto de expedicion
- numeracion

### En real

Ademas de lo anterior:

- habilitacion real
- credenciales reales
- configuracion fiscal real
- validacion operacional final

## Recomendacion operativa

El siguiente paso correcto no es mas backend FE base.

Debes cerrar en este orden:

1. usar `SuperAdmin` global para crear `TramiYa`
2. definir plan `5 usuarios / 150 facturas`
3. crear `TenantAdmin`
4. cerrar la resolucion de tenant en frontend
5. agregar pantalla visual de companias/configuracion en `Codexa-WEB`
6. aplicar tema visual azul cielo a `SIFEN`
7. cargar credenciales reales de test y ejecutar la primera homologacion SIFEN
