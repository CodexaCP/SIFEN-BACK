<!-- scope: local-runbook | relevant for: run, login, companies, tramiya, test-local -->

# Local Runbook Codexa SIFEN

## Purpose

Explicar como levantar el sistema localmente, entrar con `SuperAdmin`, crear `TramiYa` como compania cliente de `SIFEN` y preparar el siguiente paso hacia FE real.

Referencia principal:

- `docs/sifen/24-codexa-superadmin-companies-runbook.md`

## Data model

- `PlatformUser` global
- `Tenant` como compania
- `PlatformUser.TenantId` para admin de compania

## API / endpoints

- `POST /api/auth/login`
- `GET /api/auth/me`
- `GET /api/platform/companies`
- `POST /api/platform/companies`
- `PUT /api/platform/companies/{tenantId}/plan`
- `POST /api/platform/companies/{tenantId}/admins`
- `POST /internal/onboarding/tenants/{tenantId}/taxpayer-profile`
- `POST /internal/onboarding/tenants/{tenantId}/sifen-settings`
- `POST /internal/onboarding/tenants/{tenantId}/certificates`
- `GET /api/fe/diagnostic/{tenantId}`

## Business rules

- `SuperAdmin` entra a todos los modulos de plataforma.
- `TramiYa` puede existir como compania cliente dentro de `SIFEN`.
- El plan se define antes de emitir FE.
- Sin tenant configurado y resuelto, la emision FE falla.

## Constraints

- La UI de Codexa-WEB todavia no administra companias ni tenant context de punta a punta.
- Por ahora la creacion de companias y admins se hace via API.

## Edge cases

- falta `SIFEN_SUPERADMIN_PASSWORD`
- falta `SIFEN_JWT_SIGNING_KEY`
- compania creada pero sin configuracion SIFEN
- compania creada pero sin tenant context en frontend

## Ejecucion local

### 1. Variables de entorno

```powershell
$env:SIFEN_SUPERADMIN_PASSWORD="Admin123!"
$env:SIFEN_JWT_SIGNING_KEY="dev-signing-key-for-sifen-local-tests-123456789"
```

### 2. Aplicar migraciones

```powershell
dotnet ef database update --project C:\Users\Tony\Documents\GitKtraken\FE\src\SifenInvoicing.Infrastructure --startup-project C:\Users\Tony\Documents\GitKtraken\FE\src\SifenInvoicing.Api
```

### 3. Levantar backend FE

```powershell
dotnet run --project C:\Users\Tony\Documents\GitKtraken\FE\src\SifenInvoicing.Api --launch-profile https
```

- HTTP: `http://localhost:5261`
- HTTPS: `https://localhost:7207`

### 4. Levantar frontend Codexa

```powershell
cd C:\Users\Tony\Documents\GitKtraken\Codexa-WEB
npm start
```

- Landing: `http://localhost:4200/#/landing`
- SIFEN login: `http://localhost:4200/#/sifen/login`

### 5. Login de plataforma

- usuario: `admin@sifen.local`
- password: valor de `SIFEN_SUPERADMIN_PASSWORD`

## Flujo para crear TramiYa como cliente de SIFEN

### 1. Login como SuperAdmin

Llamar:

```http
POST /api/auth/login
```

```json
{
  "username": "admin@sifen.local",
  "password": "Admin123!"
}
```

Guardar el token JWT.

### 2. Crear la compania TramiYa

```http
POST /api/platform/companies
Authorization: Bearer <token>
```

```json
{
  "slug": "tramiya",
  "displayName": "TramiYa",
  "maxInvoicesPerMonth": 150,
  "maxUsers": 5
}
```

Resultado esperado:
- se crea el `Tenant`
- queda activo
- ya tiene plan comercial base

### 3. Crear admin general para TramiYa

```http
POST /api/platform/companies/{tenantId}/admins
Authorization: Bearer <token>
```

```json
{
  "email": "admin@tramiya.local",
  "password": "AdminTramiYa123!"
}
```

Resultado esperado:
- usuario `TenantAdmin`
- asociado a `TenantId = TramiYa`

### 4. Confirmar plan de TramiYa

```http
PUT /api/platform/companies/{tenantId}/plan
Authorization: Bearer <token>
```

```json
{
  "maxInvoicesPerMonth": 150,
  "maxUsers": 5,
  "active": true
}
```

### 5. Cargar configuracion SIFEN del tenant

Todavia falta hacerlo por API interna:

1. `taxpayer-profile`
2. `sifen-settings`
3. `certificates`

Sin eso, `TramiYa` no puede emitir FE.

### 6. Validar readiness

```http
GET /api/fe/diagnostic/{tenantId}
Authorization: Bearer <token>
```

Debes obtener `ready = true` antes de intentar FE real.

## Estado funcional actual

### Ya funciona

- login de plataforma
- usuario `SuperAdmin`
- listado/creacion de companias por API
- plan por compania
- admin por compania
- frontend entra a `SIFEN`

### Aun falta

- CRUD de companias desde UI
- seleccion automatica de tenant en frontend
- estilo visual final azul cielo para SIFEN
- configuracion SIFEN por UI
- homologacion real SIFEN test
