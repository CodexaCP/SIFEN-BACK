<!-- scope: sifen-fe | relevant for: soap, gateway, transport, test, diagnostic -->

# Gateway SOAP SIFEN Test

## Purpose

Cerrar el flujo operativo minimo del envio `rEnviDe` sin marcar aprobaciones simuladas como reales.

## Data model

- `ConfigurationSifenSubmissionGateway` construye `soap:Envelope` + `rEnviDe`.
- `SifenSubmissionResult` ahora devuelve datos tecnicos de transporte:
  - `Endpoint`
  - `RequestPayload`
  - `TransportCode`
  - `TransportMessage`
  - `HttpStatusCode`
  - `IsDiagnostic`

## API / endpoints

- Impacta indirectamente `POST /invoice/`.

## Business rules

- El gateway exige XML firmado antes de armar el SOAP.
- Si falta configuracion de endpoint, devuelve `MISSING_ENDPOINT_CONFIGURATION`.
- Si el modo es `Diagnostic`, no llama a SIFEN y devuelve `DIAGNOSTIC_MODE_ENABLED`.
- Si falta configuracion de certificado de transporte, devuelve `MISSING_CLIENT_CERTIFICATE_CONFIGURATION`.
- Timeout y errores de transporte se convierten en resultados controlados y persistibles.
- El servicio persiste request tecnico de forma segura mediante hash SHA-256 y no loguea secretos.

## Constraints

- No se declara aprobacion real sin respuesta real de SIFEN.
- El request completo no se loguea en texto plano; se persiste hash, largo y endpoint.

## Edge cases

- HTTP error del endpoint.
- Timeout.
- Error de transporte.
- Modo diagnostico activo.

## Checklist para prueba real SIFEN test

- `Sifen:Transport:Mode=Live`
- `Sifen:Environments:Test:BaseUrl`
- `Sifen:Environments:Test:Wsdl:Receive`
- `Sifen:Transport:ClientCertificatePath`
- `Sifen:Transport:ClientCertificatePasswordEnvironmentVariable`
- Variable de entorno `SIFEN_TRANSPORT_CERTIFICATE_PASSWORD`
- Certificado XML del tenant valido para firma
- XSD oficial configurado para FE TipoDoc 01
