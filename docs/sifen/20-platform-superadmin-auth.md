<!-- scope: platform-auth | relevant for: superadmin, login, jwt, seed, users, roles -->

# Platform SuperAdmin Auth

## Purpose

Proveer autenticacion minima de plataforma para operar el sistema FE desde cero con un usuario inicial `SuperAdmin`.

## Data model

- `PlatformRoles`
  - `Id`
  - `Name`
  - `Description`
  - `Permissions`
- `PlatformUsers`
  - `Id`
  - `Email`
  - `PasswordHash`
  - `RoleId`
  - `IsActive`

## API / endpoints

- `POST /auth/login`
- `GET /auth/me`
- `POST /api/auth/login`
- `GET /api/auth/me`

## Business rules

- El usuario inicial es `admin@sifen.local`.
- La contraseña inicial no se hardcodea.
- La contraseña se toma desde la variable de entorno `SIFEN_SUPERADMIN_PASSWORD`.
- Si el rol `SuperAdmin` no existe, se crea.
- Si el usuario inicial no existe y la variable de entorno está presente, se crea.
- Si el usuario ya existe, no se duplica ni se sobreescribe su contraseña.
- El rol `SuperAdmin` expone estos permisos declarativos:
  - `companies.read.all`
  - `companies.create`
  - `sifen.configure`
  - `invoices.issue.any-tenant`

## Constraints

- No usa ASP.NET Identity completo.
- No implementa RBAC completo en esta fase.
- No cambia el flujo FE ni el onboarding actual fuera del login básico.
- Para frontend local, la API acepta CORS desde `localhost:4200`.

## Edge cases

- Si falta `SIFEN_SUPERADMIN_PASSWORD`, el usuario inicial no se crea y se deja advertencia de arranque.
- Si falta la clave de firma JWT, el login falla con error controlado.
- Si el usuario está inactivo, no puede autenticarse.
