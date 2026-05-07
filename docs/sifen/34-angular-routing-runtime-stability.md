<!-- scope: frontend-routing | relevant for: angular, routing, hash-location, guards, sifen-login, runtime -->

# Estabilidad de routing y runtime Angular

## Purpose

Documentar la causa raíz de navegación incorrecta y pantallas blancas al abrir rutas como `/sifen/login` en `Codexa-WEB`.

## Data model

- No aplica modelo de datos.

## API / endpoints

- No aplica endpoint backend nuevo.
- Este ajuste depende de que el host frontend responda `index.html` para rutas cliente.

## Business rules

- Las rutas de Angular deben abrir con paths limpios:
  - `/sifen/login`
  - `/sifen/dashboard`
  - `/dashboard`
- Los guards deben devolver:
  - `boolean`
  - `UrlTree`
  - `Observable<boolean | UrlTree>`
- Los guards no deben ejecutar `navigate(...)` y luego devolver `false`.

## Constraints

- No cambiar arquitectura global.
- No refactorizar módulos.
- No tocar backend salvo soporte de fallback SPA ya existente.

## Edge cases

- URL abierta manualmente sin sesión
- token vencido en storage
- redirect a login desde guard
- navegación interna a rutas lazy de SIFEN
- backend respondiendo `200` pero con path cliente mal interpretado
