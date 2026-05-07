# Preguntas pendientes para GPT o validacion documental

Este archivo mantiene las dudas que deben aclararse antes de codificar reglas SIFEN sensibles.

## Para el siguiente bloque

GPT, I need clarification about: Should the first implementation target a single taxpayer installation or a multi-tenant SaaS model from day one? I need the architectural decision only, not SIFEN rules.

Motivo:

- El usuario menciono escenarios de muchos clientes.
- Multi-tenant afecta certificados, auditoria, limites, almacenamiento, reintentos, colas y aislamiento de datos.

Estado:

- Decision de trabajo adoptada por ahora: disenar como multi-tenant desde el inicio, implementando incrementalmente.

## Antes de persistencia multi-tenant

GPT, I need clarification about: For a Paraguay SIFEN SaaS serving many taxpayers, what tenant isolation model is most appropriate for production: shared database with TenantId, schema per tenant, or database per tenant? Consider certificate isolation, auditability, support operations, cost and future scaling.

Motivo:

- Define EF Core, indices, migraciones, auditoria, backups y soporte.
- Tambien afecta como aislar certificados, timbrados, CSC y colas por cliente.

## Antes de implementar CDC

GPT, I need clarification about: Which exact Manual Tecnico v150 sections and later Notas Tecnicas modify the CDC structure or verification digit algorithm for Paraguay SIFEN?

Motivo:

- El CDC identifica de forma unica el DE.
- Se usa como `Id` firmado.
- Un error en CDC causa rechazo y duplicidad.

## Antes de onboarding real de tenants y certificados

GPT, I need clarification about: For a SIFEN SaaS, what is the safest production onboarding flow for a taxpayer tenant before sending documents to SIFEN test, including certificate metadata, secret storage, RUC validation, timbrado, CSC, and approval gates?

Motivo:

- Ya existe persistencia multi-tenant base.
- El siguiente paso debe crear tenants y configuracion sin exponer secretos.
- No se debe permitir enviar a SIFEN si falta certificado, CSC, timbrado o datos aprobados.

Estado:

- Onboarding interno inicial implementado.
- Se mantiene bloqueado el readiness final por falta de capa real de secretos/certificados.

## Antes de capa de secretos y firma

GPT, I need clarification about: In a .NET SaaS for Paraguay SIFEN, what is the recommended local-development and production design for storing and using per-tenant PFX/P12 certificates and CSC values without exposing private keys to the app database or support staff?

Motivo:

- El sistema ya guarda `SecretReference`.
- El siguiente bloque debe resolver secretos de forma segura.
- La firma XML y mTLS dependen de certificado real, password, RUC y usos permitidos.

Estado:

- Capa local de secretos/certificados implementada.
- Falta implementar provider productivo Vault/HSM y carga real de material autorizado.

## Antes de firma XML SIFEN

GPT, I need clarification about: For Paraguay SIFEN v150 plus current technical notes, what exact XML Digital Signature profile must be used for DE signing, including canonicalization, transforms, digest method, signature method, Reference URI, signed node, KeyInfo content, and validation requirements?

Motivo:

- La capa de certificados ya puede cargar PFX/P12 en memoria.
- El siguiente paso sensible es firma XML.
- No se debe asumir el perfil XMLDSig ni nodos exactos sin fuente oficial vigente.

Estado:

- Perfil de firma XML implementado segun v150 + NT 16.
- Falta validar certificado contra RUC/cadena/CRL y firmar XML DE real generado desde XSD.

## Antes de CDC y XML DE real

GPT, I need clarification about: For Paraguay SIFEN v150 plus current technical notes, what is the exact CDC structure and verification digit algorithm, and which official examples/vectors should be used to test the generator?

Motivo:

- El signer ya firma por `Id = CDC`.
- El CDC real debe generarse antes del XML DE productivo.
- Un CDC incorrecto rompe firma, QR, consulta y envio SIFEN.

## Antes de generar XML DE

GPT, I need clarification about: For Paraguay SIFEN v150 plus current technical notes, which official XSD files and mandatory field groups should be implemented first for a minimal Factura Electronica test submission?

Motivo:

- La firma ya esta separada.
- El siguiente paso debe construir XML valido contra XSD.
- No se deben inventar grupos/campos obligatorios.

## Antes de implementar XML por tipo de DE

GPT, I need clarification about: Which official XSD files are authoritative for each document type in SIFEN v150, including FE, AFE, NCE, NDE and NRE, and whether any NT_E_KUATIA notes modify mandatory fields?

Motivo:

- No se debe construir XML manualmente desde memoria.
- Los campos obligatorios pueden haber cambiado por notas tecnicas.

## Antes de implementar firma digital

GPT, I need clarification about: For SIFEN v150, what are the exact XMLDSig canonicalization, transforms, reference URI and certificate requirements, including any changes introduced by technical notes?

Motivo:

- La firma invalida bloquea todo el flujo.
- La conexion SOAP tambien requiere mTLS con certificado valido.

## Antes de implementar QR/KuDE

GPT, I need clarification about: What is the exact QR hash input order and URL format for SIFEN test and production under Manual Tecnico v150 plus current technical notes?

Motivo:

- El QR depende del CSC y del hash correcto.
- En test hay CSC genericos, pero produccion usa CSC del contribuyente.

## Antes de integrar con SIFEN test

GPT, I need clarification about: What certificate, RUC, timbrado, CSC and test data are required by DNIT before invoking SIFEN test web services from a custom backend?

Motivo:

- La Guia de Pruebas exige autenticacion mutua y datos validos.
- Sin certificado habilitado solo podemos hacer contract tests locales.
