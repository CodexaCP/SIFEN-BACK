<!-- scope: sifen-fe | relevant for: piloto, salida, checklist, go-live -->

# Checklist tecnico de salida a piloto FE

## Purpose

Definir la verificacion minima antes de ofrecer o pilotear Factura Electronica TipoDoc 01.

## Data model

- Aplica al modulo FE actual.
- No cubre otros tipos de documento.

## API / endpoints

- `POST /invoice`
- `GET /invoice/{id}`
- `GET /invoice/status/{cdc}`
- `GET /invoice/{id}/xml`
- `GET /invoice/{id}/kude`
- `GET /ops/fe-diagnostic`
- `GET /invoice/app`

## Business rules

- El piloto no debe abrirse sin evidencia de aprobacion real en SIFEN test.
- Si falta validacion real o evidencia operativa, el estado es `NO LISTO`.

## Constraints

- Solo FE `TipoDoc 01`.
- No reemplaza homologacion formal ni soporte productivo completo.

## Edge cases

- tenant parcialmente configurado.
- certificado vencido.
- rechazo definitivo.
- retry sobre error no transitorio.
- tenant sin evidencia de FE previa.

## Checklist

### 1. Configuracion tenant

- `OK` tenant activo.
- `OK` perfil tributario activo.
- `OK` `CSC` configurado para ambiente test.
- `OK` diagnostico `GET /ops/fe-diagnostic` en estado `ready`.
- `PENDIENTE/TODO` configuracion persistida propia de establecimiento, punto y numeracion por tenant.

### 2. Certificado real

- `OK` certificado XML real cargado para el tenant.
- `OK` certificado vigente.
- `OK` clave privada accesible.
- `OK` fingerprint y metadatos consistentes.
- `PENDIENTE` evidencia operativa de certificado mTLS real si el flujo lo requiere en prueba homologada.

### 3. Ambiente SIFEN test

- `OK` ambiente activo configurado en test.
- `OK` endpoint SOAP test configurado.
- `OK` timeout y errores tecnicos controlados.
- `OK` modo diagnostico desactivado para prueba real.

### 4. Emision caso minimo

- `OK` XML FE caso minimo generado.
- `OK` validacion previa activa antes de firmar/enviar.
- `OK` firma XML aplicada.
- `OK` persistencia de XML, XML firmado, estado y logs.

### 5. Aprobacion real

- `OBLIGATORIO` emitir al menos una FE real aprobada en SIFEN test.
- `OBLIGATORIO` guardar evidencia de:
  - `CDC`
  - respuesta real `rRetEnviDe/rProtDe`
  - tracking/protocolo
  - estado final persistido
- Sin este punto: `NO LISTO PARA PILOTO`.

### 6. KuDE

- `OK` generacion PDF bajo demanda.
- `OK` QR presente.
- `OK` descarga funcional desde endpoint FE.
- `OK` manejo controlado si KuDE falla.

### 7. Consulta por CDC

- `OK` consulta local por `CDC`.
- `PENDIENTE` reconciliacion/consulta activa contra SIFEN test si se requiere soporte operativo real.

### 8. Descarga XML/PDF

- `OK` descarga XML.
- `OK` descarga KuDE PDF.
- `OK` mensajes claros cuando el recurso no existe.

### 9. Manejo de rechazo

- `OK` parser distingue aprobado, rechazado, observado y error tecnico.
- `OK` rechazo definitivo no entra en retry normal.
- `PENDIENTE` reproceso explicito separado para rechazo definitivo.

### 10. Soporte y retry

- `OK` logs tecnicos persistidos.
- `OK` retry controlado solo para fallas tecnicas/transitorias.
- `OK` auditoria de retry con usuario, fecha y resultado.
- `PENDIENTE` reconciliacion automatica por `CDC`.

### 11. Rollback operativo

- `OK` no reintentar aprobados.
- `OK` no reintentar rechazo definitivo sin reproceso explicito.
- `OK` modo diagnostico disponible para bloquear envios no listos.
- `PENDIENTE/TODO` playbook operativo formal para:
  - desactivar emision por tenant
  - pasar a modo diagnostico
  - frenar retries
  - revisar ultimos errores/envios
  - reanudar operacion controlada

## Criterio de salida a piloto

Estado `LISTO PARA PILOTO` solo si:

- tenant readiness en `ready`
- certificado real vigente
- ambiente test operativo
- caso minimo emitido
- al menos una aprobacion real confirmada
- KuDE y XML descargables
- rechazo y retry verificados
- soporte operativo minimo disponible

Si falta cualquiera de los puntos obligatorios, el modulo queda en `NO LISTO PARA PILOTO`.
